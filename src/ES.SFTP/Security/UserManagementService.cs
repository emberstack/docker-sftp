using System.Diagnostics.CodeAnalysis;
using System.Text;
using ES.SFTP.Interop;
using ES.SFTP.Messages.Configuration;
using ES.SFTP.Messages.Events;
using MediatR;

namespace ES.SFTP.Security;

public class UserManagementService : IHostedService, INotificationHandler<ConfigurationChanged>
{
    private const string HomeBasePath = "/home";
    private const string SftpUserInventoryGroup = "sftp-user-inventory";
    private readonly ILogger _logger;
    private readonly IMediator _mediator;

    public UserManagementService(ILogger<UserManagementService> logger, IMediator mediator)
    {
        _logger = logger;
        _mediator = mediator;
    }


    [SuppressMessage("ReSharper", "MethodSupportsCancellation")]
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Starting");


        _logger.LogDebug("Ensuring '{home}' directory exists and has correct permissions", HomeBasePath);
        Directory.CreateDirectory(HomeBasePath);
        await ProcessUtil.QuickRun("chown", $"root:root \"{HomeBasePath}\"");

        _logger.LogDebug("Ensuring group '{group}' exists", SftpUserInventoryGroup);
        if (!await GroupUtil.GroupExists(SftpUserInventoryGroup))
        {
            _logger.LogInformation("Creating group '{group}'", SftpUserInventoryGroup);
            await GroupUtil.GroupCreate(SftpUserInventoryGroup, true);
        }

        await SyncUsersAndGroups();
        _logger.LogInformation("Started");
    }

    [SuppressMessage("ReSharper", "MethodSupportsCancellation")]
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Stopping");
        _logger.LogInformation("Stopped");
        return Task.CompletedTask;
    }

    public async Task Handle(ConfigurationChanged notification, CancellationToken cancellationToken)
    {
        await SyncUsersAndGroups();
    }

    private async Task SyncUsersAndGroups()
    {
        var config = await _mediator.Send(new SftpConfigurationRequest());

        _logger.LogInformation("Synchronizing users and groups");


        //Remove users that do not exist in config anymore
        var existingUsers = await GroupUtil.GroupListUsers(SftpUserInventoryGroup);
        var toRemove = existingUsers.Where(s => !config.Users.Select(t => t.Username).Contains(s)).ToList();
        foreach (var user in toRemove)
        {
            _logger.LogDebug("Removing user '{user}'", user, SftpUserInventoryGroup);
            await UserUtil.UserDelete(user, false);
        }

        //Create groups as specified by the GID value for each user
        foreach (var user in config.Users)
        {
            if (user.GID.HasValue)
            {
                _logger.LogInformation("Processing GID for user '{user}'", user.Username);

                var virtualGroup = $"sftp-gid-{user.GID.Value}";
                if (!await GroupUtil.GroupExists(virtualGroup))
                {
                    _logger.LogDebug("Creating group '{group}' with GID '{gid}'", virtualGroup, user.GID.Value);
                    await GroupUtil.GroupCreate(virtualGroup, true, user.GID.Value);
                }
            }
        }

        foreach (var user in config.Users)
        {
            _logger.LogInformation("Processing user '{user}'", user.Username);

            if (!await UserUtil.UserExists(user.Username))
            {
                _logger.LogDebug("Creating user '{user}'", user.Username);
                await UserUtil.UserCreate(user.Username, true, user.GID);
                _logger.LogDebug("Adding user '{user}' to '{group}'", user.Username, SftpUserInventoryGroup);
                await GroupUtil.GroupAddUser(SftpUserInventoryGroup, user.Username);
            }


            _logger.LogDebug("Updating the password for user '{user}'", user.Username);
            await UserUtil.UserSetPassword(user.Username, user.Password, user.PasswordIsEncrypted);

            if (user.UID.HasValue && await UserUtil.UserGetId(user.Username) != user.UID.Value)
            {
                _logger.LogDebug("Updating the UID for user '{user}'", user.Username);
                await UserUtil.UserSetId(user.Username, user.UID.Value);
            }

            var homeDir = Directory.CreateDirectory(Path.Combine(HomeBasePath, user.Username));
            await ProcessUtil.QuickRun("chown", $"root:root {homeDir.FullName}");
            await ProcessUtil.QuickRun("chmod", $"711 {homeDir.FullName}");

            var sshDir = Directory.CreateDirectory(Path.Combine(homeDir.FullName, ".ssh"));
            var sshKeysDir = Directory.CreateDirectory(Path.Combine(sshDir.FullName, "keys"));
            var sshAuthKeysPath = Path.Combine(sshDir.FullName, "authorized_keys");
            if (File.Exists(sshAuthKeysPath)) File.Delete(sshAuthKeysPath);
            var authKeysBuilder = new StringBuilder();
            foreach (var file in Directory.GetFiles(sshKeysDir.FullName))
            {
                _logger.LogDebug("Adding public key '{file}' for user '{user}'", file, user.Username);
                authKeysBuilder.AppendLine(await File.ReadAllTextAsync(file));
            }

            foreach (var publicKey in user.PublicKeys)
            {
                _logger.LogDebug("Adding public key from config for user '{user}'", user.Username);
                authKeysBuilder.AppendLine(publicKey);
            }

            await File.WriteAllTextAsync(sshAuthKeysPath, authKeysBuilder.ToString());
            await ProcessUtil.QuickRun("chown", $"{user.Username} {sshAuthKeysPath}");
            await ProcessUtil.QuickRun("chmod", $"400 {sshAuthKeysPath}");
        }


        foreach (var groupDefinition in config.Groups)
        {
            _logger.LogInformation("Processing group '{group}'", groupDefinition.Name);

            var groupUsers = groupDefinition.Users ?? new List<string>();
            if (!await GroupUtil.GroupExists(groupDefinition.Name))
            {
                _logger.LogDebug("Creating group '{group}' with GID '{gid}'", groupDefinition.Name,
                    groupDefinition.GID);
                await GroupUtil.GroupCreate(groupDefinition.Name, true, groupDefinition.GID);
            }

            if (groupDefinition.GID.HasValue)
            {
                var currentId = await GroupUtil.GroupGetId(groupDefinition.Name);
                if (currentId != groupDefinition.GID.Value)
                {
                    _logger.LogDebug("Updating group '{group}' with GID '{gid}'", groupDefinition.Name,
                        groupDefinition.GID);
                    await GroupUtil.GroupSetId(groupDefinition.Name, groupDefinition.GID.Value);
                }
            }

            var members = await GroupUtil.GroupListUsers(groupDefinition.Name);
            var toAdd = groupUsers.Where(s => !members.Contains(s)).ToList();
            foreach (var user in toAdd)
            {
                if (!await UserUtil.UserExists(user)) continue;
                _logger.LogDebug("Adding user '{user}' to '{group}'", user, groupDefinition.Name);
                await GroupUtil.GroupAddUser(groupDefinition.Name, user);
            }

            members = await GroupUtil.GroupListUsers(groupDefinition.Name);
            var usersToRemove = members.Where(s => !groupUsers.Contains(s)).ToList();
            foreach (var user in usersToRemove)
            {
                _logger.LogDebug("Removing user '{user}'", user, groupDefinition.Name);
                await GroupUtil.GroupRemoveUser(groupDefinition.Name, user);
            }
        }
    }
}