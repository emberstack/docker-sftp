using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ES.SFTP.Host.Interop;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ES.SFTP.Host.Security
{
    public class AuthenticationService : IHostedService
    {
        private const string PamDirPath = "/etc/pam.d";
        private const string PamHookName = "sftp-hook";
        private readonly ILogger _logger;

        public AuthenticationService(ILogger<AuthenticationService> logger)
        {
            _logger = logger;
        }

        // ReSharper disable MethodSupportsCancellation
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogDebug("Starting");

            var pamCommonSessionFile = Path.Combine(PamDirPath, "common-session");
            var pamSftpHookFile = Path.Combine(PamDirPath, PamHookName);

            _logger.LogDebug("Stopping SSSD service");
            await ProcessUtil.QuickRun("service", "sssd stop", false);

            _logger.LogDebug("Applying SSSD configuration");
            File.Copy("./config/sssd.conf", "/etc/sssd/sssd.conf", true);
            await ProcessUtil.QuickRun("chown", "root:root \"/etc/sssd/sssd.conf\"");
            await ProcessUtil.QuickRun("chmod", "600 \"/etc/sssd/sssd.conf\"");

            _logger.LogDebug("Installing PAM hook");
            var scriptsDirectory = Path.Combine(PamDirPath, "scripts");
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


            if (!(await File.ReadAllTextAsync(pamCommonSessionFile)).Contains($"@include {PamHookName}"))
                await File.AppendAllTextAsync(pamCommonSessionFile, $"@include {PamHookName}{Environment.NewLine}");

            _logger.LogDebug("Restarting SSSD service");
            await ProcessUtil.QuickRun("service", "sssd restart", false);

            _logger.LogInformation("Started");
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogDebug("Stopping");
            await ProcessUtil.QuickRun("service", "sssd stop", false);
            _logger.LogInformation("Stopped");
        }
    }
}