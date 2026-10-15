using Microsoft.AspNetCore.Mvc;
using VanderBeer.Contracts.Operations;

namespace VanderBeer.Api;

internal static class ErrorResponses
{
    internal static ObjectResult ServiceUnavailable(HttpContext context)
    {
        var response = new ObjectResult(ServiceUnavailableDto.Create(context.TraceIdentifier))
        {
            StatusCode = StatusCodes.Status503ServiceUnavailable
        };
        response.ContentTypes.Add("application/problem+json");
        return response;
    }
}
