using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using MySqlConnector;
using VanderBeer.Application.Catalog;
using VanderBeer.Application.Operations;
using VanderBeer.Contracts.Catalog;
using VanderBeer.Contracts.Operations;
using VanderBeer.Infrastructure.Persistence;

namespace VanderBeer.Infrastructure.Catalog;

public sealed class MySqlProductCatalog(MySqlConnectionFactory connectionFactory) : IProductCatalog
{
    private const string ProductColumns = "p.id AS Id, p.category_id AS CategoryId, p.sku AS Sku, p.name AS Name, " +
        "p.description AS Description, p.unit_label AS UnitLabel, p.price AS Price, p.image_url AS ImageUrl, " +
        "p.is_available AS IsAvailable, p.is_active AS IsActive, p.version AS Version, p.updated_at_utc AS UpdatedAtUtc";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ProductPageDto> ListAsync(ProductQuery query, bool company, CancellationToken cancellationToken)
    {
        try
        {
            await using var db = await connectionFactory.OpenAsync(cancellationToken);
            var where = " WHERE (@Company = 1 OR (p.is_active = 1 AND c.is_active = 1))" +
                " AND (@IsActive IS NULL OR p.is_active = @IsActive)" +
                " AND (@CategoryId IS NULL OR p.category_id = @CategoryId)" +
                " AND (@Search IS NULL OR p.name LIKE @Pattern ESCAPE '=' OR p.sku LIKE @Pattern ESCAPE '=')";
            var args = new { Company = company, query.IsActive, query.CategoryId,
                Search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search,
                Pattern = string.IsNullOrWhiteSpace(query.Search) ? null : $"%{EscapeLike(query.Search.Trim())}%",
                Limit = query.PageSize, Offset = (query.Page - 1) * query.PageSize };
            var from = " FROM products p JOIN categories c ON c.id = p.category_id";
            var total = await db.ExecuteScalarAsync<long>(new CommandDefinition("SELECT COUNT(*)" + from + where,
                args, cancellationToken: cancellationToken));
            var rows = await db.QueryAsync<ProductRow>(new CommandDefinition(
                "SELECT " + ProductColumns + from + where + " ORDER BY p.name, p.id LIMIT @Limit OFFSET @Offset",
                args, cancellationToken: cancellationToken));
            return new(rows.Select(Map).ToArray(), query.Page, query.PageSize, total);
        }
        catch (MySqlException error) { throw new DataStoreUnavailableException(error); }
    }

    public async Task<ProductDto?> GetAsync(Guid id, bool company, CancellationToken cancellationToken)
    {
        try
        {
            await using var db = await connectionFactory.OpenAsync(cancellationToken);
            var row = await db.QuerySingleOrDefaultAsync<ProductRow>(new CommandDefinition(
                "SELECT " + ProductColumns + " FROM products p JOIN categories c ON c.id = p.category_id " +
                "WHERE p.id = @Id AND (@Company = 1 OR (p.is_active = 1 AND c.is_active = 1))",
                new { Id = id, Company = company }, cancellationToken: cancellationToken));
            return row is null ? null : Map(row);
        }
        catch (MySqlException error) { throw new DataStoreUnavailableException(error); }
    }

