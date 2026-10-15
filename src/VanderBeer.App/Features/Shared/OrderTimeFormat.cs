using System.Globalization;

namespace VanderBeer.App.Features.Shared;

public static class OrderTimeFormat
{
    private static readonly TimeZoneInfo StoreZone =
        TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    public static string InStoreZone(DateTimeOffset instant) =>
        TimeZoneInfo.ConvertTime(instant, StoreZone)
            .ToString("dd/MM/yyyy HH:mm", CultureInfo.GetCultureInfo("pt-BR"));

    public static string InStoreZone(DateTime instant) =>
        InStoreZone(new DateTimeOffset(instant.ToUniversalTime()));
}
