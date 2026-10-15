using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using MySqlConnector;
using VanderBeer.Application.Operations;
using VanderBeer.Application.Orders;
using VanderBeer.Contracts.Operations;
using VanderBeer.Contracts.Orders;
using VanderBeer.Contracts.Customers;
using VanderBeer.Infrastructure.Persistence;

namespace VanderBeer.Infrastructure.Orders;

public sealed class MySqlOrderRegistry(MySqlConnectionFactory connections, IOrderClock clock) : IOrderRegistry
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string Operation = "POST /api/v1/orders";

    public async Task<OrderPageDto?> ListCustomerAsync(Guid customerId, int page, int pageSize,
        CancellationToken token)
    {
        try
        {
            await using var db = await connections.OpenAsync(token);
            var exists = await db.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM customers WHERE id = @CustomerId", new { CustomerId = customerId },
                cancellationToken: token));
            if (exists == 0) return null;
            var total = await db.ExecuteScalarAsync<long>(new CommandDefinition(
                "SELECT COUNT(*) FROM orders WHERE customer_id = @CustomerId",
                new { CustomerId = customerId }, cancellationToken: token));
            var rows = await db.QueryAsync<OrderRow>(new CommandDefinition(
                "SELECT id AS Id, order_number AS OrderNumber, created_at_utc AS CreatedAtUtc, " +
                "requested_date AS RequestedDate, fulfillment_type AS FulfillmentType, " +
                "status AS Status, total AS Total, version AS Version FROM orders " +
                "WHERE customer_id = @CustomerId ORDER BY created_at_utc DESC, id DESC " +
                "LIMIT @PageSize OFFSET @Offset",
                new { CustomerId = customerId, PageSize = pageSize, Offset = ((long)page - 1) * pageSize },
                cancellationToken: token));
            return new(rows.Select(x => new OrderSummaryDto(x.Id,
                "VB-" + x.OrderNumber.ToString("D8", CultureInfo.InvariantCulture),
                DateTime.SpecifyKind(x.CreatedAtUtc, DateTimeKind.Utc),
                DateOnly.FromDateTime(x.RequestedDate), x.FulfillmentType, x.Status,
                x.Total, x.Version)).ToArray(), page, pageSize, total);
        }
        catch (MySqlException error) { throw new DataStoreUnavailableException(error); }
    }

    public async Task<OrderDto?> GetAsync(Guid id, CancellationToken token)
    {
        try
        {
            await using var db = await connections.OpenAsync(token);
            return await ReadOrderAsync(db, null, id, token);
        }
        catch (MySqlException error) { throw new DataStoreUnavailableException(error); }
    }

    private static async Task<OrderDto?> ReadOrderAsync(MySqlConnection db, MySqlTransaction? tx,
        Guid id, CancellationToken token)
    {
            var row = await db.QuerySingleOrDefaultAsync<OrderRow>(new CommandDefinition(
                "SELECT id AS Id, order_number AS OrderNumber, customer_id AS CustomerId, " +
                "creation_request_id AS ClientRequestId, customer_name AS CustomerName, " +
                "customer_phone AS CustomerPhone, fulfillment_type AS FulfillmentType, " +
                "requested_date AS RequestedDate, CAST(delivery_address_json AS CHAR) AS DeliveryAddressJson, " +
                "payment_method_id AS PaymentMethodId, payment_code AS PaymentCode, " +
                "payment_name AS PaymentName, cash_tendered AS CashTendered, change_due AS ChangeDue, " +
                "delivery_fee AS DeliveryFee, total AS Total, notes AS Notes, status AS Status, " +
                "version AS Version, created_at_utc AS CreatedAtUtc, updated_at_utc AS UpdatedAtUtc " +
                "FROM orders WHERE id = @Id", new { Id = id }, tx, cancellationToken: token));
            if (row is null) return null;
            var itemRows = await db.QueryAsync<OrderItemRow>(new CommandDefinition(
                "SELECT product_id AS ProductId, product_name AS ProductName, unit_label AS UnitLabel, " +
                "quantity AS Quantity, unit_price AS UnitPrice, subtotal AS Subtotal " +
                "FROM order_items WHERE order_id = @Id ORDER BY id", new { Id = id }, tx,
                cancellationToken: token));
            var eventRows = await db.QueryAsync<StatusEventRow>(new CommandDefinition(
                "SELECT sequence AS Sequence, from_status AS FromStatus, to_status AS ToStatus, " +
                "occurred_at_utc AS OccurredAtUtc, reason AS Reason " +
                "FROM order_status_history WHERE order_id = @Id ORDER BY sequence", new { Id = id }, tx,
                cancellationToken: token));
            return new OrderDto(row.Id, "VB-" + row.OrderNumber.ToString("D8", CultureInfo.InvariantCulture),
                DateTime.SpecifyKind(row.CreatedAtUtc, DateTimeKind.Utc),
                DateOnly.FromDateTime(row.RequestedDate), row.FulfillmentType, row.Status, row.Total,
                row.Version, row.ClientRequestId, row.CustomerId,
                new CustomerSnapshotDto(row.CustomerName, row.CustomerPhone),
                new PaymentSnapshotDto(row.PaymentMethodId, row.PaymentCode, row.PaymentName),
                row.DeliveryAddressJson is null ? null : JsonSerializer.Deserialize<AddressDto>(row.DeliveryAddressJson, JsonOptions),
                row.Notes, row.CashTendered, row.ChangeDue, row.DeliveryFee,
                DateTime.SpecifyKind(row.UpdatedAtUtc, DateTimeKind.Utc),
                itemRows.Select(x => new OrderItemDto(x.ProductId, x.ProductName, x.UnitLabel,
                    x.Quantity, x.UnitPrice, x.Subtotal)).ToArray(),
                eventRows.Select(x => new StatusEventDto(x.Sequence, x.FromStatus, x.ToStatus,
                    DateTime.SpecifyKind(x.OccurredAtUtc, DateTimeKind.Utc), x.Reason)).ToArray());
    }

    public async Task<OrderPageDto> ListCompanyAsync(int page, int pageSize, string? status,
        DateOnly? fromDate, DateOnly? toDate, CancellationToken token)
    {
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
            DateTime? start = fromDate is null ? null : TimeZoneInfo.ConvertTimeToUtc(
                fromDate.Value.ToDateTime(TimeOnly.MinValue), zone);
            DateTime? end = toDate is null ? null : TimeZoneInfo.ConvertTimeToUtc(
                toDate.Value.AddDays(1).ToDateTime(TimeOnly.MinValue), zone);
            await using var db = await connections.OpenAsync(token);
            const string where = " WHERE (@Status IS NULL OR status = @Status) " +
                "AND (@Start IS NULL OR created_at_utc >= @Start) " +
                "AND (@End IS NULL OR created_at_utc < @End)";
            var args = new { Status = status, Start = start, End = end,
                PageSize = pageSize, Offset = ((long)page - 1) * pageSize };
            var total = await db.ExecuteScalarAsync<long>(new CommandDefinition(
                "SELECT COUNT(*) FROM orders" + where, args, cancellationToken: token));
            var rows = await db.QueryAsync<OrderRow>(new CommandDefinition(
                "SELECT id AS Id, order_number AS OrderNumber, created_at_utc AS CreatedAtUtc, " +
                "requested_date AS RequestedDate, fulfillment_type AS FulfillmentType, " +
                "status AS Status, total AS Total, version AS Version FROM orders" + where +
                " ORDER BY created_at_utc DESC, id DESC LIMIT @PageSize OFFSET @Offset",
                args, cancellationToken: token));
            return new(rows.Select(x => new OrderSummaryDto(x.Id,
                "VB-" + x.OrderNumber.ToString("D8", CultureInfo.InvariantCulture),
                DateTime.SpecifyKind(x.CreatedAtUtc, DateTimeKind.Utc),
                DateOnly.FromDateTime(x.RequestedDate), x.FulfillmentType, x.Status,
                x.Total, x.Version)).ToArray(), page, pageSize, total);
        }
        catch (MySqlException error) { throw new DataStoreUnavailableException(error); }
    }

    public async Task<OrderCommandResult> UpdateStatusAsync(Guid id, UpdateOrderStatusRequest request,
        string traceId, CancellationToken token)
    {
        var value = OrderStatusRules.Normalize(request);
        var operation = $"PATCH /api/v1/admin/orders/{id:D}/status";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            value.ClientRequestId, value.ExpectedVersion, value.Status, value.Reason
        }, JsonOptions)));
        try
        {
            await using var db = await connections.OpenAsync(token);
            await using var tx = await db.BeginTransactionAsync(token);
            try
            {
                await db.ExecuteAsync(new CommandDefinition(
                    "INSERT INTO request_receipts (client_request_id, operation, request_hash, created_at_utc) " +
                    "VALUES (@Key, @Operation, @Hash, UTC_TIMESTAMP(6))",
                    new { Key = value.ClientRequestId, Operation = operation, Hash = hash }, tx,
                    cancellationToken: token));
            }
            catch (MySqlException error) when (error.Number == 1062)
            {
                await tx.RollbackAsync(token);
                return await ReplayStatusAsync(db, value.ClientRequestId, operation, hash, traceId, token);
            }

            var row = await db.QuerySingleOrDefaultAsync<OrderRow>(new CommandDefinition(
                "SELECT id AS Id, status AS Status, version AS Version, fulfillment_type AS FulfillmentType " +
                "FROM orders WHERE id = @Id FOR UPDATE", new { Id = id }, tx, cancellationToken: token));
            OrderCommandResult result;
            if (row is null)
                result = Reject(404, "NotFound", "Pedido não encontrado.", traceId);
            else if (row.Version != value.ExpectedVersion)
                result = Reject(409, "OrderVersionConflict", "O atendimento foi alterado. Atualize o pedido.",
                    traceId, currentVersion: row.Version);
            else if (!OrderStatusRules.CanMove(row.Status, value.Status, row.FulfillmentType))
                result = Reject(409, "InvalidOrderTransition", "Transição de atendimento inválida.", traceId);
            else if (OrderStatusRules.ValidateReason(value) is { Count: > 0 } errors)
                result = Reject(400, "ValidationFailed", "Revise o motivo.", traceId, errors);
            else
            {
                var now = await db.ExecuteScalarAsync<DateTime>(new CommandDefinition(
                    "SELECT UTC_TIMESTAMP(6)", transaction: tx, cancellationToken: token));
                await db.ExecuteAsync(new CommandDefinition(
                    "UPDATE orders SET status = @Status, version = version + 1, updated_at_utc = @Now " +
                    "WHERE id = @Id", new { Id = id, value.Status, Now = now }, tx,
                    cancellationToken: token));
                await db.ExecuteAsync(new CommandDefinition(
                    "INSERT INTO order_status_history (order_id, sequence, from_status, to_status, " +
                    "reason, occurred_at_utc) VALUES (@Id, @Sequence, @FromStatus, @ToStatus, @Reason, @Now)",
                    new { Id = id, Sequence = row.Version + 1, FromStatus = row.Status,
                        ToStatus = value.Status, value.Reason, Now = now }, tx, cancellationToken: token));
                result = new(200, await ReadOrderAsync(db, tx, id, token), null, false);
            }
            await db.ExecuteAsync(new CommandDefinition(
                "UPDATE request_receipts SET original_status = @Status, response_json = @Response, " +
                "resource_type = @ResourceType, resource_id = @ResourceId, completed_at_utc = UTC_TIMESTAMP(6) " +
                "WHERE client_request_id = @Key",
                new { result.Status, Response = JsonSerializer.Serialize(
                    (object?)result.Order ?? result.Problem!, JsonOptions),
                    ResourceType = result.Order is null ? null : "Order", ResourceId = result.Order?.Id,
                    Key = value.ClientRequestId }, tx, cancellationToken: token));
            await tx.CommitAsync(token);
            return result;
        }
        catch (MySqlException error) { throw new DataStoreUnavailableException(error); }
    }

    private static async Task<OrderCommandResult> ReplayStatusAsync(MySqlConnection db, Guid key,
        string operation, byte[] hash, string traceId, CancellationToken token)
    {
        var receipt = await db.QuerySingleAsync<ReceiptRow>(new CommandDefinition(
            "SELECT operation AS Operation, request_hash AS Hash, original_status AS Status, " +
            "CAST(response_json AS CHAR) AS Response FROM request_receipts WHERE client_request_id = @Key",
            new { Key = key }, cancellationToken: token));
        if (receipt.Operation != operation || !hash.SequenceEqual(receipt.Hash))
            return Reject(409, "RequestKeyConflict", "Esta chave já identifica outra alteração.", traceId);
        if (receipt.Status is null || receipt.Response is null) throw new DataStoreUnavailableException();
        return receipt.Status == 200
            ? new(200, JsonSerializer.Deserialize<OrderDto>(receipt.Response, JsonOptions), null, true)
            : new(receipt.Status.Value, null,
                JsonSerializer.Deserialize<ApiProblemDto>(receipt.Response, JsonOptions), true);
    }

    public async Task<OrderCommandResult> CreateAsync(CreateOrderRequest request, string traceId,
        CancellationToken token)
    {
        var value = OrderValidation.Normalize(request);
        var hash = Hash(value);
        try
        {
            await using var db = await connections.OpenAsync(token);
            await using var tx = await db.BeginTransactionAsync(token);
            try
            {
                await db.ExecuteAsync(new CommandDefinition(
                    "INSERT INTO request_receipts (client_request_id, operation, request_hash, created_at_utc) " +
                    "VALUES (@Key, @Operation, @Hash, UTC_TIMESTAMP(6))",
                    new { Key = value.ClientRequestId, Operation, Hash = hash }, tx,
                    cancellationToken: token));
            }
            catch (MySqlException error) when (error.Number == 1062)
            {
                await tx.RollbackAsync(token);
                return await ReplayAsync(db, value.ClientRequestId, hash, traceId, token);
            }

            var result = await ValidateAndCreateAsync(db, tx, value, traceId, token);
            var response = result.Order is not null ? JsonSerializer.Serialize(result.Order, JsonOptions)
                : JsonSerializer.Serialize(result.Problem, JsonOptions);
            await db.ExecuteAsync(new CommandDefinition(
                "UPDATE request_receipts SET original_status = @Status, response_json = @Response, " +
                "resource_type = @ResourceType, resource_id = @ResourceId, completed_at_utc = UTC_TIMESTAMP(6) " +
                "WHERE client_request_id = @Key",
                new { result.Status, Response = response, ResourceType = result.Order is null ? null : "Order",
                    ResourceId = result.Order?.Id, Key = value.ClientRequestId }, tx,
                cancellationToken: token));
            await tx.CommitAsync(token);
            return result;
        }
        catch (MySqlException error) { throw new DataStoreUnavailableException(error); }
    }

    private async Task<OrderCommandResult> ValidateAndCreateAsync(MySqlConnection db,
        MySqlTransaction tx, CreateOrderRequest value, string traceId, CancellationToken token)
    {
        var customer = await db.QuerySingleOrDefaultAsync<CustomerRow>(new CommandDefinition(
            "SELECT id AS Id, full_name AS Name, phone AS Phone, version AS Version " +
            "FROM customers WHERE id = @Id FOR UPDATE", new { Id = value.CustomerId }, tx,
            cancellationToken: token));
        if (customer is null) return Reject(404, "NotFound", "Cliente não encontrado.", traceId);
        if (customer.Version != value.ExpectedCustomerVersion)
            return Reject(409, "CustomerVersionConflict", "O cadastro foi alterado. Revise os dados.",
                traceId, currentVersion: customer.Version);

        var now = await clock.UtcNowAsync(db, tx, token);
        var businessErrors = OrderValidation.ValidateBusiness(value, OrderValidation.TodayInStore(now));
        if (businessErrors.Count > 0) return Reject(400, "ValidationFailed", "Revise o pedido.",
            traceId, businessErrors);

        var payment = await db.QuerySingleOrDefaultAsync<PaymentRow>(new CommandDefinition(
            "SELECT id AS Id, code AS Code, name AS Name, is_active AS IsActive " +
            "FROM payment_methods WHERE id = @Id FOR UPDATE", new { Id = value.PaymentMethodId }, tx,
            cancellationToken: token));
        if (payment is null) return Reject(404, "NotFound", "Forma de pagamento não encontrada.", traceId);
        if (!payment.IsActive) return Reject(409, "PaymentMethodUnavailable",
            "Forma de pagamento indisponível.", traceId, [new("paymentMethodId", "Escolha uma forma ativa.")]);

        Dictionary<Guid, ProductRow> products = [];
        foreach (var id in value.Items.Select(x => x.ProductId).OrderBy(x => x))
        {
            var product = await db.QuerySingleOrDefaultAsync<ProductRow>(new CommandDefinition(
                "SELECT p.id AS Id, p.name AS Name, p.unit_label AS UnitLabel, p.price AS Price, " +
                "p.is_active AS IsActive, p.is_available AS IsAvailable, c.is_active AS CategoryActive " +
                "FROM products p JOIN categories c ON c.id = p.category_id WHERE p.id = @Id FOR UPDATE",
                new { Id = id }, tx, cancellationToken: token));
            if (product is null) return Reject(404, "NotFound", "Produto não encontrado.", traceId);
            products.Add(id, product);
        }
        List<FieldErrorDto> unavailable = [];
        for (var i = 0; i < value.Items.Count; i++)
        {
            var product = products[value.Items[i].ProductId];
            if (!product.IsActive || !product.IsAvailable || !product.CategoryActive)
                unavailable.Add(new($"items[{i}].productId", "Produto indisponível."));
        }
        if (unavailable.Count > 0) return Reject(409, "ProductUnavailable",
            "Revise os produtos indisponíveis.", traceId, unavailable);

        var lines = value.Items.Select(line =>
        {
            var product = products[line.ProductId];
            return new OrderItemDto(line.ProductId, product.Name, product.UnitLabel,
                line.Quantity, product.Price, line.Quantity * product.Price);
        }).ToArray();
        var total = lines.Sum(x => x.Subtotal);
        if (total > 999999.99m || lines.Any(x => x.Subtotal > 999999.99m))
            return Reject(400, "ValidationFailed", "Total acima do limite permitido.", traceId,
                [new("items", "O pedido deve totalizar no máximo R$ 999.999,99.")]);
        var changes = value.Items.Where(line => products[line.ProductId].Price != line.ExpectedUnitPrice)
            .Select(line => new PriceChangeDto(line.ProductId, line.ExpectedUnitPrice,
                products[line.ProductId].Price)).ToArray();
        if (changes.Length > 0)
        {
            var problem = ApiProblemDto.Create(409, "PriceChanged", "O preço mudou. Revise antes de enviar.", traceId)
                with { PriceChanges = changes.Cast<object>().ToArray(), CurrentTotal = total };
            return new(409, null, problem, false);
        }
        if (payment.Code != "Cash" && value.CashTendered is not null)
            return Reject(400, "ValidationFailed", "Troco permitido somente em dinheiro.", traceId,
                [new("cashTendered", "Remova o valor para esta forma de pagamento.")]);
        if (payment.Code == "Cash" && value.CashTendered is { } tendered && tendered < total)
            return Reject(400, "ValidationFailed", "Valor em dinheiro abaixo do total.", traceId,
                [new("cashTendered", "Informe pelo menos o valor total.")]);

        var orderId = Guid.NewGuid();
        var addressJson = value.DeliveryAddress is null ? null :
            JsonSerializer.Serialize(value.DeliveryAddress, JsonOptions);
        var changeDue = value.CashTendered is { } cash ? cash - total : 0m;
        await db.ExecuteAsync(new CommandDefinition(
            "INSERT INTO orders (id, customer_id, creation_request_id, customer_name, customer_phone, " +
            "fulfillment_type, requested_date, delivery_address_json, payment_method_id, payment_code, " +
            "payment_name, cash_tendered, change_due, delivery_fee, total, notes, status, version, " +
            "created_at_utc, updated_at_utc) VALUES (@Id, @CustomerId, @RequestId, @CustomerName, " +
            "@CustomerPhone, @FulfillmentType, @RequestedDate, @DeliveryAddressJson, @PaymentId, " +
            "@PaymentCode, @PaymentName, @CashTendered, @ChangeDue, 0, @Total, @Notes, 'Received', 1, " +
            "@Now, @Now)",
            new { Id = orderId, CustomerId = value.CustomerId, RequestId = value.ClientRequestId,
                CustomerName = customer.Name, CustomerPhone = customer.Phone,
                value.FulfillmentType, RequestedDate = value.RequestedDate.ToDateTime(TimeOnly.MinValue),
                DeliveryAddressJson = addressJson, PaymentId = payment.Id, PaymentCode = payment.Code,
                PaymentName = payment.Name, value.CashTendered, ChangeDue = changeDue, Total = total,
                value.Notes, Now = now }, tx, cancellationToken: token));
        var number = await db.ExecuteScalarAsync<long>(new CommandDefinition("SELECT LAST_INSERT_ID()",
            transaction: tx, cancellationToken: token));
        foreach (var line in lines)
            await db.ExecuteAsync(new CommandDefinition(
                "INSERT INTO order_items (id, order_id, product_id, product_name, unit_label, quantity, " +
                "unit_price, subtotal) VALUES (@Id, @OrderId, @ProductId, @ProductName, @UnitLabel, " +
                "@Quantity, @UnitPrice, @Subtotal)",
                new { Id = Guid.NewGuid(), OrderId = orderId, line.ProductId, line.ProductName,
                    line.UnitLabel, line.Quantity, line.UnitPrice, line.Subtotal }, tx,
                cancellationToken: token));
        await db.ExecuteAsync(new CommandDefinition(
            "INSERT INTO order_status_history (order_id, sequence, from_status, to_status, reason, " +
            "occurred_at_utc) VALUES (@Id, 1, NULL, 'Received', NULL, @Now)",
            new { Id = orderId, Now = now }, tx, cancellationToken: token));

        var utc = DateTime.SpecifyKind(now, DateTimeKind.Utc);
        var order = new OrderDto(orderId, "VB-" + number.ToString("D8", CultureInfo.InvariantCulture),
            utc, value.RequestedDate, value.FulfillmentType, "Received", total, 1,
            value.ClientRequestId, value.CustomerId, new(customer.Name, customer.Phone),
            new(payment.Id, payment.Code, payment.Name), value.DeliveryAddress, value.Notes,
            value.CashTendered, changeDue, 0m, utc, lines,
            [new StatusEventDto(1, null, "Received", utc, null)]);
        return new(201, order, null, false);
    }

    private static async Task<OrderCommandResult> ReplayAsync(MySqlConnection db, Guid key,
        byte[] hash, string traceId, CancellationToken token)
    {
        var receipt = await db.QuerySingleAsync<ReceiptRow>(new CommandDefinition(
            "SELECT operation AS Operation, request_hash AS Hash, original_status AS Status, " +
            "CAST(response_json AS CHAR) AS Response FROM request_receipts WHERE client_request_id = @Key",
            new { Key = key }, cancellationToken: token));
        if (receipt.Operation != Operation || !hash.SequenceEqual(receipt.Hash))
            return Reject(409, "RequestKeyConflict", "Esta chave já identifica outra alteração.", traceId);
        if (receipt.Status is null || receipt.Response is null) throw new DataStoreUnavailableException();
        return receipt.Status is 200 or 201
            ? new(200, JsonSerializer.Deserialize<OrderDto>(receipt.Response, JsonOptions), null, true)
            : new(receipt.Status.Value, null,
                JsonSerializer.Deserialize<ApiProblemDto>(receipt.Response, JsonOptions), true);
    }

    private static byte[] Hash(CreateOrderRequest value)
    {
        var canonical = new
        {
            value.ClientRequestId, value.CustomerId, value.ExpectedCustomerVersion, value.FulfillmentType,
            value.RequestedDate, value.PaymentMethodId,
            CashTendered = value.CashTendered?.ToString("F2", CultureInfo.InvariantCulture),
            value.DeliveryAddress, value.Notes,
            Items = value.Items.Select(x => new { x.ProductId, x.Quantity,
                ExpectedUnitPrice = x.ExpectedUnitPrice.ToString("F2", CultureInfo.InvariantCulture) })
        };
        return SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(canonical, JsonOptions)));
    }

    private static OrderCommandResult Reject(int status, string code, string detail, string traceId,
        IReadOnlyList<FieldErrorDto>? errors = null, int? currentVersion = null) =>
        new(status, null, ApiProblemDto.Create(status, code, detail, traceId, errors, currentVersion), false);

    private sealed class CustomerRow
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public string Phone { get; set; } = "";
        public int Version { get; set; }
    }
    private sealed class PaymentRow
    {
        public Guid Id { get; set; }
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public bool IsActive { get; set; }
    }
    private sealed class ProductRow
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public string UnitLabel { get; set; } = "";
        public decimal Price { get; set; }
        public bool IsActive { get; set; }
        public bool IsAvailable { get; set; }
        public bool CategoryActive { get; set; }
    }
    private sealed class ReceiptRow
    {
        public string Operation { get; set; } = "";
        public byte[] Hash { get; set; } = [];
        public int? Status { get; set; }
        public string? Response { get; set; }
    }
    private sealed class OrderRow
    {
        public Guid Id { get; set; }
        public long OrderNumber { get; set; }
        public Guid CustomerId { get; set; }
        public Guid ClientRequestId { get; set; }
        public string CustomerName { get; set; } = "";
        public string CustomerPhone { get; set; } = "";
        public string FulfillmentType { get; set; } = "";
        public DateTime RequestedDate { get; set; }
        public string? DeliveryAddressJson { get; set; }
        public Guid PaymentMethodId { get; set; }
        public string PaymentCode { get; set; } = "";
        public string PaymentName { get; set; } = "";
        public decimal? CashTendered { get; set; }
        public decimal ChangeDue { get; set; }
        public decimal DeliveryFee { get; set; }
        public decimal Total { get; set; }
        public string? Notes { get; set; }
        public string Status { get; set; } = "";
        public int Version { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
    }
    private sealed class OrderItemRow
    {
        public Guid ProductId { get; set; }
        public string ProductName { get; set; } = "";
        public string UnitLabel { get; set; } = "";
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal Subtotal { get; set; }
    }
    private sealed class StatusEventRow
    {
        public int Sequence { get; set; }
        public string? FromStatus { get; set; }
        public string ToStatus { get; set; } = "";
        public DateTime OccurredAtUtc { get; set; }
        public string? Reason { get; set; }
    }
}
