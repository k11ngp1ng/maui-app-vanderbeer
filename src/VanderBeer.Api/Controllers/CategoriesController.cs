using Microsoft.AspNetCore.Mvc;
using VanderBeer.Application.Catalog;
using VanderBeer.Application.Operations;

namespace VanderBeer.Api.Controllers;

[ApiController]
[Route("api/v1/categories")]
public sealed class CategoriesController(ICategoryReader categoryReader) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await categoryReader.GetActiveAsync(cancellationToken));
        }
        catch (DataStoreUnavailableException)
        {
            return ErrorResponses.ServiceUnavailable(HttpContext);
        }
    }
}
