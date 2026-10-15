using VanderBeer.App.Storage;
using VanderBeer.Contracts.Catalog;
using SQLite;
using VanderBeer.Contracts.Orders;

namespace VanderBeer.UnitTests;

public sealed class LocalOrderStoreTests
{
    [Fact]
    public async Task Reorder_cart_does_not_modify_original_local_order_or_request_key()
    {
        var path = Path.Combine(Path.GetTempPath(), "vanderbeer-reorder-order-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var cart = new CartStore(path);
            var product = new ProductDto(Guid.NewGuid(), Guid.NewGuid(), "VB-ORIGINAL", "Produto",
                null, "unidade", 10m, null, true, true, 1, DateTime.UtcNow);
            await cart.AddAsync(product, 2);
            var orders = new LocalOrderStore(path);
            var id = await orders.SaveAsync(new("Pickup", DateOnly.FromDateTime(DateTime.Today),
                Guid.NewGuid(), "Cash", "Dinheiro", null, null, null, Guid.NewGuid(), 1));
            var original = (await orders.ReadAsync(id))!;
            var originalLine = Assert.Single(await orders.ItemsAsync(id));
            await cart.ReplaceAsync([(product with { Price = 12m }, 1)]);
            Assert.Equal(original.ClientRequestId, (await orders.ReadAsync(id))!.ClientRequestId);
            Assert.Equal(originalLine.ExpectedUnitPriceCents,
                Assert.Single(await orders.ItemsAsync(id)).ExpectedUnitPriceCents);
            Assert.Equal(12m, Assert.Single(await cart.ListAsync()).UnitPrice);
            await orders.CloseAsync();
            await cart.CloseAsync();
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task Remote_history_keeps_newer_attendance_after_stale_refresh()
    {
        var path = Path.Combine(Path.GetTempPath(), "vanderbeer-history-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var store = new LocalOrderStore(path);
            var now = DateTime.UtcNow;
            var received = new OrderDto(Guid.NewGuid(), "VB-00000042", now,
                DateOnly.FromDateTime(DateTime.Today), "Pickup", "Received", 10m, 1,
                Guid.NewGuid(), Guid.NewGuid(), new("Cliente", "11999999999"),
                new(Guid.NewGuid(), "Cash", "Dinheiro"), null, null, null, 0m, 0m,
                now, [], [new(1, null, "Received", now, null)]);
            var confirmed = received with { Status = "Confirmed", Version = 2,
                StatusHistory = [new(1, null, "Received", now, null),
                    new(2, "Received", "Confirmed", now.AddMinutes(1), null)] };
            await store.SaveRemoteAsync(received);
            await store.SaveRemoteAsync(confirmed);
            await store.SaveRemoteAsync(received);
            var cached = Assert.Single(await store.RemoteOrdersAsync());
            Assert.Equal("Confirmed", cached.Status);
            Assert.Equal(2, cached.Version);
            Assert.Equal(2, cached.StatusHistory.Count);
            await store.CloseAsync();
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task Definitive_rejection_can_be_revised_in_same_local_entry_with_new_key_and_price()
    {
        var path = Path.Combine(Path.GetTempPath(), "vanderbeer-review-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var cart = new CartStore(path);
            var product = new ProductDto(Guid.NewGuid(), Guid.NewGuid(), "VB-REVIEW", "Produto",
                null, "unidade", 8m, null, true, true, 1, DateTime.UtcNow);
            await cart.AddAsync(product, 2);
            var store = new LocalOrderStore(path);
            var checkout = new CheckoutSnapshot("Pickup", DateOnly.FromDateTime(DateTime.Today),
                Guid.NewGuid(), "CardOnFulfillment", "Cartão", null, null, null, Guid.NewGuid(), 1,
                null, "http://127.0.0.1:5081");
            var id = await store.SaveAsync(checkout);
            var rejected = await store.PrepareSendAsync(id);
            await store.MarkReviewAsync(id, "{\"detail\":\"Preço alterado\"}");
            var restored = await store.RestoreForReviewAsync(id,
                new Dictionary<Guid, ProductDto?> { [product.Id] = product with { Price = 10m } });
            Assert.Empty(restored.Unavailable);
            Assert.Equal(20m, (await cart.ListAsync()).Single().Subtotal);
            await store.ReviseAsync(id, checkout);
            Assert.Empty(await cart.ListAsync());
            var revised = (await store.OutboxAsync(id))!;
            Assert.Equal("Pending", revised.Status);
            Assert.Null(revised.RequestJson);
            Assert.NotEqual(rejected.ClientRequestId, revised.ClientRequestId);
            var sent = await store.PrepareSendAsync(id);
            Assert.Contains("10", sent.RequestJson);
            Assert.Equal(id.ToString("D"), (await store.ListAsync()).Single().LocalId);
            await store.CloseAsync();
            await cart.CloseAsync();
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task Pending_customer_is_resolved_once_and_retry_preserves_frozen_order()
    {
        var path = Path.Combine(Path.GetTempPath(), "vanderbeer-dependent-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var cart = new CartStore(path);
            await cart.AddAsync(new ProductDto(Guid.NewGuid(), Guid.NewGuid(), "VB-DEP",
                "Produto", null, "unidade", 8m, null, true, true, 1, DateTime.UtcNow), 1);
            var store = new LocalOrderStore(path);
            var id = await store.SaveAsync(new("Pickup", DateOnly.FromDateTime(DateTime.Today),
                Guid.NewGuid(), "Cash", "Dinheiro", null, null, null, Guid.Empty, 0,
                Guid.NewGuid(), "http://127.0.0.1:5081"));
            Assert.NotNull((await store.OutboxAsync(id))!.DependsOnId);
            Assert.Equal("http://127.0.0.1:5081", (await store.OutboxAsync(id))!.BaseUrl);
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.PrepareSendAsync(id));
            var customerId = Guid.NewGuid();
            var customer = new VanderBeer.Contracts.Customers.CustomerDto(customerId, "Individual", "Cliente Teste",
                null, "12345678901", null, "11999999999", null, null, 2, DateTime.UtcNow, DateTime.UtcNow);
            var first = await store.PrepareSendAsync(id, customer);
            Assert.Null(first.DependsOnId);
            Assert.Contains(customerId.ToString("D"), first.RequestJson);
            await store.MarkTransientAsync(id, TimeSpan.Zero);
            await store.CloseAsync();
            await cart.CloseAsync();
            var reopened = new LocalOrderStore(path);
            var again = await reopened.PrepareSendAsync(id, customer with { Version = 3 });
            Assert.Equal(first.ClientRequestId, again.ClientRequestId);
            Assert.Equal(first.RequestJson, again.RequestJson);
            Assert.Equal("http://127.0.0.1:5081", again.BaseUrl);
            await reopened.CloseAsync();
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task Simultaneous_send_preparation_uses_one_key_and_one_frozen_body()
    {
        var path = Path.Combine(Path.GetTempPath(), "vanderbeer-parallel-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var cart = new CartStore(path);
            await cart.AddAsync(new ProductDto(Guid.NewGuid(), Guid.NewGuid(), "VB-PAR",
                "Produto", null, "unidade", 8m, null, true, true, 1, DateTime.UtcNow), 1);
            var store = new LocalOrderStore(path);
            var id = await store.SaveAsync(new("Pickup", DateOnly.FromDateTime(DateTime.Today),
                Guid.NewGuid(), "Cash", "Dinheiro", null, null, null, Guid.NewGuid(), 1));
            var sent = await Task.WhenAll(store.PrepareSendAsync(id), store.PrepareSendAsync(id));
            Assert.Equal(sent[0].ClientRequestId, sent[1].ClientRequestId);
            Assert.Equal(sent[0].RequestJson, sent[1].RequestJson);
            Assert.Equal(2, (await store.OutboxAsync(id))!.Attempts);
            await cart.CloseAsync();
            await store.CloseAsync();
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task Reopen_resets_sending_to_pending_without_changing_request_or_due_time()
    {
        var path = Path.Combine(Path.GetTempPath(), "vanderbeer-reopen-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var cart = new CartStore(path);
            await cart.AddAsync(new ProductDto(Guid.NewGuid(), Guid.NewGuid(), "VB-REOPEN",
                "Produto", null, "unidade", 8m, null, true, true, 1, DateTime.UtcNow), 1);
            var store = new LocalOrderStore(path);
            var id = await store.SaveAsync(new("Pickup", DateOnly.FromDateTime(DateTime.Today),
                Guid.NewGuid(), "Cash", "Dinheiro", null, null, null, Guid.NewGuid(), 1));
            var before = await store.PrepareSendAsync(id);
            await store.CloseAsync();
            await cart.CloseAsync();
            var reopened = new LocalOrderStore(path);
            await reopened.ResetCycleAsync(id);
            var after = await reopened.OutboxAsync(id);
            Assert.Equal("Pending", after!.Status);
            Assert.Equal("Pending", (await reopened.ReadAsync(id))!.SyncStatus);
            Assert.Equal(before.RequestJson, after.RequestJson);
            Assert.Equal(before.ClientRequestId, after.ClientRequestId);
            await reopened.CloseAsync();
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task Remote_history_matches_pending_key_and_keeps_newer_status_after_replay()
    {
        var path = Path.Combine(Path.GetTempPath(), "vanderbeer-merge-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var cart = new CartStore(path);
            var product = new ProductDto(Guid.NewGuid(), Guid.NewGuid(), "VB-MERGE", "Produto exemplo",
                null, "unidade", 12.50m, null, true, true, 1, DateTime.UtcNow);
            await cart.AddAsync(product, 1);
            var store = new LocalOrderStore(path);
            var customerId = Guid.NewGuid();
            var id = await store.SaveAsync(new("Pickup", DateOnly.FromDateTime(DateTime.Today),
                Guid.NewGuid(), "Cash", "Dinheiro", null, null, null, customerId, 1));
            var key = Guid.Parse((await store.OutboxAsync(id))!.ClientRequestId);
            var received = new OrderDto(Guid.NewGuid(), "VB-2026-000002", DateTime.UtcNow,
                DateOnly.FromDateTime(DateTime.Today), "Pickup", "Received", 12.50m, 1, key,
                customerId, new("Cliente", "11999999999"), new(Guid.NewGuid(), "Cash", "Dinheiro"),
                null, null, null, 0m, 0m, DateTime.UtcNow, [], []);
            await store.SaveRemoteAsync(received with { Status = "Confirmed", Version = 2 });
            Assert.Equal("Synchronized", (await store.ReadAsync(id))!.SyncStatus);
            Assert.Equal("Succeeded", (await store.OutboxAsync(id))!.Status);
            Assert.Empty(await store.RemoteOrdersAsync());
            Assert.Equal("Confirmed", (await store.ReceiptAsync(id))!.Status);
            await store.ConfirmAsync(id, received);
            Assert.Equal("Confirmed", (await store.ReceiptAsync(id))!.Status);
            await cart.CloseAsync();
            await store.CloseAsync();
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task Send_freezes_request_and_confirmation_survives_reopen()
    {
        var path = Path.Combine(Path.GetTempPath(), "vanderbeer-send-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var cart = new CartStore(path);
            var product = new ProductDto(Guid.NewGuid(), Guid.NewGuid(), "VB-SEND", "Produto exemplo",
                null, "unidade", 12.50m, null, true, true, 1, DateTime.UtcNow);
            await cart.AddAsync(product, 2);
            var store = new LocalOrderStore(path);
            var id = await store.SaveAsync(new("Pickup", DateOnly.FromDateTime(DateTime.Today),
                Guid.NewGuid(), "Cash", "Dinheiro", null, null, null, Guid.NewGuid(), 1));
            var first = await store.PrepareSendAsync(id);
            Assert.Contains(first.ClientRequestId, first.RequestJson);
            Assert.Equal("Sending", (await store.ReadAsync(id))!.SyncStatus);
            await store.CloseAsync();
            await cart.CloseAsync();

            var reopened = new LocalOrderStore(path);
            var repeated = await reopened.PrepareSendAsync(id);
            Assert.Equal(first.RequestJson, repeated.RequestJson);
            Assert.Equal(first.ClientRequestId, repeated.ClientRequestId);
            var request = System.Text.Json.JsonSerializer.Deserialize<CreateOrderRequest>(repeated.RequestJson!,
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
            var result = new OrderDto(Guid.NewGuid(), "VB-2026-000001", DateTime.UtcNow,
                request.RequestedDate, "Pickup", "Received", 25m, 1, request.ClientRequestId,
                request.CustomerId, new("Cliente", "11999999999"), new(request.PaymentMethodId, "Cash", "Dinheiro"),
                null, null, null, 0m, 0m, DateTime.UtcNow, [], []);
            await reopened.ConfirmAsync(id, result);
            Assert.Equal("Synchronized", (await reopened.ReadAsync(id))!.SyncStatus);
            Assert.Equal("Succeeded", (await reopened.OutboxAsync(id))!.Status);
            Assert.Equal(result.Protocol, (await reopened.ReceiptAsync(id))!.Protocol);
            await reopened.CloseAsync();
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task Checkout_saves_order_items_and_outbox_and_clears_only_current_cart()
    {
        var path = Path.Combine(Path.GetTempPath(), "vanderbeer-order-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var cart = new CartStore(path);
            var product = new ProductDto(Guid.NewGuid(), Guid.NewGuid(), "VB-TEST", "Produto exemplo",
                null, "unidade", 12.50m, null, true, true, 1, DateTime.UtcNow);
            await cart.AddAsync(product, 2);
            var store = new LocalOrderStore(path);
            var id = await store.SaveAsync(new("Pickup", DateOnly.FromDateTime(DateTime.Today),
                Guid.NewGuid(), "Cash", "Dinheiro", null, null, null, Guid.NewGuid(), 1));
            Assert.Empty(await cart.ListAsync());
            await cart.CloseAsync();
            await store.CloseAsync();
            var reopened = new LocalOrderStore(path);
            var order = await reopened.ReadAsync(id);
            Assert.NotNull(order);
            Assert.Equal("Pending", order.SyncStatus);
            Assert.NotEqual(Guid.Empty, Guid.Parse(order.ClientRequestId));
            var item = Assert.Single(await reopened.ItemsAsync(id));
            Assert.Equal(product.Id.ToString("D"), item.ProductId);
            Assert.Equal(2, item.Quantity);
            Assert.Equal(1250, item.ExpectedUnitPriceCents);
            var queued = await reopened.OutboxAsync(id);
            Assert.NotNull(queued);
            Assert.Equal(order.ClientRequestId, queued.ClientRequestId);
            Assert.Equal("Pending", queued.Status);
            await reopened.CloseAsync();
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task Empty_cart_does_not_create_order_or_outbox()
    {
        var path = Path.Combine(Path.GetTempPath(), "vanderbeer-order-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var store = new LocalOrderStore(path);
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(new("Pickup",
                DateOnly.FromDateTime(DateTime.Today), Guid.NewGuid(), "Cash", "Dinheiro",
                null, null, null, Guid.NewGuid(), 1)));
            await store.CloseAsync();
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task Outbox_failure_rolls_back_order_items_and_cart_removal()
    {
        var path = Path.Combine(Path.GetTempPath(), "vanderbeer-order-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var cart = new CartStore(path);
            var product = new ProductDto(Guid.NewGuid(), Guid.NewGuid(), "VB-ROLLBACK",
                "Produto fictício", null, "unidade", 9.90m, null, true, true, 1, DateTime.UtcNow);
            await cart.AddAsync(product, 1);
            var store = new LocalOrderStore(path);
            await store.OutboxAsync(Guid.NewGuid());
            using (var setup = new SQLiteConnection(path))
                setup.Execute("CREATE TRIGGER reject_outbox BEFORE INSERT ON OutboxRow " +
                    "BEGIN SELECT RAISE(ABORT, 'falha simulada'); END");

            await Assert.ThrowsAsync<SQLiteException>(() => store.SaveAsync(new("Pickup",
                DateOnly.FromDateTime(DateTime.Today), Guid.NewGuid(), "Cash", "Dinheiro",
                null, null, null, Guid.NewGuid(), 1)));

            Assert.Single(await cart.ListAsync());
            using (var check = new SQLiteConnection(path))
            {
                Assert.Equal(0, check.ExecuteScalar<int>("SELECT COUNT(*) FROM LocalOrderRow"));
                Assert.Equal(0, check.ExecuteScalar<int>("SELECT COUNT(*) FROM LocalOrderItemRow"));
                Assert.Equal(0, check.ExecuteScalar<int>("SELECT COUNT(*) FROM OutboxRow"));
            }
            await cart.CloseAsync();
            await store.CloseAsync();
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
