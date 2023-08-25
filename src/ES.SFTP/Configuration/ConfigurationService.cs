using ES.SFTP.Configuration.Elements;
using ES.SFTP.Messages.Configuration;
using ES.SFTP.Messages.Events;
using MediatR;
using Microsoft.Extensions.Options;

namespace ES.SFTP.Configuration;

public class ConfigurationService : IHostedService, IRequestHandler<SftpConfigurationRequest, SftpConfiguration>
{
    private readonly ILogger _logger;
    private readonly IMediator _mediator;
    private readonly IOptionsMonitor<SftpConfiguration> _sftpOptionsMonitor;
    private SftpConfiguration _config;
    private IDisposable _sftpOptionsMonitorChangeHandler;


    public ConfigurationService(ILogger<ConfigurationService> logger,
        IOptionsMonitor<SftpConfiguration> sftpOptionsMonitor,
        IMediator mediator)
    {
        _logger = logger;
        _sftpOptionsMonitor = sftpOptionsMonitor;
        _mediator = mediator;
    }


    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Starting");
        _sftpOptionsMonitorChangeHandler = _sftpOptionsMonitor.OnChange(OnSftpConfigurationChanged);
        await UpdateConfiguration();

        _logger.LogInformation("Started");
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Stopping");

        _sftpOptionsMonitorChangeHandler?.Dispose();
        _logger.LogInformation("Stopped");

        return Task.CompletedTask;
    }


    public Task<SftpConfiguration> Handle(SftpConfigurationRequest request, CancellationToken cancellationToken)
    {
        return Task.FromResult(_config);
    }

    private void OnSftpConfigurationChanged(SftpConfiguration arg1, string arg2)
    {
        _logger.LogInformation("SFTP Configuration was changed.");
        UpdateConfiguration().Wait();
        _mediator.Publish(new ConfigurationChanged()).ConfigureAwait(false);
    }

    private Task UpdateConfiguration()
    {
        _logger.LogDebug("Validating and updating configuration");

        var config = _sftpOptionsMonitor.CurrentValue ?? new SftpConfiguration();

        config.Global ??= new GlobalConfiguration();

        config.Global.Directories ??= new List<string>();
        config.Global.Logging ??= new LoggingDefinition();
        config.Global.Chroot ??= new ChrootDefinition();
        config.Global.PKIandPassword ??= new string("");
        config.Global.HostKeys ??= new HostKeysDefinition();
        config.Global.Hooks ??= new HooksDefinition();

        if (string.IsNullOrWhiteSpace(config.Global.Chroot.Directory)) config.Global.Chroot.Directory = "%h";
        if (string.IsNullOrWhiteSpace(config.Global.Chroot.StartPath)) config.Global.Chroot.StartPath = null;


        config.Users ??= new List<UserDefinition>();

        var validUsers = new List<UserDefinition>();
        for (var index = 0; index < config.Users.Count; index++)
        {
            var userDefinition = config.Users[index];
            if (string.IsNullOrWhiteSpace(userDefinition.Username))
            {
                _logger.LogWarning("Users[{index}] has a null or whitespace username. Skipping user.", index);
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
}