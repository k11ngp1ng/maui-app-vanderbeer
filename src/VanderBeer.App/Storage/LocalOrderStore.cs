using System.Text.Json;
using SQLite;
using VanderBeer.Contracts.Catalog;
using VanderBeer.Contracts.Customers;
using VanderBeer.Contracts.Orders;

namespace VanderBeer.App.Storage;

public sealed record CheckoutSnapshot(string FulfillmentType, DateOnly RequestedDate,
    Guid PaymentMethodId, string PaymentCode, string PaymentName, decimal? CashTendered,
    AddressDto? DeliveryAddress, string? Notes, Guid CustomerId, int ExpectedCustomerVersion,
    Guid? DependsOnRequestId = null, string? ApiBaseUrl = null);

public sealed class LocalOrderStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SQLiteAsyncConnection _db;

    public LocalOrderStore() : this(LocalDatabase.Path) { }
    public LocalOrderStore(string path) => _db = new(path);
    public Task CloseAsync() => _db.CloseAsync();

    public async Task<Guid> SaveAsync(CheckoutSnapshot checkout)
    {
        await _db.CreateTableAsync<CartStore.CartItemRow>();
        await _db.CreateTableAsync<LocalOrderRow>();
        await _db.CreateTableAsync<LocalOrderItemRow>();
        await _db.CreateTableAsync<OutboxRow>();
        var id = Guid.NewGuid();
        var key = Guid.NewGuid();
        var now = DateTime.UtcNow.ToString("O");
        await _db.RunInTransactionAsync(connection =>
        {
            var items = connection.Table<CartStore.CartItemRow>().ToList();
            if (items.Count is < 1 or > 50) throw new InvalidOperationException("O carrinho deve ter de 1 a 50 produtos.");
            var totalCents = items.Sum(x => checked(x.ExpectedUnitPriceCents * x.Quantity));
            if (totalCents > 99999999) throw new InvalidOperationException("O total estimado excede o limite da loja.");
            var orderId = id.ToString("D");
            connection.Insert(new LocalOrderRow { LocalId = orderId, ClientRequestId = key.ToString("D"),
                CheckoutJson = JsonSerializer.Serialize(checkout, JsonOptions), SyncStatus = "Pending",
                CreatedAtUtc = now, UpdatedAtUtc = now });
            foreach (var item in items)
            {
                connection.Insert(new LocalOrderItemRow { LocalOrderId = orderId,
                    ProductId = item.ProductId, ProductName = item.ProductName,
                    UnitLabel = item.UnitLabel, Quantity = item.Quantity,
                    ExpectedUnitPriceCents = item.ExpectedUnitPriceCents });
                connection.Delete(item);
            }
            connection.Insert(new OutboxRow { Id = Guid.NewGuid().ToString("D"), LocalOrderId = orderId,
                ClientRequestId = key.ToString("D"), SnapshotJson = JsonSerializer.Serialize(checkout, JsonOptions),
                Sequence = connection.ExecuteScalar<int>("SELECT COALESCE(MAX(sequence), 0) FROM OutboxRow") + 1,
                Operation = "CreateOrder", DispatchMode = "Automatic", TargetKey = orderId,
                DependsOnId = checkout.DependsOnRequestId?.ToString("D"),
                BaseUrl = checkout.ApiBaseUrl,
                Status = "Pending", CreatedAtUtc = now, UpdatedAtUtc = now });
        });
        return id;
    }

    public async Task<LocalOrderRow?> ReadAsync(Guid id)
    {
        await _db.CreateTableAsync<LocalOrderRow>();
        return await _db.FindAsync<LocalOrderRow>(id.ToString("D"));
    }

    public async Task<IReadOnlyList<LocalOrderItemRow>> ItemsAsync(Guid id)
    {
        await _db.CreateTableAsync<LocalOrderItemRow>();
        var localId = id.ToString("D");
        return await _db.Table<LocalOrderItemRow>().Where(x => x.LocalOrderId == localId).ToListAsync();
    }

    public async Task<OutboxRow?> OutboxAsync(Guid id)
    {
        await _db.CreateTableAsync<OutboxRow>();
        var localId = id.ToString("D");
        return await _db.Table<OutboxRow>().Where(x => x.LocalOrderId == localId).FirstOrDefaultAsync();
    }

    public async Task<IReadOnlyList<LocalOrderRow>> ListAsync()
    {
        await _db.CreateTableAsync<LocalOrderRow>();
        return await _db.Table<LocalOrderRow>().OrderByDescending(x => x.CreatedAtUtc).ToListAsync();
    }

    public async Task<(CheckoutSnapshot Checkout, IReadOnlyList<Guid> Unavailable)> RestoreForReviewAsync(
        Guid id, IReadOnlyDictionary<Guid, ProductDto?> currentProducts)
    {
        await _db.CreateTableAsync<CartStore.CartItemRow>();
        await _db.CreateTableAsync<LocalOrderRow>();
        await _db.CreateTableAsync<LocalOrderItemRow>();
        await _db.CreateTableAsync<OutboxRow>();
        CheckoutSnapshot? checkout = null;
        List<Guid> unavailable = [];
        await _db.RunInTransactionAsync(connection =>
        {
            var localId = id.ToString("D");
            var order = connection.Find<LocalOrderRow>(localId)
                ?? throw new InvalidOperationException("Pedido local não encontrado.");
            var outbox = connection.Table<OutboxRow>().First(x => x.LocalOrderId == localId);
            if (outbox.Status != "RequiresReview" || outbox.RequestJson is null)
                throw new InvalidOperationException("Somente uma tentativa rejeitada pode ser revisada.");
            if (connection.Table<CartStore.CartItemRow>().Count() != 0)
                throw new InvalidOperationException("Esvazie o carrinho antes de revisar este pedido.");
            checkout = JsonSerializer.Deserialize<CheckoutSnapshot>(order.CheckoutJson, JsonOptions)
                ?? throw new InvalidDataException("Finalização original inválida.");
            var lines = connection.Table<LocalOrderItemRow>().Where(x => x.LocalOrderId == localId).ToList();
            foreach (var line in lines)
            {
                var productId = Guid.Parse(line.ProductId);
                currentProducts.TryGetValue(productId, out var product);
                if (product is null || !product.IsActive || !product.IsAvailable)
                    unavailable.Add(productId);
                var price = product is { IsActive: true, IsAvailable: true }
                    ? checked((long)(product.Price * 100m)) : line.ExpectedUnitPriceCents;
                connection.Insert(new CartStore.CartItemRow
                {
                    ProductId = line.ProductId,
                    ProductName = product?.Name ?? line.ProductName,
                    UnitLabel = product?.UnitLabel ?? line.UnitLabel,
                    Quantity = line.Quantity,
                    ExpectedUnitPriceCents = price,
                    UpdatedAtUtc = DateTime.UtcNow.ToString("O")
                });
            }
        });
        return (checkout!, unavailable);
    }

    public async Task ReviseAsync(Guid id, CheckoutSnapshot checkout)
    {
        await _db.CreateTableAsync<CartStore.CartItemRow>();
        await _db.CreateTableAsync<LocalOrderRow>();
        await _db.CreateTableAsync<LocalOrderItemRow>();
        await _db.CreateTableAsync<OutboxRow>();
        await _db.RunInTransactionAsync(connection =>
        {
            var localId = id.ToString("D");
            var order = connection.Find<LocalOrderRow>(localId)
                ?? throw new InvalidOperationException("Pedido local não encontrado.");
            var outbox = connection.Table<OutboxRow>().First(x => x.LocalOrderId == localId);
            if (outbox.Status != "RequiresReview" || outbox.RequestJson is null)
                throw new InvalidOperationException("A tentativa original ainda não foi rejeitada definitivamente.");
            var cart = connection.Table<CartStore.CartItemRow>().ToList();
            if (cart.Count is < 1 or > 50)
                throw new InvalidOperationException("O carrinho deve ter de 1 a 50 produtos.");
            if (cart.Sum(x => checked(x.ExpectedUnitPriceCents * x.Quantity)) > 99999999)
                throw new InvalidOperationException("O total estimado excede o limite da loja.");
            connection.Execute("DELETE FROM LocalOrderItemRow WHERE LocalOrderId = ?", localId);
            foreach (var item in cart)
            {
                connection.Insert(new LocalOrderItemRow { LocalOrderId = localId, ProductId = item.ProductId,
                    ProductName = item.ProductName, UnitLabel = item.UnitLabel, Quantity = item.Quantity,
                    ExpectedUnitPriceCents = item.ExpectedUnitPriceCents });
                connection.Delete(item);
            }
            var key = Guid.NewGuid().ToString("D");
            var now = DateTime.UtcNow.ToString("O");
            order.ClientRequestId = key;
            order.CheckoutJson = JsonSerializer.Serialize(checkout, JsonOptions);
            order.SyncStatus = "Pending";
            order.UpdatedAtUtc = now;
            outbox.ClientRequestId = key;
            outbox.SnapshotJson = order.CheckoutJson;
            outbox.DependsOnId = checkout.DependsOnRequestId?.ToString("D");
            outbox.BaseUrl = checkout.ApiBaseUrl;
            outbox.RequestPath = null;
            outbox.RequestJson = null;
            outbox.ResultJson = null;
            outbox.LastErrorJson = null;
            outbox.Attempts = 0;
            outbox.CycleAttempts = 0;
            outbox.NextAttemptAtUtc = null;
            outbox.Status = "Pending";
            outbox.UpdatedAtUtc = now;
            connection.Update(order);
            connection.Update(outbox);
        });
    }

    public async Task<OutboxRow> PrepareSendAsync(Guid id, CustomerDto? resolvedCustomer = null)
    {
        await _db.CreateTableAsync<LocalOrderRow>();
        await _db.CreateTableAsync<LocalOrderItemRow>();
        await _db.CreateTableAsync<OutboxRow>();
        OutboxRow? prepared = null;
        await _db.RunInTransactionAsync(connection =>
        {
            var order = connection.Find<LocalOrderRow>(id.ToString("D"))
                ?? throw new InvalidOperationException("Pedido local não encontrado.");
            var outbox = connection.Table<OutboxRow>().FirstOrDefault(x => x.LocalOrderId == order.LocalId)
                ?? throw new InvalidOperationException("Fila do pedido não encontrada.");
            if (outbox.Status == "Succeeded") { prepared = outbox; return; }
            if (outbox.Status == "RequiresReview")
                throw new InvalidOperationException("Revise o pedido antes de uma nova tentativa.");
            if (outbox.RequestJson is null)
            {
                var checkout = JsonSerializer.Deserialize<CheckoutSnapshot>(outbox.SnapshotJson, JsonOptions)
                    ?? throw new InvalidDataException("Dados da finalização inválidos.");
                var items = connection.Table<LocalOrderItemRow>().Where(x => x.LocalOrderId == order.LocalId)
                    .ToList().Select(x => new OrderLineRequest(Guid.Parse(x.ProductId), x.Quantity,
                        x.ExpectedUnitPriceCents / 100m)).ToList();
                if (items.Count == 0) throw new InvalidDataException("Pedido sem itens.");
                var customerId = resolvedCustomer?.Id ?? checkout.CustomerId;
                var customerVersion = resolvedCustomer?.Version ?? checkout.ExpectedCustomerVersion;
                if (customerId == Guid.Empty || customerVersion < 1)
                    throw new InvalidOperationException("Confirme o cadastro antes de enviar o pedido.");
                var request = new CreateOrderRequest(Guid.Parse(outbox.ClientRequestId), customerId,
                    customerVersion, checkout.FulfillmentType, checkout.RequestedDate,
                    checkout.PaymentMethodId, checkout.CashTendered, checkout.DeliveryAddress,
                    checkout.Notes, items);
                outbox.RequestPath = "/api/v1/orders";
                outbox.RequestJson = JsonSerializer.Serialize(request, JsonOptions);
                outbox.DependsOnId = null;
            }
            outbox.Status = "Sending";
            outbox.Attempts++;
            outbox.CycleAttempts++;
            outbox.NextAttemptAtUtc = null;
            outbox.UpdatedAtUtc = DateTime.UtcNow.ToString("O");
            order.SyncStatus = "Sending";
            order.UpdatedAtUtc = outbox.UpdatedAtUtc;
            connection.Update(outbox);
            connection.Update(order);
            prepared = outbox;
        });
        return prepared!;
    }

    public async Task<IReadOnlyList<Guid>> PendingIdsAsync(DateTime utcNow)
    {
        await _db.CreateTableAsync<OutboxRow>();
        var rows = await _db.Table<OutboxRow>().Where(x => x.DispatchMode == "Automatic" &&
            (x.Status == "Pending" || x.Status == "Sending")).OrderBy(x => x.Sequence).ToListAsync();
        return rows.Where(x => x.CycleAttempts < 5 &&
            (x.NextAttemptAtUtc is null || DateTime.Parse(x.NextAttemptAtUtc,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind) <= utcNow))
            .Select(x => Guid.Parse(x.LocalOrderId)).ToArray();
    }

    public async Task ResetCycleAsync(Guid id)
    {
        await _db.CreateTableAsync<OutboxRow>();
        var localId = id.ToString("D");
        await _db.RunInTransactionAsync(connection =>
        {
            var row = connection.Table<OutboxRow>().FirstOrDefault(x => x.LocalOrderId == localId);
            if (row is null || row.Status is "Succeeded" or "RequiresReview") return;
            row.CycleAttempts = 0;
            row.Status = "Pending";
            // Preserve a server Retry-After deadline across app restarts.
            connection.Update(row);
            var order = connection.Find<LocalOrderRow>(localId);
            if (order is not null) { order.SyncStatus = "Pending"; connection.Update(order); }
        });
    }

    public async Task MarkTransientAsync(Guid id, TimeSpan delay, string? error = null)
    {
        await _db.RunInTransactionAsync(connection =>
        {
            var order = connection.Find<LocalOrderRow>(id.ToString("D"))!;
            var outbox = connection.Table<OutboxRow>().First(x => x.LocalOrderId == order.LocalId);
            if (outbox.Status == "Succeeded") return;
            outbox.Status = "Pending";
            outbox.NextAttemptAtUtc = DateTime.UtcNow.Add(delay).ToString("O");
            outbox.LastErrorJson = error;
            outbox.UpdatedAtUtc = DateTime.UtcNow.ToString("O");
            order.SyncStatus = "Pending";
            order.UpdatedAtUtc = outbox.UpdatedAtUtc;
            connection.Update(outbox);
            connection.Update(order);
        });
    }

    public async Task ConfirmAsync(Guid id, OrderDto result)
    {
        await _db.CreateTableAsync<OrderReceiptRow>();
        await _db.CreateTableAsync<RemoteOrderRow>();
        await _db.RunInTransactionAsync(connection =>
        {
            var order = connection.Find<LocalOrderRow>(id.ToString("D"))!;
            var outbox = connection.Table<OutboxRow>().First(x => x.LocalOrderId == order.LocalId);
            if (result.ClientRequestId.ToString("D") != outbox.ClientRequestId)
                throw new InvalidDataException("A resposta pertence a outra tentativa.");
            var json = JsonSerializer.Serialize(result, JsonOptions);
            var receipt = connection.Find<OrderReceiptRow>(order.LocalId) ?? new() { LocalOrderId = order.LocalId };
            receipt.ServerOrderId = result.Id.ToString("D");
            var cached = string.IsNullOrEmpty(receipt.OrderJson) ? null :
                JsonSerializer.Deserialize<OrderDto>(receipt.OrderJson, JsonOptions);
            if (cached is not null && cached.Id != result.Id)
                throw new InvalidDataException("O recibo local pertence a outro pedido.");
            if (cached is null || cached.Version <= result.Version) receipt.OrderJson = json;
            connection.InsertOrReplace(receipt);
            outbox.ResultJson = json;
            outbox.Status = "Succeeded";
            outbox.NextAttemptAtUtc = null;
            outbox.LastErrorJson = null;
            outbox.UpdatedAtUtc = DateTime.UtcNow.ToString("O");
            order.SyncStatus = "Synchronized";
            order.UpdatedAtUtc = outbox.UpdatedAtUtc;
            connection.Update(outbox);
            connection.Update(order);
            connection.Execute("DELETE FROM RemoteOrderRow WHERE ServerOrderId = ?", result.Id.ToString("D"));
        });
    }

    public async Task MarkReviewAsync(Guid id, string problemJson)
    {
        await _db.RunInTransactionAsync(connection =>
        {
            var order = connection.Find<LocalOrderRow>(id.ToString("D"))!;
            var outbox = connection.Table<OutboxRow>().First(x => x.LocalOrderId == order.LocalId);
            outbox.Status = "RequiresReview";
            outbox.LastErrorJson = problemJson;
            outbox.UpdatedAtUtc = DateTime.UtcNow.ToString("O");
            order.SyncStatus = "RequiresReview";
            order.UpdatedAtUtc = outbox.UpdatedAtUtc;
            connection.Update(outbox);
            connection.Update(order);
        });
    }

    public async Task<OrderDto?> ReceiptAsync(Guid id)
    {
        await _db.CreateTableAsync<OrderReceiptRow>();
        var row = await _db.FindAsync<OrderReceiptRow>(id.ToString("D"));
        return row is null ? null : JsonSerializer.Deserialize<OrderDto>(row.OrderJson, JsonOptions);
    }

    public async Task<IReadOnlyList<OrderDto>> RemoteOrdersAsync()
    {
        await _db.CreateTableAsync<RemoteOrderRow>();
        var rows = await _db.Table<RemoteOrderRow>().ToListAsync();
        return rows.Select(x => JsonSerializer.Deserialize<OrderDto>(x.OrderJson, JsonOptions)!)
            .OrderByDescending(x => x.CreatedAtUtc).ToArray();
    }

    public async Task SaveRemoteAsync(OrderDto result)
    {
        await _db.CreateTableAsync<LocalOrderRow>();
        await _db.CreateTableAsync<OutboxRow>();
        await _db.CreateTableAsync<OrderReceiptRow>();
        await _db.CreateTableAsync<RemoteOrderRow>();
        await _db.RunInTransactionAsync(connection =>
        {
            var serverId = result.Id.ToString("D");
            var key = result.ClientRequestId.ToString("D");
            var receipt = connection.Table<OrderReceiptRow>().FirstOrDefault(x => x.ServerOrderId == serverId);
            var local = receipt is null
                ? connection.Table<LocalOrderRow>().FirstOrDefault(x => x.ClientRequestId == key)
                : connection.Find<LocalOrderRow>(receipt.LocalOrderId);
            var json = JsonSerializer.Serialize(result, JsonOptions);
            if (local is not null)
            {
                if (local.ClientRequestId != key)
                    throw new InvalidDataException("Pedido remoto não corresponde à chave local.");
                receipt ??= new OrderReceiptRow { LocalOrderId = local.LocalId };
                var previous = string.IsNullOrEmpty(receipt.OrderJson) ? null :
                    JsonSerializer.Deserialize<OrderDto>(receipt.OrderJson, JsonOptions);
                if (previous is not null && previous.Id != result.Id)
                    throw new InvalidDataException("Pedido remoto diverge do recibo local.");
                receipt.ServerOrderId = serverId;
                if (previous is null || previous.Version <= result.Version) receipt.OrderJson = json;
                connection.InsertOrReplace(receipt);
                var outbox = connection.Table<OutboxRow>().First(x => x.LocalOrderId == local.LocalId);
                outbox.ResultJson ??= json;
                outbox.Status = "Succeeded";
                outbox.NextAttemptAtUtc = null;
                outbox.LastErrorJson = null;
                outbox.UpdatedAtUtc = DateTime.UtcNow.ToString("O");
                local.SyncStatus = "Synchronized";
                local.UpdatedAtUtc = outbox.UpdatedAtUtc;
                connection.Update(outbox);
                connection.Update(local);
                connection.Execute("DELETE FROM RemoteOrderRow WHERE ServerOrderId = ?", serverId);
            }
            else
            {
                var remote = connection.Find<RemoteOrderRow>(serverId);
                var previous = remote is null ? null :
                    JsonSerializer.Deserialize<OrderDto>(remote.OrderJson, JsonOptions);
                if (previous is null || previous.Version <= result.Version)
                    connection.InsertOrReplace(new RemoteOrderRow { ServerOrderId = serverId, OrderJson = json });
            }
        });
    }

    public sealed class RemoteOrderRow
    {
        [PrimaryKey] public string ServerOrderId { get; set; } = "";
        public string OrderJson { get; set; } = "";
    }

    public sealed class OrderReceiptRow
    {
        [PrimaryKey] public string LocalOrderId { get; set; } = "";
        [Indexed] public string ServerOrderId { get; set; } = "";
        public string OrderJson { get; set; } = "";
    }

    public sealed class LocalOrderRow
    {
        [PrimaryKey] public string LocalId { get; set; } = "";
        [Unique] public string ClientRequestId { get; set; } = "";
        public string CheckoutJson { get; set; } = "";
        public string SyncStatus { get; set; } = "";
        public string CreatedAtUtc { get; set; } = "";
        public string UpdatedAtUtc { get; set; } = "";
    }

    public sealed class LocalOrderItemRow
    {
        [PrimaryKey, AutoIncrement] public int Id { get; set; }
        [Indexed] public string LocalOrderId { get; set; } = "";
        public string ProductId { get; set; } = "";
        public string ProductName { get; set; } = "";
        public string UnitLabel { get; set; } = "";
        public int Quantity { get; set; }
        public long ExpectedUnitPriceCents { get; set; }
    }

    public sealed class OutboxRow
    {
        [PrimaryKey] public string Id { get; set; } = "";
        [Unique] public int Sequence { get; set; }
        public string Operation { get; set; } = "";
        public string DispatchMode { get; set; } = "";
        public string TargetKey { get; set; } = "";
        [Indexed] public string LocalOrderId { get; set; } = "";
        public string? DependsOnId { get; set; }
        [Unique] public string ClientRequestId { get; set; } = "";
        public string SnapshotJson { get; set; } = "";
        public string? RequestPath { get; set; }
        public string? BaseUrl { get; set; }
        public string? RequestJson { get; set; }
        public string Status { get; set; } = "";
        public int Attempts { get; set; }
        public int CycleAttempts { get; set; }
        public string? NextAttemptAtUtc { get; set; }
        public string? LastErrorJson { get; set; }
        public string? ResultJson { get; set; }
        public string CreatedAtUtc { get; set; } = "";
        public string UpdatedAtUtc { get; set; } = "";
    }
}
