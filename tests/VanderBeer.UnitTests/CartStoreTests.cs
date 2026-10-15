using VanderBeer.App.Storage;
using VanderBeer.Contracts.Catalog;
using SQLite;

namespace VanderBeer.UnitTests;

public sealed class CartStoreTests
{
    [Fact]
    public async Task Reorder_replaces_cart_atomically_with_current_price()
    {
        var path = Path.Combine(Path.GetTempPath(), "vanderbeer-reorder-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var cart = new CartStore(path);
            var oldCartProduct = Product(8m);
            var orderedProduct = Product(10m);
            await cart.AddAsync(oldCartProduct, 1);
            await cart.ReplaceAsync([(orderedProduct with { Price = 12.50m }, 2)]);
            var item = Assert.Single(await cart.ListAsync());
            Assert.Equal(orderedProduct.Id, item.ProductId);
            Assert.Equal(12.50m, item.UnitPrice);
            Assert.Equal(25m, item.Subtotal);
            await Assert.ThrowsAsync<InvalidOperationException>(() => cart.ReplaceAsync(
                [(orderedProduct with { IsAvailable = false }, 1)]));
            Assert.Equal(orderedProduct.Id, Assert.Single(await cart.ListAsync()).ProductId);
            await cart.CloseAsync();
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task Reorder_insert_failure_restores_previous_cart()
    {
        var path = Path.Combine(Path.GetTempPath(), "vanderbeer-reorder-rollback-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var cart = new CartStore(path);
            var original = Product(9m);
            await cart.AddAsync(original, 1);
            using (var setup = new SQLiteConnection(path))
                setup.Execute("CREATE TRIGGER reject_reorder BEFORE INSERT ON CartItemRow " +
                    "BEGIN SELECT RAISE(ABORT, 'falha simulada'); END");
            await Assert.ThrowsAsync<SQLiteException>(() => cart.ReplaceAsync([(Product(12m), 2)]));
            var preserved = Assert.Single(await cart.ListAsync());
            Assert.Equal(original.Id, preserved.ProductId);
            Assert.Equal(9m, preserved.UnitPrice);
            await cart.CloseAsync();
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task Same_product_combines_quantities_and_reopens_with_consistent_cent_total()
    {
        var path = Path.Combine(Path.GetTempPath(), "vanderbeer-cart-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var product = Product(12.34m);
            var first = new CartStore(path);
            await first.AddAsync(product, 2);
            await first.AddAsync(product with { Price = 13.50m }, 3);

            var reopened = new CartStore(path);
            var item = Assert.Single(await reopened.ListAsync());
            Assert.Equal(5, item.Quantity);
            Assert.Equal(1350, item.ExpectedUnitPriceCents);
            Assert.Equal(67.50m, item.Subtotal);
            await reopened.SetQuantityAsync(product.Id, 7);
            Assert.Equal(94.50m, Assert.Single(await new CartStore(path).ListAsync()).Subtotal);
            await reopened.RemoveAsync(product.Id);
            Assert.Empty(await new CartStore(path).ListAsync());
            await first.CloseAsync();
            await reopened.CloseAsync();
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task Invalid_quantity_or_unavailable_product_does_not_change_existing_cart()
    {
        var path = Path.Combine(Path.GetTempPath(), "vanderbeer-cart-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var product = Product(9.99m);
            var store = new CartStore(path);
            await store.AddAsync(product, 999);
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.AddAsync(product, 1));
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.SetQuantityAsync(product.Id, 0));
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.AddAsync(
                product with { Id = Guid.NewGuid(), IsAvailable = false }, 1));
            Assert.Equal(999, Assert.Single(await store.ListAsync()).Quantity);
            await store.CloseAsync();
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task Fifty_distinct_products_are_allowed_but_the_next_is_rejected()
    {
        var path = Path.Combine(Path.GetTempPath(), "vanderbeer-cart-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var store = new CartStore(path);
            for (var i = 0; i < 50; i++) await store.AddAsync(Product(1.01m), 1);
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.AddAsync(Product(1.01m), 1));
            var items = await store.ListAsync();
            Assert.Equal(50, items.Count);
            Assert.Equal(50.50m, items.Sum(x => x.Subtotal));
            await store.CloseAsync();
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private static ProductDto Product(decimal price) => new(Guid.NewGuid(), Guid.NewGuid(), "VB-CART",
        "Produto exemplo", null, "500 ml", price, null, true, true, 1, DateTime.UtcNow);
}
