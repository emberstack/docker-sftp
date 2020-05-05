using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ES.SFTP.Host.Business.Configuration;
using ES.SFTP.Host.Business.Interop;
using ES.SFTP.Host.Business.Security;
using ES.SFTP.Host.Messages;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ES.SFTP.Host
{
    public class Orchestrator : IRequestHandler<PamEventRequest, bool>
    {
        private const string HomeBasePath = "/home";
        private const string SftpUserInventoryGroup = "sftp-user-inventory";
        private const string SshDirectoryPath = "/etc/ssh";
        private const string SshHostKeysDirPath = "/etc/ssh/keys";
        private const string SshConfigPath = "/etc/ssh/sshd_config";

        private readonly List<HostKeyType> _hostKeyTypes = new List<HostKeyType>
        {
            new HostKeyType{Type = "Ed25519", KeygenArgs = "-t ed25519 -f {0} -N \"\"", File = "ssh_host_ed25519_key"},
            new HostKeyType{Type = "Rsa", KeygenArgs = "-t rsa -b 4096 -f {0} -N \"\"", File = "ssh_host_rsa_key"},
        };

        private readonly ILogger<Orchestrator> _logger;
        private readonly IOptionsMonitor<SftpConfiguration> _sftpOptionsMonitor;
        private SftpConfiguration _config;
        private Process _serverProcess;

        public Orchestrator(ILogger<Orchestrator> logger, IOptionsMonitor<SftpConfiguration> sftpOptionsMonitor)
        {
            _logger = logger;
            _sftpOptionsMonitor = sftpOptionsMonitor;
            _sftpOptionsMonitor.OnChange((_, __) =>
            {
                _logger.LogWarning("Configuration changed. Restarting service.");
                Stop().ContinueWith(___ => Start()).Wait();
            });
        }

        public async Task<bool> Handle(PamEventRequest request, CancellationToken cancellationToken)
        {
            if (!string.Equals(request.EventType, "open_session", StringComparison.OrdinalIgnoreCase)) return true;
            _logger.LogInformation("Preparing session for user '{user}'", request.Username);
            await PrepareUserForSftp(request.Username);
            _logger.LogInformation("Session prepared for user '{user}'", request.Username);
            return true;
        }


        public async Task Start()
        {
            _logger.LogDebug("Starting");
            await ConfigureAuthentication();
            await PrepareAndValidateConfiguration();
            await ImportOrCreateHostKeyFiles();
            await ConfigureOpenSSH();
            await SetupHomeBaseDirectory();
            await SyncUsersAndGroups();
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

        private async Task ConfigureAuthentication()
        {
            const string pamDirPath = "/etc/pam.d";
            const string pamHookName = "sftp-hook";
            var pamCommonSessionFile = Path.Combine(pamDirPath, "common-session");
            var pamSftpHookFile = Path.Combine(pamDirPath, pamHookName);


            await ProcessUtil.QuickRun("service", "sssd stop", false);

            File.Copy("./config/sssd.conf", "/etc/sssd/sssd.conf", true);
            await ProcessUtil.QuickRun("chown", "root:root \"/etc/sssd/sssd.conf\"");
            await ProcessUtil.QuickRun("chmod", "600 \"/etc/sssd/sssd.conf\"");


            var scriptsDirectory = Path.Combine(pamDirPath, "scripts");
            if (!Directory.Exists(scriptsDirectory)) Directory.CreateDirectory(scriptsDirectory);
            var hookScriptFile = Path.Combine(new DirectoryInfo(scriptsDirectory).FullName, "sftp-pam-event.sh");
            var eventsScriptBuilder = new StringBuilder();
            eventsScriptBuilder.AppendLine("#!/bin/sh");
            eventsScriptBuilder.AppendLine(
                "curl \"http://localhost:25080/api/events/pam/generic?username=$PAM_USER&type=$PAM_TYPE&service=$PAM_SERVICE\"");
            await File.WriteAllTextAsync(hookScriptFile, eventsScriptBuilder.ToString());
            await ProcessUtil.QuickRun("chown", $"root:root \"{hookScriptFile}\"");
            await ProcessUtil.QuickRun("chmod", $"+x \"{hookScriptFile}\"");


            var hookBuilder = new StringBuilder();
            hookBuilder.AppendLine("# This file is used to signal the SFTP service on user events.");
            hookBuilder.AppendLine($"session required pam_exec.so {new FileInfo(hookScriptFile).FullName}");
            await File.WriteAllTextAsync(pamSftpHookFile, hookBuilder.ToString());
            await ProcessUtil.QuickRun("chown", $"root:root \"{pamSftpHookFile}\"");
            await ProcessUtil.QuickRun("chmod", $"644 \"{pamSftpHookFile}\"");


            if (!(await File.ReadAllTextAsync(pamCommonSessionFile)).Contains($"@include {pamHookName}"))
                await File.AppendAllTextAsync(pamCommonSessionFile, $"@include {pamHookName}{Environment.NewLine}");


            await ProcessUtil.QuickRun("service", "sssd restart", false);
        }

        private Task PrepareAndValidateConfiguration()
        {
            _logger.LogDebug("Preparing and validating configuration");

            var config = _sftpOptionsMonitor.CurrentValue ?? new SftpConfiguration();

            config.Global ??= new GlobalConfiguration();

            config.Global.Directories ??= new List<string>();
            config.Global.Logging ??= new LoggingDefinition();
            config.Global.Chroot ??= new ChrootDefinition();
            config.Global.HostKeys ??= new HostKeyDefinition();
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

            _config = config;
            return Task.CompletedTask;
        }

        private async Task ImportOrCreateHostKeyFiles()
        {
            _logger.LogInformation("Importing host key files");

            if (!Directory.Exists(SshHostKeysDirPath))
                Directory.CreateDirectory(SshHostKeysDirPath);

            foreach (var hostKeyType in _hostKeyTypes)
            {
                var filePath = Path.Combine(SshHostKeysDirPath, hostKeyType.File);
                if (File.Exists(filePath)) continue;
                var keyConfig = (string)_config.Global.HostKeys.GetType().GetProperty(hostKeyType.Type).GetValue(_config.Global.HostKeys, null);
                if (!string.IsNullOrWhiteSpace(keyConfig))
                {
                _logger.LogDebug("Writing host key file '{file}' from config", filePath);
                await File.WriteAllTextAsync(filePath, keyConfig); 
                }
                else
                {
                _logger.LogDebug("Generating host key file '{file}'", filePath);
                var keygenArgs = string.Format(hostKeyType.KeygenArgs, filePath);
                await ProcessUtil.QuickRun("ssh-keygen", keygenArgs);
                }
            }

            foreach (var file in Directory.GetFiles(SshHostKeysDirPath))
            {
                var targetFile = Path.Combine(SshDirectoryPath, Path.GetFileName(file));
                _logger.LogDebug("Copying '{sourceFile}' to '{targetFile}'", file, targetFile);
                File.Copy(file, targetFile, true);
                await ProcessUtil.QuickRun("chown", $"root:root \"{targetFile}\"");
                await ProcessUtil.QuickRun("chmod", $"700 \"{targetFile}\"");
            }
        }

        private async Task ConfigureOpenSSH()
        {
            var builder = new StringBuilder();
            builder.AppendLine();
            builder.AppendLine("UsePAM yes");

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
            builder.AppendLine("LogLevel INFO");
            builder.AppendLine();
            builder.AppendLine("# Subsystem");
            builder.AppendLine("Subsystem sftp internal-sftp");
            builder.AppendLine();
            builder.AppendLine();
            builder.AppendLine("# Match all users");
            builder.Append("Match User \"*");
            if (_config.Users.Any(s => s.Chroot != null))
            {
                var exceptionUsers = _config.Users
                    .Where(s => s.Chroot != null)
                    .Select(s => s.Username).Distinct()
                    .Select(s => $"!{s.Trim()}").ToList();
                var exceptionList = string.Join(",", exceptionUsers);
                builder.Append(",");
                builder.Append(exceptionList);
            }

            builder.Append("\"");


            builder.AppendLine();
            builder.AppendLine($"ChrootDirectory {_config.Global.Chroot.Directory}");
            builder.AppendLine("X11Forwarding no");
            builder.AppendLine("AllowTcpForwarding no");
            builder.AppendLine(
                !string.IsNullOrWhiteSpace(_config.Global.Chroot.StartPath)
                    ? $"ForceCommand internal-sftp -d {_config.Global.Chroot.StartPath}"
                    : "ForceCommand internal-sftp");
            builder.AppendLine();
            builder.AppendLine();
            foreach (var user in _config.Users.Where(s => s.Chroot != null).ToList())
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

        private async Task SyncUsersAndGroups()
        {
            _logger.LogInformation("Synchronizing users and groups");

            if (!await GroupUtil.GroupExists(SftpUserInventoryGroup))
            {
                _logger.LogInformation("Creating group '{group}'", SftpUserInventoryGroup);
                await GroupUtil.GroupCreate(SftpUserInventoryGroup, true);
            }

            var existingUsers = await GroupUtil.GroupListUsers(SftpUserInventoryGroup);
            var toRemove = existingUsers.Where(s => !_config.Users.Select(t => t.Username).Contains(s)).ToList();
            foreach (var user in toRemove)
            {
                _logger.LogDebug("Removing user '{user}'", user, SftpUserInventoryGroup);
                await UserUtil.UserDelete(user, false);
            }


            foreach (var user in _config.Users)
            {
                _logger.LogInformation("Processing user '{user}'", user.Username);

                if (!await UserUtil.UserExists(user.Username))
                {
                    _logger.LogDebug("Creating user '{user}'", user.Username);
                    await UserUtil.UserCreate(user.Username, true);
                    _logger.LogDebug("Adding user '{user}' to '{group}'", user.Username, SftpUserInventoryGroup);
                    await GroupUtil.GroupAddUser(SftpUserInventoryGroup, user.Username);
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

                await PrepareUserForSftp(user.Username);
            }
        }

        private async Task PrepareUserForSftp(string username)
        {
            var user = _config.Users.FirstOrDefault(s => s.Username == username) ?? new UserDefinition
            {
                Username = username,
                Chroot = _config.Global.Chroot,
                Directories = _config.Global.Directories
            };

            var homeDirPath = Path.Combine(HomeBasePath, username);
            if (!Directory.Exists(homeDirPath))
            {
                _logger.LogDebug("Creating the home directory for user '{user}'", username);
                Directory.CreateDirectory(homeDirPath);
            }

            homeDirPath = new DirectoryInfo(homeDirPath).FullName;
            await ProcessUtil.QuickRun("chown", $"root:root {homeDirPath}");
            await ProcessUtil.QuickRun("chmod", $"700 {homeDirPath}");

            var chroot = user.Chroot ?? _config.Global.Chroot;

            //Parse chroot path by replacing markers
            var chrootPath = string.Join("%%h",
                chroot.Directory.Split("%%h").Select(s => s.Replace("%h", homeDirPath)).ToList());
            chrootPath = string.Join("%%u",
                chrootPath.Split("%%u").Select(s => s.Replace("%u", username)).ToList());

            //Create chroot directory and set owner to root and correct permissions
            if (!Directory.Exists(chrootPath)) Directory.CreateDirectory(chrootPath);
            await ProcessUtil.QuickRun("chown", $"root:root {chrootPath}");
            await ProcessUtil.QuickRun("chmod", $"755 {chrootPath}");

            var chrootDirectory = new DirectoryInfo(chrootPath);

            var directories = new List<string>();
            directories.AddRange(_config.Global.Directories);
            directories.AddRange(user.Directories);
            foreach (var directory in directories.Distinct().OrderBy(s => s).ToList())
            {
                var dirPath = Path.Combine(chrootDirectory.FullName, directory);
                if (!Directory.Exists(dirPath))
                {
                    _logger.LogDebug("Creating directory '{dir}' for user '{user}'", dirPath, username);
                    Directory.CreateDirectory(dirPath);
                }

                var directoryInfo = new DirectoryInfo(dirPath);

                try
                {
                    if (IsSubDirectory(chrootDirectory, directoryInfo))
                    {
                        var dir = directoryInfo;
                        while (dir.FullName != chrootDirectory.FullName)
                        {
                            await ProcessUtil.QuickRun("chown", $"{username}:{SftpUserInventoryGroup} {dir.FullName}");
                            dir = dir.Parent ?? chrootDirectory;
                        }
                    }
                    else
                    {
                        _logger.LogWarning(
                            "Directory '{dir}' is not withing chroot path '{chroot}'. Setting direct permissions.",
                            directoryInfo.FullName, chrootDirectory.FullName);

                        await ProcessUtil.QuickRun("chown",
                            $"{username}:{SftpUserInventoryGroup} {directoryInfo.FullName}");
                    }
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(exception, "Exception occured while setting permissions for '{dir}' ",
                        directoryInfo.FullName);
                }
            }

            var sshDir = Path.Combine(homeDirPath, ".ssh");
            if (!Directory.Exists(sshDir)) Directory.CreateDirectory(sshDir);
            var sshKeysDir = Path.Combine(sshDir, "keys");
            if (!Directory.Exists(sshKeysDir)) Directory.CreateDirectory(sshKeysDir);
            var sshAuthKeysPath = Path.Combine(sshDir, "authorized_keys");
            if (File.Exists(sshAuthKeysPath)) File.Delete(sshAuthKeysPath);
            var authKeysBuilder = new StringBuilder();
            foreach (var file in Directory.GetFiles(sshKeysDir))
            {
                _logger.LogDebug("Adding public key '{file}' for user '{user}'", file, username);
                authKeysBuilder.AppendLine(await File.ReadAllTextAsync(file));
            }
            foreach (var publicKey in user.PublicKeys)
            {
                _logger.LogDebug("Adding public key from config for user '{user}'", username);
                authKeysBuilder.AppendLine(publicKey);
            }
            await File.WriteAllTextAsync(sshAuthKeysPath, authKeysBuilder.ToString());
            await ProcessUtil.QuickRun("chown", $"{user.Username} {sshAuthKeysPath}");
            await ProcessUtil.QuickRun("chmod", $"400 {sshAuthKeysPath}");
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
            if (string.IsNullOrWhiteSpace(e.Data)) return;
            if (_config.Global.Logging.IgnoreNoIdentificationString &&
                e.Data.Trim().StartsWith("Did not receive identification string from")) return;
            _logger.LogTrace($"sshd - {e.Data}");
        }

        private static bool IsSubDirectory(DirectoryInfo parent, DirectoryInfo directory)
        {
            if (parent == null) return false;
            if (directory.Parent == null) return false;
            if (directory.Parent.FullName == parent.FullName) return true;
            return IsSubDirectory(parent, directory.Parent);
        }
    }
}