    public async Task<IReadOnlyList<PaymentMethodDto>> GetPaymentMethodsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var db = await connectionFactory.OpenAsync(cancellationToken);
            var rows = await db.QueryAsync<PaymentRow>(new CommandDefinition(
                "SELECT id AS Id, code AS Code, name AS Name FROM payment_methods WHERE is_active = 1 ORDER BY name, id",
                cancellationToken: cancellationToken));
            return rows.Select(x => new PaymentMethodDto(x.Id, x.Code, x.Name)).ToArray();
        }
        catch (MySqlException error) { throw new DataStoreUnavailableException(error); }
    }

    public Task<ProductCommandResult> CreateAsync(ProductWriteRequest request, string traceId, CancellationToken cancellationToken) =>
        WriteAsync(null, ProductValidation.Normalize(request), null, traceId, cancellationToken);

    public Task<ProductCommandResult> UpdateAsync(Guid id, ProductUpdateRequest request, string traceId, CancellationToken cancellationToken) =>
        WriteAsync(id, ProductValidation.Normalize(new(request.ClientRequestId, request.CategoryId,
            request.Sku, request.Name, request.Description, request.UnitLabel, request.Price,
            request.ImageUrl, request.IsAvailable, request.IsActive)), request.ExpectedVersion,
            traceId, cancellationToken);

    private async Task<ProductCommandResult> WriteAsync(Guid? id, ProductWriteRequest value, int? expectedVersion,
        string traceId, CancellationToken cancellationToken, bool retriedSkuRace = false)
    {
        var operation = id is null ? "POST /api/v1/admin/products" : $"PUT /api/v1/admin/products/{id.Value:D}";
        var canonical = new { value.CategoryId, value.Sku, value.Name, value.Description,
            value.UnitLabel, Price = value.Price.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
            value.ImageUrl, value.IsAvailable, value.IsActive, value.ClientRequestId, ExpectedVersion = expectedVersion };
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(canonical, JsonOptions)));
        try
        {
            await using var db = await connectionFactory.OpenAsync(cancellationToken);
            await using var tx = await db.BeginTransactionAsync(cancellationToken);
            try
            {
                await db.ExecuteAsync(new CommandDefinition(
                    "INSERT INTO request_receipts (client_request_id, operation, request_hash, created_at_utc) " +
                    "VALUES (@Key, @Operation, @Hash, UTC_TIMESTAMP(6))",
                    new { Key = value.ClientRequestId, Operation = operation, Hash = hash }, tx,
                    cancellationToken: cancellationToken));
            }
            catch (MySqlException error) when (error.Number == 1062)
            {
                await tx.RollbackAsync(cancellationToken);
                return await ReplayAsync(db, value.ClientRequestId, operation, hash, traceId, cancellationToken);
            }

            ProductCommandResult result;
            if (id is not null)
            {
                var current = await db.QuerySingleOrDefaultAsync<ProductRow>(new CommandDefinition(
                    "SELECT " + ProductColumns + " FROM products p WHERE p.id = @Id FOR UPDATE",
                    new { Id = id }, tx, cancellationToken: cancellationToken));
                if (current is null)
                    result = Reject(404, "NotFound", "Produto não encontrado.", traceId);
                else if (current.Version != expectedVersion)
                    result = Reject(409, "ProductVersionConflict", "O produto foi alterado. Atualize e revise os campos.",
                        traceId, currentVersion: current.Version);
                else
                    result = await ValidateAndSaveAsync(db, tx, id, value, traceId, cancellationToken);
            }
            else result = await ValidateAndSaveAsync(db, tx, null, value, traceId, cancellationToken);

            var response = result.Product is not null
                ? JsonSerializer.Serialize(result.Product, JsonOptions)
                : JsonSerializer.Serialize(result.Problem, JsonOptions);
            await db.ExecuteAsync(new CommandDefinition(
                "UPDATE request_receipts SET original_status = @Status, response_json = @Response, " +
                "resource_type = @ResourceType, resource_id = @ResourceId, completed_at_utc = UTC_TIMESTAMP(6) " +
                "WHERE client_request_id = @Key",
                new { result.Status, Response = response, ResourceType = result.Product is null ? null : "Product",
                    ResourceId = result.Product?.Id, Key = value.ClientRequestId }, tx,
                cancellationToken: cancellationToken));
            await tx.CommitAsync(cancellationToken);
            return result;
        }
        catch (MySqlException error) when (error.Number == 1062 && !retriedSkuRace)
        {
            // A competing insert may win after our duplicate check. Retry once to record DuplicateSku.
            return await WriteAsync(id, value, expectedVersion, traceId, cancellationToken, true);
        }
        catch (MySqlException error) { throw new DataStoreUnavailableException(error); }
    }

    private static async Task<ProductCommandResult> ValidateAndSaveAsync(MySqlConnection db, MySqlTransaction tx,
        Guid? id, ProductWriteRequest value, string traceId, CancellationToken cancellationToken)
    {
        var categoryActive = await db.ExecuteScalarAsync<int?>(new CommandDefinition(
            "SELECT is_active FROM categories WHERE id = @Id FOR UPDATE", new { Id = value.CategoryId }, tx,
            cancellationToken: cancellationToken));
        if (categoryActive is null) return Reject(404, "NotFound", "Categoria não encontrada.", traceId);
        if (categoryActive == 0) return Reject(409, "CategoryUnavailable", "Categoria inativa.", traceId,
            [new("categoryId", "Escolha uma categoria ativa.")]);
        var duplicate = await db.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            "SELECT id FROM products WHERE sku = @Sku AND (@Id IS NULL OR id <> @Id) LIMIT 1",
            new { value.Sku, Id = id }, tx, cancellationToken: cancellationToken));
        if (duplicate is not null) return Reject(409, "DuplicateSku", "SKU já cadastrado.", traceId,
            [new("sku", "Informe um SKU diferente.")]);
        var productId = id ?? Guid.NewGuid();
        if (id is null)
            await db.ExecuteAsync(new CommandDefinition(
                "INSERT INTO products (id, category_id, sku, name, description, unit_label, price, image_url, " +
                "is_available, is_active, version, created_at_utc, updated_at_utc) VALUES " +
                "(@Id, @CategoryId, @Sku, @Name, @Description, @UnitLabel, @Price, @ImageUrl, " +
                "@IsAvailable, @IsActive, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6))",
                new { Id = productId, value.CategoryId, value.Sku, value.Name, value.Description,
                    value.UnitLabel, value.Price, value.ImageUrl, value.IsAvailable, value.IsActive }, tx,
                cancellationToken: cancellationToken));
        else
            await db.ExecuteAsync(new CommandDefinition(
                "UPDATE products SET category_id = @CategoryId, sku = @Sku, name = @Name, description = @Description, " +
                "unit_label = @UnitLabel, price = @Price, image_url = @ImageUrl, is_available = @IsAvailable, " +
                "is_active = @IsActive, version = version + 1, updated_at_utc = UTC_TIMESTAMP(6) WHERE id = @Id",
                new { Id = productId, value.CategoryId, value.Sku, value.Name, value.Description,
                    value.UnitLabel, value.Price, value.ImageUrl, value.IsAvailable, value.IsActive }, tx,
                cancellationToken: cancellationToken));
        var row = await db.QuerySingleAsync<ProductRow>(new CommandDefinition(
            "SELECT " + ProductColumns + " FROM products p WHERE p.id = @Id",
            new { Id = productId }, tx, cancellationToken: cancellationToken));
        return new(id is null ? 201 : 200, Map(row), null, false);
    }

    private static async Task<ProductCommandResult> ReplayAsync(MySqlConnection db, Guid key,
        string operation, byte[] hash, string traceId, CancellationToken cancellationToken)
    {
        var receipt = await db.QuerySingleAsync<ReceiptRow>(new CommandDefinition(
            "SELECT operation AS Operation, request_hash AS Hash, original_status AS Status, " +
            "CAST(response_json AS CHAR) AS Response FROM request_receipts WHERE client_request_id = @Key",
            new { Key = key }, cancellationToken: cancellationToken));
        if (receipt.Operation != operation || !hash.SequenceEqual(receipt.Hash))
            return Reject(409, "RequestKeyConflict", "Esta chave já identifica outra alteração.", traceId);
        if (receipt.Status is null || receipt.Response is null)
            throw new DataStoreUnavailableException();
        return receipt.Status is 200 or 201
            ? new(200, JsonSerializer.Deserialize<ProductDto>(receipt.Response, JsonOptions), null, true)
            : new(receipt.Status.Value, null, JsonSerializer.Deserialize<ApiProblemDto>(receipt.Response, JsonOptions), true);
    }

    private static ProductCommandResult Reject(int status, string code, string detail, string traceId,
        IReadOnlyList<FieldErrorDto>? errors = null, int? currentVersion = null) =>
        new(status, null, ApiProblemDto.Create(status, code, detail, traceId, errors, currentVersion), false);

    private static string EscapeLike(string value) => value.Replace("=", "==").Replace("%", "=%").Replace("_", "=_");
    private static ProductDto Map(ProductRow x) => new(x.Id, x.CategoryId, x.Sku, x.Name,
        x.Description, x.UnitLabel, x.Price, x.ImageUrl, x.IsAvailable, x.IsActive,
        x.Version, DateTime.SpecifyKind(x.UpdatedAtUtc, DateTimeKind.Utc));

    private sealed class ProductRow
    {
        public Guid Id { get; set; }
        public Guid CategoryId { get; set; }
        public string Sku { get; set; } = "";
        public string Name { get; set; } = "";
        public string? Description { get; set; }
        public string UnitLabel { get; set; } = "";
        public decimal Price { get; set; }
        public string? ImageUrl { get; set; }
        public bool IsAvailable { get; set; }
        public bool IsActive { get; set; }
        public int Version { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
    }
    private sealed class PaymentRow { public Guid Id { get; set; } public string Code { get; set; } = ""; public string Name { get; set; } = ""; }
    private sealed class ReceiptRow { public string Operation { get; set; } = ""; public byte[] Hash { get; set; } = []; public int? Status { get; set; } public string? Response { get; set; } }
}
