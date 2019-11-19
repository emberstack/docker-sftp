using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ES.SFTP.Host
{
    public class HostedService : IHostedService
    {
        private readonly Orchestrator _controller;
        private readonly ILogger<HostedService> _logger;


        public HostedService(ILogger<HostedService> logger, Orchestrator controller)
        {
            _logger = logger;
            _controller = controller;
        }


        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogDebug("Starting");
            await _controller.Start();
            _logger.LogInformation("Started");
        }


        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Application stop requested.");
            _logger.LogDebug("Stopping");
            await _controller.Stop();
            _logger.LogInformation("Stopped");
        }
    }
}