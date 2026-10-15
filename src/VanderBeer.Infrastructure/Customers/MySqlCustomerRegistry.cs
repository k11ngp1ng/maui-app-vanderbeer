using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using MySqlConnector;
using VanderBeer.Application.Customers;
using VanderBeer.Application.Operations;
using VanderBeer.Contracts.Customers;
using VanderBeer.Contracts.Operations;
using VanderBeer.Infrastructure.Persistence;

namespace VanderBeer.Infrastructure.Customers;

public sealed class MySqlCustomerRegistry(MySqlConnectionFactory connections) : ICustomerRegistry
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string CustomerSql = "SELECT c.id AS Id, c.customer_type AS CustomerType, c.full_name AS FullName, " +
        "c.company_name AS CompanyName, c.cpf AS Cpf, c.cnpj AS Cnpj, c.phone AS Phone, c.email AS Email, " +
        "c.version AS Version, c.created_at_utc AS CreatedAtUtc, c.updated_at_utc AS UpdatedAtUtc, " +
        "a.postal_code AS PostalCode, a.street AS Street, a.number AS Number, a.complement AS Complement, " +
        "a.district AS District, a.city AS City, a.state AS State, a.reference AS Reference " +
        "FROM customers c LEFT JOIN customer_addresses a ON a.customer_id = c.id WHERE c.id = @Id";

    public async Task<CustomerDto?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await using var db = await connections.OpenAsync(cancellationToken);
            var row = await db.QuerySingleOrDefaultAsync<CustomerRow>(new CommandDefinition(CustomerSql,
                new { Id = id }, cancellationToken: cancellationToken));
            return row is null ? null : Map(row);
        }
        catch (MySqlException error) { throw new DataStoreUnavailableException(error); }
    }

    public Task<CustomerCommandResult> CreateAsync(CustomerWriteRequest request, string traceId,
        CancellationToken cancellationToken) => WriteAsync(null, CustomerValidation.Normalize(request), null,
            traceId, cancellationToken);

    public Task<CustomerCommandResult> UpdateAsync(Guid id, CustomerUpdateRequest request, string traceId,
        CancellationToken cancellationToken) => WriteAsync(id, CustomerValidation.Normalize(new(request.ClientRequestId,
            request.CustomerType, request.FullName, request.CompanyName, request.Cpf, request.Cnpj,
            request.Phone, request.Email, request.Address)), request.ExpectedVersion, traceId, cancellationToken);

    private async Task<CustomerCommandResult> WriteAsync(Guid? id, CustomerWriteRequest value, int? expectedVersion,
        string traceId, CancellationToken token, bool retriedIdentityRace = false)
    {
        var operation = id is null ? "POST /api/v1/customers" : $"PUT /api/v1/customers/{id.Value:D}";
        var canonical = new { value.ClientRequestId, value.CustomerType, value.FullName, value.CompanyName,
            value.Cpf, value.Cnpj, value.Phone, value.Email, value.Address, ExpectedVersion = expectedVersion };
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(canonical, JsonOptions)));
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
                return await ReplayAsync(db, value.ClientRequestId, operation, hash, traceId, token);
            }

            CustomerCommandResult result;
            if (id is not null)
            {
                var version = await db.ExecuteScalarAsync<int?>(new CommandDefinition(
                    "SELECT version FROM customers WHERE id = @Id FOR UPDATE", new { Id = id }, tx,
                    cancellationToken: token));
                result = version is null ? Reject(404, "NotFound", "Cliente não encontrado.", traceId)
                    : version != expectedVersion ? Reject(409, "CustomerVersionConflict",
                        "O cadastro foi alterado. Atualize e revise os dados.", traceId, currentVersion: version)
                    : await ValidateAndSaveAsync(db, tx, id, value, traceId, token);
            }
            else result = await ValidateAndSaveAsync(db, tx, null, value, traceId, token);

            var response = result.Customer is not null ? JsonSerializer.Serialize(result.Customer, JsonOptions)
                : JsonSerializer.Serialize(result.Problem, JsonOptions);
            await db.ExecuteAsync(new CommandDefinition(
                "UPDATE request_receipts SET original_status = @Status, response_json = @Response, " +
                "resource_type = @ResourceType, resource_id = @ResourceId, completed_at_utc = UTC_TIMESTAMP(6) " +
                "WHERE client_request_id = @Key",
                new { result.Status, Response = response, ResourceType = result.Customer is null ? null : "Customer",
                    ResourceId = result.Customer?.Id, Key = value.ClientRequestId }, tx,
                cancellationToken: token));
            await tx.CommitAsync(token);
            return result;
        }
        catch (MySqlException error) when (error.Number == 1062 && !retriedIdentityRace)
        {
            return await WriteAsync(id, value, expectedVersion, traceId, token, true);
        }
        catch (MySqlException error) { throw new DataStoreUnavailableException(error); }
    }

    private static async Task<CustomerCommandResult> ValidateAndSaveAsync(MySqlConnection db, MySqlTransaction tx,
        Guid? id, CustomerWriteRequest value, string traceId, CancellationToken token)
    {
        var duplicate = await db.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            "SELECT id FROM customers WHERE ((@Cpf IS NOT NULL AND cpf = @Cpf) OR " +
            "(@Cnpj IS NOT NULL AND cnpj = @Cnpj)) AND (@Id IS NULL OR id <> @Id) LIMIT 1",
            new { value.Cpf, value.Cnpj, Id = id }, tx, cancellationToken: token));
        if (duplicate is not null)
            return Reject(409, value.Cpf is null ? "DuplicateCnpj" : "DuplicateCpf",
                "Documento já cadastrado.", traceId,
                [new(value.Cpf is null ? "cnpj" : "cpf", "Use outro documento ou revise o cadastro local.")]);

        var customerId = id ?? Guid.NewGuid();
        if (id is null)
            await db.ExecuteAsync(new CommandDefinition(
                "INSERT INTO customers (id, customer_type, full_name, company_name, cpf, cnpj, phone, email, " +
                "version, created_at_utc, updated_at_utc) VALUES (@Id, @CustomerType, @FullName, @CompanyName, " +
                "@Cpf, @Cnpj, @Phone, @Email, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6))",
                new { Id = customerId, value.CustomerType, value.FullName, value.CompanyName, value.Cpf,
                    value.Cnpj, value.Phone, value.Email }, tx, cancellationToken: token));
        else
            await db.ExecuteAsync(new CommandDefinition(
                "UPDATE customers SET customer_type = @CustomerType, full_name = @FullName, " +
                "company_name = @CompanyName, cpf = @Cpf, cnpj = @Cnpj, phone = @Phone, email = @Email, " +
                "version = version + 1, updated_at_utc = UTC_TIMESTAMP(6) WHERE id = @Id",
                new { Id = customerId, value.CustomerType, value.FullName, value.CompanyName, value.Cpf,
                    value.Cnpj, value.Phone, value.Email }, tx, cancellationToken: token));

        await db.ExecuteAsync(new CommandDefinition("DELETE FROM customer_addresses WHERE customer_id = @Id",
            new { Id = customerId }, tx, cancellationToken: token));
        if (value.Address is { } address)
            await db.ExecuteAsync(new CommandDefinition(
                "INSERT INTO customer_addresses (customer_id, postal_code, street, number, complement, " +
                "district, city, state, reference) VALUES (@CustomerId, @PostalCode, @Street, @Number, " +
                "@Complement, @District, @City, @State, @Reference)",
                new { CustomerId = customerId, address.PostalCode, address.Street, address.Number,
                    address.Complement, address.District, address.City, address.State, address.Reference }, tx,
                cancellationToken: token));

        var saved = await db.QuerySingleAsync<CustomerRow>(new CommandDefinition(CustomerSql,
            new { Id = customerId }, tx, cancellationToken: token));
        return new(id is null ? 201 : 200, Map(saved), null, false);
    }

    private static async Task<CustomerCommandResult> ReplayAsync(MySqlConnection db, Guid key, string operation,
        byte[] hash, string traceId, CancellationToken token)
    {
        var receipt = await db.QuerySingleAsync<ReceiptRow>(new CommandDefinition(
            "SELECT operation AS Operation, request_hash AS Hash, original_status AS Status, " +
            "CAST(response_json AS CHAR) AS Response FROM request_receipts WHERE client_request_id = @Key",
            new { Key = key }, cancellationToken: token));
        if (receipt.Operation != operation || !hash.SequenceEqual(receipt.Hash))
            return Reject(409, "RequestKeyConflict", "Esta chave já identifica outra alteração.", traceId);
        if (receipt.Status is null || receipt.Response is null) throw new DataStoreUnavailableException();
        return receipt.Status is 200 or 201
            ? new(200, JsonSerializer.Deserialize<CustomerDto>(receipt.Response, JsonOptions), null, true)
            : new(receipt.Status.Value, null, JsonSerializer.Deserialize<ApiProblemDto>(receipt.Response, JsonOptions), true);
    }

    private static CustomerCommandResult Reject(int status, string code, string detail, string traceId,
        IReadOnlyList<FieldErrorDto>? errors = null, int? currentVersion = null) =>
        new(status, null, ApiProblemDto.Create(status, code, detail, traceId, errors, currentVersion), false);

    private static CustomerDto Map(CustomerRow row) => new(row.Id, row.CustomerType, row.FullName,
        row.CompanyName, row.Cpf, row.Cnpj, row.Phone, row.Email,
        row.PostalCode is null ? null : new(row.PostalCode, row.Street!, row.Number!, row.Complement,
            row.District!, row.City!, row.State!, row.Reference), row.Version,
        DateTime.SpecifyKind(row.CreatedAtUtc, DateTimeKind.Utc),
        DateTime.SpecifyKind(row.UpdatedAtUtc, DateTimeKind.Utc));

    private sealed class CustomerRow
    {
        public Guid Id { get; set; }
        public string CustomerType { get; set; } = "";
        public string FullName { get; set; } = "";
        public string? CompanyName { get; set; }
        public string? Cpf { get; set; }
        public string? Cnpj { get; set; }
        public string Phone { get; set; } = "";
        public string? Email { get; set; }
        public int Version { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
        public string? PostalCode { get; set; }
        public string? Street { get; set; }
        public string? Number { get; set; }
        public string? Complement { get; set; }
        public string? District { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
        public string? Reference { get; set; }
    }

    private sealed class ReceiptRow
    {
        public string Operation { get; set; } = "";
        public byte[] Hash { get; set; } = [];
        public int? Status { get; set; }
        public string? Response { get; set; }
    }
}
