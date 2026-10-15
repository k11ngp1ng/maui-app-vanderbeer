using Microsoft.AspNetCore.Mvc;
using VanderBeer.Contracts.Catalog;

namespace VanderBeer.Api.Controllers;

[ApiController]
[Route("api/v1/store-settings")]
public sealed class StoreSettingsController : ControllerBase
{
    [HttpGet]
    public StoreSettingsDto Get()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone));
        return new("VanderBeer", "America/Sao_Paulo", "BRL", today, today,
            today.AddDays(30), 0m, 50, 999, 999999.99m);
    }
}
