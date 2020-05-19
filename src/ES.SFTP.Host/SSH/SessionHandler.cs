using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ES.SFTP.Host.Configuration.Elements;
using ES.SFTP.Host.Extensions;
using ES.SFTP.Host.Interop;
using ES.SFTP.Host.Messages.Configuration;
using ES.SFTP.Host.Messages.Events;
using ES.SFTP.Host.Messages.Pam;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ES.SFTP.Host.SSH
{
    public class SessionHandler : IRequestHandler<PamEventRequest, bool>
    {
        private const string HomeBasePath = "/home";
        private const string SftpUserInventoryGroup = "sftp-user-inventory";

        private readonly ILogger _logger;
        private readonly IMediator _mediator;
        private SftpConfiguration _config;

        public SessionHandler(ILogger<SessionHandler> logger, IMediator mediator)
        {
            _logger = logger;
            _mediator = mediator;
        }


        [SuppressMessage("ReSharper", "MethodSupportsCancellation")]
        public async Task<bool> Handle(PamEventRequest request, CancellationToken cancellationToken)
        {
            switch (request.EventType)
            {
                case "open_session":
                    await PrepareUserForSftp(request.Username);
                    break;
            }

            await _mediator.Publish(new UserSessionChangedEvent
            {
                Username = request.Username,
                SessionState = request.EventType
            });
            return true;
        }

        private async Task PrepareUserForSftp(string username)
        {
            _logger.LogDebug("Configuring session for user '{user}'", username);

            _config = await _mediator.Send(new SftpConfigurationRequest());

            var user = _config.Users.FirstOrDefault(s => s.Username == username) ?? new UserDefinition
            {
                Username = username,
                Chroot = _config.Global.Chroot,
                Directories = _config.Global.Directories
            };

            var homeDirPath = Path.Combine(HomeBasePath, username);
            var chroot = user.Chroot ?? _config.Global.Chroot;

            //Parse chroot path by replacing markers
            var chrootPath = string.Join("%%h",
                chroot.Directory.Split("%%h")
                    .Select(s => s.Replace("%h", homeDirPath)).ToList());
            chrootPath = string.Join("%%u",
                chrootPath.Split("%%u")
                    .Select(s => s.Replace("%u", username)).ToList());

            //Create chroot directory and set owner to root and correct permissions
            var chrootDirectory = Directory.CreateDirectory(chrootPath);
            await ProcessUtil.QuickRun("chown", $"root:root {chrootDirectory.FullName}");
            await ProcessUtil.QuickRun("chmod", $"755 {chrootDirectory.FullName}");

            var directories = new List<string>();
            directories.AddRange(_config.Global.Directories);
            directories.AddRange(user.Directories);
            foreach (var directory in directories.Distinct().OrderBy(s => s).ToList())
            {
                var dirInfo = new DirectoryInfo(Path.Combine(chrootDirectory.FullName, directory));
                if (!dirInfo.Exists)
                {
                    _logger.LogDebug("Creating directory '{dir}' for user '{user}'", dirInfo.FullName, username);
                    Directory.CreateDirectory(dirInfo.FullName);
                }

                try
                {
                    if (dirInfo.IsDescendentOf(chrootDirectory))
                    {
                        //Set the user as owner for directory and all parents until chroot path
                        var dir = dirInfo;
                        while (dir.FullName != chrootDirectory.FullName)
                        {
                            await ProcessUtil.QuickRun("chown", $"{username}:{SftpUserInventoryGroup} {dir.FullName}");
                            dir = dir.Parent ?? chrootDirectory;
                        }
                    }
                    else
                    {
                        _logger.LogWarning(
                            "Directory '{dir}' is not within chroot path '{chroot}'. Setting direct permissions.",
                            dirInfo.FullName, chrootDirectory.FullName);

                        await ProcessUtil.QuickRun("chown",
                            $"{username}:{SftpUserInventoryGroup} {dirInfo.FullName}");
                    }
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(exception, "Exception occured while setting permissions for '{dir}' ",
                        dirInfo.FullName);
                }
            }

            _logger.LogInformation("Session ready for user '{user}'", username);
        }
    }
}