namespace VanderBeer.Contracts.Catalog;

public sealed record StoreSettingsDto(string StoreName, string TimeZone, string Currency,
    DateOnly Today, DateOnly MinRequestedDate, DateOnly MaxRequestedDate,
    decimal DeliveryFee, int MaxDistinctItems, int MaxQuantityPerItem, decimal MaxOrderTotal);
