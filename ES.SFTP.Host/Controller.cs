using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ES.SFTP.Host.Business.Configuration;
using ES.SFTP.Host.Business.Interop;
using ES.SFTP.Host.Business.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ES.SFTP.Host
{
    public class Controller
    {
        private const string HomeBasePath = "/home";
        private const string SftpGroup = "sftp";
        private const string SshDirectoryPath = "/etc/ssh";
        private const string SshHostKeysDirPath = "/etc/ssh/keys";
        private const string SshConfigPath = "/etc/ssh/sshd_config";
        private readonly ILogger<Controller> _logger;
        private readonly IOptionsMonitor<SftpConfiguration> _sftpOptionsMonitor;
        private Process _serverProcess;

        public Controller(ILogger<Controller> logger, IOptionsMonitor<SftpConfiguration> sftpOptionsMonitor)
        {
            _logger = logger;
            _sftpOptionsMonitor = sftpOptionsMonitor;
            _sftpOptionsMonitor.OnChange((_, __) =>
            {
                _logger.LogWarning("Configuration changed. Restarting service.");
                Stop().ContinueWith(___ => Start()).Wait();
            });
        }

        private Dictionary<string, string> HostKeyFiles { get; } = new Dictionary<string, string>
        {
            {"ssh_host_ed25519_key", "-t ed25519 -f {0} -N \"\""},
            {"ssh_host_rsa_key", "-t rsa -b 4096 -f {0} -N \"\""}
        };

        public async Task Start()
        {
            _logger.LogDebug("Starting");
            var config = await PrepareAndValidateConfiguration();
            await ImportOrCreateHostKeyFiles();
            await ConfigureOpenSSH(config);
            await SetupHomeBaseDirectory();
            await SyncUsersAndGroups(config);
            await StartOpenSSH();
            _logger.LogInformation("Started");
        }

        public Task Stop()
        {
            _logger.LogDebug("Stopping");
            _serverProcess.Kill(true);
            _serverProcess.OutputDataReceived -= OnSSHOutput;
            _serverProcess.ErrorDataReceived -= OnSSHOutput;
            _logger.LogInformation("Stopped");
            return Task.CompletedTask;
        }

        private Task<SftpConfiguration> PrepareAndValidateConfiguration()
        {
            _logger.LogDebug("Preparing and validating configuration");

            var config = _sftpOptionsMonitor.CurrentValue ?? new SftpConfiguration();

            config.Global ??= new GlobalConfiguration();
            if (string.IsNullOrWhiteSpace(config.Global.HomeDirectory)) config.Global.HomeDirectory = "/home";


            config.Global.Directories ??= new List<string>();
            config.Global.Chroot ??= new ChrootDefinition();
            if (string.IsNullOrWhiteSpace(config.Global.Chroot.Directory)) config.Global.Chroot.Directory = "%h";
            if (string.IsNullOrWhiteSpace(config.Global.Chroot.StartPath)) config.Global.Chroot.StartPath = null;


            config.Users ??= new List<UserDefinition>();

            var validUsers = new List<UserDefinition>();
            for (var index = 0; index < config.Users.Count; index++)
            {
                var userDefinition = config.Users[index];
                if (string.IsNullOrWhiteSpace(userDefinition.Username))
                {
                    _logger.LogWarning("Users[index] has a null or whitespace username. Skipping user.", index);
                    continue;
                }

                userDefinition.Chroot ??= new ChrootDefinition();
                if (string.IsNullOrWhiteSpace(userDefinition.Chroot.Directory))
                    userDefinition.Chroot.Directory = config.Global.Chroot.Directory;
                if (string.IsNullOrWhiteSpace(userDefinition.Chroot.StartPath))
                    userDefinition.Chroot.StartPath = config.Global.Chroot.StartPath;

                if (userDefinition.Chroot.Directory == config.Global.Chroot.Directory &&
                    userDefinition.Chroot.StartPath == config.Global.Chroot.StartPath)
                    userDefinition.Chroot = null;
                userDefinition.Directories ??= new List<string>();

                validUsers.Add(userDefinition);
            }

            config.Users = validUsers;
            _logger.LogInformation("Configuration contains '{userCount}' user(s)", config.Users.Count);

            return Task.FromResult(config);
        }

        private async Task ImportOrCreateHostKeyFiles()
        {
            _logger.LogInformation("Importing host key files");

            if (!Directory.Exists(SshHostKeysDirPath))
                Directory.CreateDirectory(SshHostKeysDirPath);

            foreach (var file in Directory.GetFiles(SshHostKeysDirPath))
            {
                var targetFile = Path.Combine(SshDirectoryPath, Path.GetFileName(file));
                _logger.LogDebug("Copying '{sourceFile}' to '{targetFile}'", file, targetFile);
                File.Copy(file, targetFile);
                await ProcessUtil.QuickRun("chown", $"root:root \"{targetFile}\"");
                await ProcessUtil.QuickRun("chmod", $"700 \"{targetFile}\"");
            }

            foreach (var hostKeyFile in HostKeyFiles)
            {
                var filePath = Path.Combine(SshDirectoryPath, hostKeyFile.Key);
                if (File.Exists(filePath)) continue;
                _logger.LogDebug("Generating host key file '{file}'", filePath);
                var keygenArgs = string.Format(hostKeyFile.Value, filePath);
                await ProcessUtil.QuickRun("ssh-keygen", keygenArgs);
            }
        }

        private async Task ConfigureOpenSSH(SftpConfiguration configuration)
        {
            var builder = new StringBuilder();
            builder.AppendLine();
            builder.AppendLine("# SSH Protocol");
            builder.AppendLine("Protocol 2");
            builder.AppendLine();
            builder.AppendLine("# Host Keys");
            builder.AppendLine("HostKey /etc/ssh/ssh_host_ed25519_key");
            builder.AppendLine("HostKey /etc/ssh/ssh_host_rsa_key");
            builder.AppendLine();
            builder.AppendLine("# Disable DNS for fast connections");
            builder.AppendLine("UseDNS no");
            builder.AppendLine();
            builder.AppendLine("# Logging");
            builder.AppendLine("LogLevel VERBOSE");
            builder.AppendLine();
            builder.AppendLine("# Subsystem");
            builder.AppendLine("Subsystem sftp internal-sftp");
            builder.AppendLine();
            builder.AppendLine();
            builder.AppendLine("# Match SFTP group");
            builder.Append($"Match Group {SftpGroup}");
            if (configuration.Users.Any(s => s.Chroot != null))
            {
                var exceptionUsers = configuration.Users
                    .Where(s => s.Chroot != null)
                    .Select(s => s.Username).Distinct()
                    .Select(s => $"!{s.Trim()}").ToList();
                var exceptionList = string.Join(",", exceptionUsers);
                builder.Append(" User \"*,");
                builder.Append(exceptionList);
                builder.Append("\"");
            }

            builder.AppendLine();
            builder.AppendLine($"ChrootDirectory {configuration.Global.Chroot.Directory}");
            builder.AppendLine("X11Forwarding no");
            builder.AppendLine("AllowTcpForwarding no");
            builder.AppendLine(
                !string.IsNullOrWhiteSpace(configuration.Global.Chroot.StartPath)
                    ? $"ForceCommand internal-sftp -d {configuration.Global.Chroot.StartPath}"
                    : "ForceCommand internal-sftp");
            builder.AppendLine();
            builder.AppendLine();
            foreach (var user in configuration.Users.Where(s => s.Chroot != null).ToList())
            {
                builder.AppendLine($"# Match User {user.Username}");
                builder.AppendLine($"Match User {user.Username}");
                builder.AppendLine($"ChrootDirectory {user.Chroot.Directory}");
                builder.AppendLine("X11Forwarding no");
                builder.AppendLine("AllowTcpForwarding no");
                builder.AppendLine(
                    !string.IsNullOrWhiteSpace(user.Chroot.StartPath)
                        ? $"ForceCommand internal-sftp -d {user.Chroot.StartPath}"
                        : "ForceCommand internal-sftp");
                builder.AppendLine();
            }

            var resultingConfig = builder.ToString();
            await File.WriteAllTextAsync(SshConfigPath, resultingConfig);
        }

        private async Task SetupHomeBaseDirectory()
        {
            if (!Directory.Exists(HomeBasePath)) Directory.CreateDirectory(HomeBasePath);
            await ProcessUtil.QuickRun("chown", $"root:root \"{HomeBasePath}\"");
        }

        private async Task SyncUsersAndGroups(SftpConfiguration configuration)
        {
            _logger.LogInformation("Synchronizing users and groups");

            if (!await GroupUtil.GroupExists(SftpGroup))
            {
                _logger.LogInformation("Creating group '{group}'", SftpGroup);
                await GroupUtil.GroupCreate(SftpGroup, true);
            }

            foreach (var user in configuration.Users)
            {
                _logger.LogInformation("Processing user '{user}'", user.Username);

                if (!await UserUtil.UserExists(user.Username))
                {
                    _logger.LogDebug("Creating user '{user}'", user.Username);
                    await UserUtil.UserCreate(user.Username, true);
                }

                if ((await GroupUtil.GroupListUsers(SftpGroup)).All(s => s != user.Username))
                {
                    _logger.LogDebug("Adding user '{user}' to '{group}'", user.Username, SftpGroup);
                    await GroupUtil.GroupAddUser(SftpGroup, user.Username);
                }

                _logger.LogDebug("Updating the password for user '{user}'", user.Username);
                await UserUtil.UserSetPassword(user.Username, user.Password, user.PasswordIsEncrypted);

                if (user.UID.HasValue)
                    if (await UserUtil.UserGetId(user.Username) != user.UID.Value)
                    {
                        _logger.LogDebug("Updating the UID for user '{user}'", user.Username);
                        await UserUtil.UserSetId(user.Username, user.UID.Value);
                    }

                if (user.GID.HasValue)
                {
                    var virtualGroup = $"sftp-gid-{user.GID.Value}";
                    if (!await GroupUtil.GroupExists(virtualGroup))
                    {
                        _logger.LogDebug("Creating group '{group}' with GID '{gid}'", virtualGroup, user.GID.Value);
                        await GroupUtil.GroupCreate(virtualGroup, true, user.GID.Value);
                    }

                    _logger.LogDebug("Adding user '{user}' to '{group}'", user.Username, virtualGroup);
                    await GroupUtil.GroupAddUser(virtualGroup, user.Username);
                }

                var homeDirPath = Path.Combine(HomeBasePath, user.Username);
                if (!Directory.Exists(homeDirPath))
                {
                    _logger.LogDebug("Creating the home directory for user '{user}'", user.Username);
                    Directory.CreateDirectory(homeDirPath);
                }

                homeDirPath = new DirectoryInfo(homeDirPath).FullName;
                await ProcessUtil.QuickRun("chown", $"root:root {homeDirPath}");
                await ProcessUtil.QuickRun("chmod", $"700 {homeDirPath}");

                var chroot = user.Chroot ?? configuration.Global.Chroot;
                var chrootPath = string.Join("%%h",
                    chroot.Directory.Split("%%h").Select(s => s.Replace("%h", homeDirPath)).ToList());
                chrootPath = string.Join("%%u",
                    chrootPath.Split("%%u").Select(s => s.Replace("%u", user.Username)).ToList());
                await ProcessUtil.QuickRun("chown", $"root:root {chrootPath}");
                await ProcessUtil.QuickRun("chmod", $"755 {chrootPath}");

                var directories = new List<string>();
                directories.AddRange(configuration.Global.Directories);
                directories.AddRange(user.Directories);
                foreach (var directory in directories.Distinct().OrderBy(s => s).ToList())
                {
                    var dirPath = Path.Combine(homeDirPath, directory);
                    if (!Directory.Exists(dirPath))
                    {
                        _logger.LogDebug("Creating directory '{dir}' for user '{user}'", dirPath, user.Username);
                        Directory.CreateDirectory(dirPath);
                    }

                    await ProcessUtil.QuickRun("chown", $"-R {user.Username}:{SftpGroup} {dirPath}");
                }

                var sshDir = Path.Combine(homeDirPath, ".ssh");
                if (!Directory.Exists(sshDir)) Directory.CreateDirectory(sshDir);
                var sshKeysDir = Path.Combine(sshDir, "keys");
                if (!Directory.Exists(sshKeysDir)) Directory.CreateDirectory(sshKeysDir);
                var sshAuthKeysPath = Path.Combine(sshDir, "authorized_keys");
                if (File.Exists(sshAuthKeysPath)) File.Delete(sshAuthKeysPath);
                var authKeysBuilder = new StringBuilder();
                foreach (var file in Directory.GetFiles(sshKeysDir))
                    authKeysBuilder.AppendLine(await File.ReadAllTextAsync(file));
                await File.WriteAllTextAsync(sshAuthKeysPath, authKeysBuilder.ToString());
                await ProcessUtil.QuickRun("chown", $"{user.Username} {sshAuthKeysPath}");
                await ProcessUtil.QuickRun("chmod", $"600 {sshAuthKeysPath}");
            }
        }

        private async Task StartOpenSSH()
        {
            var command = await ProcessUtil.QuickRun("killall", "-q -w sshd", false);
            if (command.ExitCode != 0 && command.ExitCode != 1 && !string.IsNullOrWhiteSpace(command.Output))
                throw new Exception($"Could not stop existing sshd processes.{Environment.NewLine}{command.Output}");

            _logger.LogInformation("Starting 'sshd' process");

            _serverProcess = new Process
            {
                StartInfo =
                {
                    FileName = "/usr/sbin/sshd",
                    Arguments = "-D -e",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
            _serverProcess.OutputDataReceived -= OnSSHOutput;
            _serverProcess.ErrorDataReceived -= OnSSHOutput;
            _serverProcess.OutputDataReceived += OnSSHOutput;
            _serverProcess.ErrorDataReceived += OnSSHOutput;
            _serverProcess.Start();
            _serverProcess.BeginOutputReadLine();
            _serverProcess.BeginErrorReadLine();
        }

        private void OnSSHOutput(object sender, DataReceivedEventArgs e)
        {
            _logger.LogTrace($"sshd - {e.Data}");
        }
    }
}