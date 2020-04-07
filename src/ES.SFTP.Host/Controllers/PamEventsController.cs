using System.Threading.Tasks;
using ES.SFTP.Host.Messages;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace ES.SFTP.Host.Controllers
{
    [Route("api/events/pam")]
    public class PamEventsController : Controller
    {
        private readonly ILogger<PamEventsController> _logger;
        private readonly IMediator _mediator;

        public PamEventsController(ILogger<PamEventsController> logger, IMediator mediator)
        {
            _logger = logger;
            _mediator = mediator;
        }


        [HttpGet]
        [Route("generic")]
        public async Task<IActionResult> OnGenericPamEvent(string username, string type, string service)
        {
            _logger.LogInformation("Received event for user '{username}' with type '{type}', {service}", username, type,
                service);
            var response = await _mediator.Send(new PamEventRequest
            {
                Username = username,
                EventType = type,
                Service = service
            });
            return response ? (IActionResult) Ok() : BadRequest();
        }
    }
}