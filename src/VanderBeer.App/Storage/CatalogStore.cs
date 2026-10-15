using System.Text.Json;
using SQLite;
using VanderBeer.Contracts.Catalog;

namespace VanderBeer.App.Storage;

public sealed class CatalogStore
{
    private readonly SQLiteAsyncConnection _db = new(LocalDatabase.Path);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private bool _ready;

    private async Task ReadyAsync()
    {
        if (_ready) return;
        await _db.CreateTableAsync<CachedProduct>();
        await _db.CreateTableAsync<CachedReference>();
        await _db.CreateTableAsync<PendingProductCommand>();
        _ready = true;
    }

    public async Task SaveApiAddressAsync(string address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
            return;
        await ReadyAsync();
        await _db.InsertOrReplaceAsync(new CachedReference { Key = "api-address",
            Json = JsonSerializer.Serialize(address), FetchedAtUtc = DateTime.UtcNow.ToString("O") });
    }

    public async Task<string?> ApiAddressAsync()
    {
        await ReadyAsync();
        var row = await _db.FindAsync<CachedReference>("api-address");
        return row is null ? null : JsonSerializer.Deserialize<string>(row.Json);
    }

    public async Task SaveCategoriesAsync(IReadOnlyList<CategoryDto> categories)
    {
        await ReadyAsync();
        await _db.InsertOrReplaceAsync(new CachedReference { Key = "categories",
            Json = JsonSerializer.Serialize(categories, JsonOptions), FetchedAtUtc = DateTime.UtcNow.ToString("O") });
    }

    public async Task SaveCheckoutReferencesAsync(StoreSettingsDto settings,
        IReadOnlyList<PaymentMethodDto> payments)
    {
        await ReadyAsync();
        var at = DateTime.UtcNow.ToString("O");
        var settingsJson = JsonSerializer.Serialize(settings, JsonOptions);
        var paymentsJson = JsonSerializer.Serialize(payments, JsonOptions);
        await _db.RunInTransactionAsync(connection =>
        {
            connection.InsertOrReplace(new CachedReference { Key = "store-settings",
                Json = settingsJson, FetchedAtUtc = at });
            connection.InsertOrReplace(new CachedReference { Key = "payment-methods",
                Json = paymentsJson, FetchedAtUtc = at });
        });
    }

    public async Task<(StoreSettingsDto Settings, IReadOnlyList<PaymentMethodDto> Payments)?> CheckoutReferencesAsync()
    {
        await ReadyAsync();
        var settings = await _db.FindAsync<CachedReference>("store-settings");
        var payments = await _db.FindAsync<CachedReference>("payment-methods");
        if (settings is null || payments is null) return null;
        return (JsonSerializer.Deserialize<StoreSettingsDto>(settings.Json, JsonOptions)!,
            JsonSerializer.Deserialize<List<PaymentMethodDto>>(payments.Json, JsonOptions) ?? []);
    }

    public async Task<(IReadOnlyList<CategoryDto> Items, DateTime? FetchedAtUtc)> CategoriesAsync()
    {
        await ReadyAsync();
        var row = await _db.FindAsync<CachedReference>("categories");
        return row is null ? ([], null) :
            (JsonSerializer.Deserialize<List<CategoryDto>>(row.Json, JsonOptions) ?? [], DateTime.Parse(row.FetchedAtUtc));
    }

    public async Task SaveProductsAsync(IReadOnlyList<ProductDto> products)
    {
        await ReadyAsync();
        var now = DateTime.UtcNow.ToString("O");
        foreach (var product in products)
            await _db.InsertOrReplaceAsync(new CachedProduct { Id = product.Id.ToString("D"),
                Json = JsonSerializer.Serialize(product, JsonOptions), FetchedAtUtc = now });
    }

    public async Task<ProductPageDto> ProductsAsync(bool company, string? search, Guid? categoryId,
        bool? isActive, int page)
    {
        await ReadyAsync();
        var rows = await _db.Table<CachedProduct>().ToListAsync();
        var matches = rows.Select(x => JsonSerializer.Deserialize<ProductDto>(x.Json, JsonOptions)!)
            .Where(x => (company || x.IsActive) && (isActive is null || x.IsActive == isActive) &&
                (categoryId is null || x.CategoryId == categoryId) &&
                (string.IsNullOrWhiteSpace(search) || x.Name.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase) ||
                 x.Sku.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)))
            .OrderBy(x => x.Name).ThenBy(x => x.Id).ToArray();
        return new(matches.Skip((page - 1) * 20).Take(20).ToArray(), page, 20, matches.Length);
    }

    public async Task<DateTime?> LatestProductFetchAsync()
    {
        await ReadyAsync();
        var rows = await _db.Table<CachedProduct>().ToListAsync();
        var latest = rows.Select(x => x.FetchedAtUtc).OrderByDescending(x => x).FirstOrDefault();
        return DateTime.TryParse(latest, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed : null;
    }

    public async Task<ProductDto?> ProductAsync(Guid id)
    {
        await ReadyAsync();
        var row = await _db.FindAsync<CachedProduct>(id.ToString("D"));
        return row is null ? null : JsonSerializer.Deserialize<ProductDto>(row.Json, JsonOptions);
    }

    public async Task RemoveProductAsync(Guid id)
    {
        await ReadyAsync();
        await _db.DeleteAsync<CachedProduct>(id.ToString("D"));
    }

    public async Task<PendingProductCommand?> PendingAsync()
    {
        await ReadyAsync();
        return await _db.FindAsync<PendingProductCommand>(1);
    }

    public async Task SavePendingAsync(PendingProductCommand command)
    {
        await ReadyAsync();
        await _db.InsertOrReplaceAsync(command);
    }

    public async Task ClearPendingAsync()
    {
        await ReadyAsync();
        await _db.DeleteAsync<PendingProductCommand>(1);
    }

    public sealed class CachedProduct
    {
        [PrimaryKey] public string Id { get; set; } = "";
        public string Json { get; set; } = "";
        public string FetchedAtUtc { get; set; } = "";
    }
    public sealed class CachedReference
    {
        [PrimaryKey] public string Key { get; set; } = "";
        public string Json { get; set; } = "";
        public string FetchedAtUtc { get; set; } = "";
    }
    public sealed class PendingProductCommand
    {
        [PrimaryKey] public int Id { get; set; } = 1;
        public string Path { get; set; } = "";
        public string Method { get; set; } = "";
        public string Json { get; set; } = "";
        public string BaseUrl { get; set; } = "";
        public string CreatedAtUtc { get; set; } = "";
    }
}
