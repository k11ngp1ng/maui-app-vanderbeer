using Microsoft.AspNetCore.Mvc;
using VanderBeer.Application.Operations;
using VanderBeer.Contracts.Operations;

namespace VanderBeer.Api.Controllers;

[ApiController]
[Route("health")]
public sealed class HealthController(IDatabaseProbe databaseProbe) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        if (!await databaseProbe.IsAvailableAsync(cancellationToken))
        {
            return ErrorResponses.ServiceUnavailable(HttpContext);
        }

        return Ok(new HealthDto("Healthy"));
    }
}
