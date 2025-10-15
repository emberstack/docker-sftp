using System.Diagnostics;
using ES.SFTP.Configuration.Elements;
using ES.SFTP.Interop;
using ES.SFTP.Messages.Configuration;
using ES.SFTP.Messages.Events;
using ES.SFTP.SSH.Configuration;
using MediatR;

namespace ES.SFTP.SSH;

public class SSHService : IHostedService, INotificationHandler<ConfigurationChanged>
{
    private const string SshDirPath = "/etc/ssh";
    private static readonly string KeysImportDirPath = Path.Combine(SshDirPath, "keys");
    private static readonly string ConfigFilePath = Path.Combine(SshDirPath, "sshd_config");
    private readonly ILogger<SSHService> _logger;
    private readonly IMediator _mediator;
    private bool _loggingIgnoreNoIdentificationString;
    private Process _serverProcess;
    private Action _serviceProcessExitAction;


    public SSHService(ILogger<SSHService> logger, IMediator mediator)
    {
        _logger = logger;
        _mediator = mediator;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Starting");
        await RestartService(true);
        _logger.LogInformation("Started");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Stopping");
        await StopOpenSSH();
        _logger.LogInformation("Stopped");
    }

    public async Task Handle(ConfigurationChanged notification, CancellationToken cancellationToken)
    {
        await RestartService();
    }

    private async Task RestartService(bool forceStop = false)
    {
        await StopOpenSSH(forceStop);
        await UpdateHostKeyFiles();
        await UpdateConfiguration();
        await StartOpenSSH();
    }


    private async Task UpdateConfiguration()
    {
        var sftpConfig = await _mediator.Send(new SftpConfigurationRequest());
        _loggingIgnoreNoIdentificationString = sftpConfig.Global.Logging.IgnoreNoIdentificationString;

        var sshdConfig = new SSHConfiguration
        {
            Ciphers = sftpConfig.Global.Ciphers,
            HostKeyAlgorithms = sftpConfig.Global.HostKeyAlgorithms,
            KexAlgorithms = sftpConfig.Global.KexAlgorithms,
            MACs = sftpConfig.Global.MACs,
            PKIandPassword = sftpConfig.Global.PKIandPassword
        };

        var exceptionalChrootUsers = sftpConfig.Users.Where(s => s.Chroot != null).ToList();
        var exceptionalUmaskUsers = sftpConfig.Users.Where(s => !string.IsNullOrWhiteSpace(s.Umask)).ToList();

        var standardDeclarations = new[]
        {
            "X11Forwarding no",
            "AllowTcpForwarding no"
        };

        sshdConfig.AllowUsers.AddRange(sftpConfig.Users.Select(s =>
            s.AllowedHosts.Any()
                ? $"{s.Username}@{string.Join(",", s.AllowedHosts)}"
                : s.Username)
        );

        sshdConfig.MatchBlocks.AddRange(exceptionalUmaskUsers.Select(s => new MatchBlock
        {
            Criteria = MatchBlock.MatchCriteria.User,
            Match = {s.Username},
            Declarations = new List<string>(standardDeclarations)
            {
                $"ForceCommand internal-sftp -u {s.Umask}"
            }
        }));

        sshdConfig.MatchBlocks.AddRange(exceptionalChrootUsers.Select(s => new MatchBlock
        {
            Criteria = MatchBlock.MatchCriteria.User,
            Match = {s.Username},
            Declarations = new List<string>(standardDeclarations)
            {
                $"ChrootDirectory {s.Chroot.Directory}",
                !string.IsNullOrWhiteSpace(s.Chroot.StartPath)
                    ? $"ForceCommand internal-sftp -d {s.Chroot.StartPath}"
                    : "ForceCommand internal-sftp"
            }
        }));

        sshdConfig.MatchBlocks.Add(new MatchBlock
        {
            Criteria = MatchBlock.MatchCriteria.User,
            Match = {"*"},
            //Except = exceptionalChrootUsers.Select(s => s.Username).ToList(),
            Declarations = new List<string>(standardDeclarations)
            {
                $"ChrootDirectory {sftpConfig.Global.Chroot.Directory}",
                !string.IsNullOrWhiteSpace(sftpConfig.Global.Chroot.StartPath)
                    ? $"ForceCommand internal-sftp -d {sftpConfig.Global.Chroot.StartPath}"
                    : "ForceCommand internal-sftp"
            }
        });

        var resultingConfig = sshdConfig.ToString();
        await File.WriteAllTextAsync(ConfigFilePath, resultingConfig);
    }

