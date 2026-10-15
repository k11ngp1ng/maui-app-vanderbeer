namespace VanderBeer.Contracts.Catalog;

public sealed record ProductDto(Guid Id, Guid CategoryId, string Sku, string Name,
    string? Description, string UnitLabel, decimal Price, string? ImageUrl,
    bool IsAvailable, bool IsActive, int Version, DateTime UpdatedAtUtc);

public sealed record ProductPageDto(IReadOnlyList<ProductDto> Items, int Page, int PageSize, long Total);

public sealed record PaymentMethodDto(Guid Id, string Code, string Name);

public sealed record ProductWriteRequest(Guid ClientRequestId, Guid CategoryId, string Sku,
    string Name, string? Description, string UnitLabel, decimal Price, string? ImageUrl,
    bool IsAvailable, bool IsActive);

public sealed record ProductUpdateRequest(Guid ClientRequestId, int ExpectedVersion,
    Guid CategoryId, string Sku, string Name, string? Description, string UnitLabel,
    decimal Price, string? ImageUrl, bool IsAvailable, bool IsActive);
