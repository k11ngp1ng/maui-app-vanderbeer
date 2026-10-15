using SQLite;
using VanderBeer.Contracts.Catalog;

namespace VanderBeer.App.Storage;

public sealed class CartStore
{
    private readonly SQLiteAsyncConnection _db;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public CartStore() : this(LocalDatabase.Path) { }
    public CartStore(string databasePath) => _db = new(databasePath);
    public Task CloseAsync() => _db.CloseAsync();

    public async Task<IReadOnlyList<CartItem>> ListAsync()
    {
        await _db.CreateTableAsync<CartItemRow>();
        var rows = await _db.Table<CartItemRow>().OrderBy(x => x.ProductName).ToListAsync();
        return rows.Select(ToItem).ToList();
    }

    public async Task AddAsync(ProductDto product, int quantity)
    {
        if (!product.IsActive || !product.IsAvailable)
            throw new InvalidOperationException("Produto indisponível para o carrinho.");
        ValidateQuantity(quantity);
        var cents = PriceCents(product.Price);
        await _gate.WaitAsync();
        try
        {
            await _db.CreateTableAsync<CartItemRow>();
            var id = product.Id.ToString("D");
            var current = await _db.FindAsync<CartItemRow>(id);
            if (current is null)
            {
                if (await _db.Table<CartItemRow>().CountAsync() >= 50)
                    throw new InvalidOperationException("O carrinho permite no máximo 50 produtos distintos.");
                await _db.InsertAsync(new CartItemRow
                {
                    ProductId = id, ProductName = product.Name, UnitLabel = product.UnitLabel,
                    Quantity = quantity, ExpectedUnitPriceCents = cents,
                    UpdatedAtUtc = DateTime.UtcNow.ToString("O")
                });
            }
            else
            {
                ValidateQuantity((long)current.Quantity + quantity);
                current.Quantity += quantity;
                current.ProductName = product.Name;
                current.UnitLabel = product.UnitLabel;
                current.ExpectedUnitPriceCents = cents;
                current.UpdatedAtUtc = DateTime.UtcNow.ToString("O");
                await _db.UpdateAsync(current);
            }
        }
        finally { _gate.Release(); }
    }

    public async Task ReplaceAsync(IReadOnlyList<(ProductDto Product, int Quantity)> items)
    {
        if (items.Count is < 1 or > 50 || items.Select(x => x.Product.Id).Distinct().Count() != items.Count)
            throw new InvalidOperationException("Escolha de 1 a 50 produtos distintos.");
        foreach (var (product, quantity) in items)
        {
            if (!product.IsActive || !product.IsAvailable)
                throw new InvalidOperationException("Revise os produtos indisponíveis.");
            ValidateQuantity(quantity);
            PriceCents(product.Price);
        }
        var totalCents = items.Sum(x => checked(PriceCents(x.Product.Price) * x.Quantity));
        if (totalCents > 99999999)
            throw new InvalidOperationException("O total estimado excede o limite da loja.");
        await _gate.WaitAsync();
        try
        {
            await _db.CreateTableAsync<CartItemRow>();
            var now = DateTime.UtcNow.ToString("O");
            await _db.RunInTransactionAsync(connection =>
            {
                connection.DeleteAll<CartItemRow>();
                foreach (var (product, quantity) in items)
                    connection.Insert(new CartItemRow
                    {
                        ProductId = product.Id.ToString("D"), ProductName = product.Name,
                        UnitLabel = product.UnitLabel, Quantity = quantity,
                        ExpectedUnitPriceCents = PriceCents(product.Price), UpdatedAtUtc = now
                    });
            });
        }
        finally { _gate.Release(); }
    }

    public async Task SetQuantityAsync(Guid productId, int quantity)
    {
        ValidateQuantity(quantity);
        await _gate.WaitAsync();
        try
        {
            await _db.CreateTableAsync<CartItemRow>();
            var current = await _db.FindAsync<CartItemRow>(productId.ToString("D"))
                ?? throw new InvalidOperationException("Produto não encontrado no carrinho.");
            current.Quantity = quantity;
            current.UpdatedAtUtc = DateTime.UtcNow.ToString("O");
            await _db.UpdateAsync(current);
        }
        finally { _gate.Release(); }
    }

    public async Task RemoveAsync(Guid productId)
    {
        await _gate.WaitAsync();
        try
        {
            await _db.CreateTableAsync<CartItemRow>();
            await _db.DeleteAsync<CartItemRow>(productId.ToString("D"));
        }
        finally { _gate.Release(); }
    }

    private static void ValidateQuantity(long quantity)
    {
        if (quantity is < 1 or > 999)
            throw new InvalidOperationException("Informe quantidade de 1 a 999 por produto.");
    }

    private static long PriceCents(decimal price)
    {
        if (price is < 0.01m or > 99999.99m || price * 100 != decimal.Truncate(price * 100))
            throw new InvalidOperationException("Preço inválido para o carrinho.");
        return checked((long)(price * 100));
    }

    private static CartItem ToItem(CartItemRow row) => new(Guid.Parse(row.ProductId), row.ProductName,
        row.UnitLabel, row.Quantity, row.ExpectedUnitPriceCents);

    public sealed class CartItemRow
    {
        [PrimaryKey] public string ProductId { get; set; } = "";
        [NotNull] public string ProductName { get; set; } = "";
        [NotNull] public string UnitLabel { get; set; } = "";
        public int Quantity { get; set; }
        public long ExpectedUnitPriceCents { get; set; }
        [NotNull] public string UpdatedAtUtc { get; set; } = "";
    }
}

public sealed record CartItem(Guid ProductId, string ProductName, string UnitLabel, int Quantity,
    long ExpectedUnitPriceCents)
{
    public decimal UnitPrice => ExpectedUnitPriceCents / 100m;
    public decimal Subtotal => Quantity * UnitPrice;
}