    private async Task UpdateHostKeyFiles()
    {
        var config = await _mediator.Send(new SftpConfigurationRequest());
        _logger.LogDebug("Updating host key files");
        Directory.CreateDirectory(KeysImportDirPath);

        var hostKeys = new[]
        {
            new
            {
                Type = nameof(HostKeysDefinition.Ed25519),
                KeygenArgs = "-t ed25519 -f {0} -N \"\"",
                File = "ssh_host_ed25519_key"
            },
            new
            {
                Type = nameof(HostKeysDefinition.Rsa),
                KeygenArgs = "-t rsa -b 4096 -f {0} -N \"\"",
                File = "ssh_host_rsa_key"
            }
        };

        foreach (var hostKeyType in hostKeys)
        {
            var filePath = Path.Combine(KeysImportDirPath, hostKeyType.File);
            if (File.Exists(filePath)) continue;
            var configValue = (string) config.Global.HostKeys.GetType().GetProperty(hostKeyType.Type)
                ?.GetValue(config.Global.HostKeys, null);

            if (!string.IsNullOrWhiteSpace(configValue))
            {
                _logger.LogDebug("Writing host key file '{file}' from config", filePath);
                await File.WriteAllTextAsync(filePath, configValue);
            }
            else
            {
                _logger.LogDebug("Generating host key file '{file}'", filePath);
                var keygenArgs = string.Format(hostKeyType.KeygenArgs, filePath);
                await ProcessUtil.QuickRun("ssh-keygen", keygenArgs);
            }
        }

        foreach (var file in Directory.GetFiles(KeysImportDirPath))
        {
            var targetFile = Path.Combine(SshDirPath, Path.GetFileName(file));
            _logger.LogDebug("Copying '{sourceFile}' to '{targetFile}'", file, targetFile);
            File.Copy(file, targetFile, true);
            await ProcessUtil.QuickRun("chown", $"root:root \"{targetFile}\"");
            await ProcessUtil.QuickRun("chmod", $"700 \"{targetFile}\"");
        }
    }


    private async Task StartOpenSSH()
    {
        _logger.LogInformation("Starting 'sshd' process");
        _serviceProcessExitAction = () =>
        {
            _logger.LogWarning("'sshd' process has stopped. Restarting process.");
            RestartService().Wait();
        };

        void ListenForExit()
        {
            //Use this approach since the Exited event does not trigger on process crash
            Task.Run(() =>
            {
                _serverProcess.WaitForExit();
                _serviceProcessExitAction?.Invoke();
            });
        }

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
        ListenForExit();
        _serverProcess.BeginOutputReadLine();
        _serverProcess.BeginErrorReadLine();
        await _mediator.Publish(new ServerStartupEvent());
    }

    private void OnSSHOutput(object sender, DataReceivedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.Data)) return;
        if (_loggingIgnoreNoIdentificationString &&
            e.Data.Trim().StartsWith("Did not receive identification string from")) return;
        _logger.LogTrace($"sshd - {e.Data}");
    }

    private async Task StopOpenSSH(bool force = false)
    {
        if (_serverProcess != null)
        {
            _logger.LogDebug("Stopping 'sshd' process");
            _serviceProcessExitAction = null;
            _serverProcess.Kill(true);
            _serverProcess.OutputDataReceived -= OnSSHOutput;
            _serverProcess.ErrorDataReceived -= OnSSHOutput;
            _logger.LogInformation("Stopped 'sshd' process");
            _serverProcess.Dispose();
            _serverProcess = null;
        }

        if (force)
        {
            var arguments = Debugger.IsAttached ? "-q sshd" : "-q -w sshd";
            var command = await ProcessUtil.QuickRun("killall", arguments, false);
            if (command.ExitCode != 0 && command.ExitCode != 1 && !string.IsNullOrWhiteSpace(command.Output))
                throw new Exception(
                    $"Could not stop existing sshd processes.{Environment.NewLine}{command.Output}");
        }
    }
}