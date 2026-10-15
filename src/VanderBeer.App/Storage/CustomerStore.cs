using System.Text.Json;
using SQLite;
using VanderBeer.Contracts.Customers;

namespace VanderBeer.App.Storage;

public sealed class CustomerStore
{
    private readonly SQLiteAsyncConnection _db;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public CustomerStore() : this(LocalDatabase.Path) { }

    public CustomerStore(string databasePath) => _db = new(databasePath);

    public Task CloseAsync() => _db.CloseAsync();

    public async Task<CustomerIdentity?> ReadAsync()
    {
        await _db.CreateTableAsync<LocalCustomerIdentity>();
        var row = await _db.FindAsync<LocalCustomerIdentity>(1);
        return row is null ? null : new(row.CustomerId is null ? null : Guid.Parse(row.CustomerId),
            row.RequestJson is null || row.RequestKind == "Update" ? null :
                JsonSerializer.Deserialize<CustomerWriteRequest>(row.RequestJson, JsonOptions),
            row.CustomerJson is null ? null : JsonSerializer.Deserialize<CustomerDto>(row.CustomerJson, JsonOptions),
            row.DraftJson is null ? null : JsonSerializer.Deserialize<CustomerWriteRequest>(row.DraftJson, JsonOptions),
            row.LastErrorCode,
            row.RequestKind == "Update" && row.RequestJson is not null
                ? JsonSerializer.Deserialize<CustomerUpdateRequest>(row.RequestJson, JsonOptions) : null);
    }

    public async Task<CustomerWriteRequest> StageCreationAsync(CustomerWriteRequest request)
    {
        await _db.CreateTableAsync<LocalCustomerIdentity>();
        var current = await _db.FindAsync<LocalCustomerIdentity>(1);
        if (current?.CustomerId is not null)
            throw new InvalidOperationException("Este aparelho já possui um cliente cadastrado.");
        if (current?.RequestJson is not null)
            throw new InvalidOperationException("Há um cadastro pendente. Recupere o resultado antes de criar outro.");
        await _db.InsertOrReplaceAsync(new LocalCustomerIdentity
        {
            Id = 1,
            RequestJson = JsonSerializer.Serialize(request, JsonOptions),
            RequestKind = "Create",
            DraftJson = JsonSerializer.Serialize(request, JsonOptions),
            LastErrorCode = null,
            UpdatedAtUtc = DateTime.UtcNow.ToString("O")
        });
        return request;
    }

    public async Task StageUpdateAsync(CustomerUpdateRequest request)
    {
        await _db.CreateTableAsync<LocalCustomerIdentity>();
        var current = await _db.FindAsync<LocalCustomerIdentity>(1)
            ?? throw new InvalidOperationException("Nenhum cliente cadastrado neste aparelho.");
        if (current.CustomerId is null || current.CustomerJson is null)
            throw new InvalidOperationException("O cadastro ainda não foi confirmado pela API.");
        if (current.RequestJson is not null)
            throw new InvalidOperationException("Há uma alteração pendente. Recupere o resultado antes de editar.");
        current.RequestKind = "Update";
        current.RequestJson = JsonSerializer.Serialize(request, JsonOptions);
        current.DraftJson = JsonSerializer.Serialize(new CustomerWriteRequest(request.ClientRequestId,
            request.CustomerType, request.FullName, request.CompanyName, request.Cpf, request.Cnpj,
            request.Phone, request.Email, request.Address), JsonOptions);
        current.LastErrorCode = null;
        current.UpdatedAtUtc = DateTime.UtcNow.ToString("O");
        await _db.UpdateAsync(current);
    }

    public async Task ConfirmAsync(CustomerDto customer)
    {
        await _db.CreateTableAsync<LocalCustomerIdentity>();
        await _db.RunInTransactionAsync(connection =>
        {
            var current = connection.Find<LocalCustomerIdentity>(1)
                ?? throw new InvalidOperationException("Nenhum cadastro local pendente.");
            if (current.RequestJson is null)
                throw new InvalidOperationException("Nenhum cadastro local pendente.");
            if (current.CustomerId is not null && current.CustomerId != customer.Id.ToString("D"))
                throw new InvalidDataException("A resposta pertence a outro cadastro.");
            var saved = current.CustomerJson is null ? null :
                JsonSerializer.Deserialize<CustomerDto>(current.CustomerJson, JsonOptions);
            current.CustomerId = customer.Id.ToString("D");
            if (saved is null || saved.Version <= customer.Version)
                current.CustomerJson = JsonSerializer.Serialize(customer, JsonOptions);
            current.RequestJson = null;
            current.RequestKind = null;
            current.DraftJson = null;
            current.LastErrorCode = null;
            current.UpdatedAtUtc = DateTime.UtcNow.ToString("O");
            connection.Update(current);
        });
    }

    public async Task<CustomerDto> RefreshConfirmedAsync(CustomerDto customer)
    {
        await _db.CreateTableAsync<LocalCustomerIdentity>();
        CustomerDto? effective = null;
        await _db.RunInTransactionAsync(connection =>
        {
            var current = connection.Find<LocalCustomerIdentity>(1)
                ?? throw new InvalidOperationException("Nenhum cadastro local para revisar.");
            if (current.CustomerId != customer.Id.ToString("D") || current.CustomerJson is null)
                throw new InvalidDataException("A resposta pertence a outro cadastro.");
            if (current.RequestJson is not null || current.DraftJson is not null)
                throw new InvalidOperationException("Revise a alteração pendente do cadastro antes do pedido.");
            var saved = JsonSerializer.Deserialize<CustomerDto>(current.CustomerJson, JsonOptions)
                ?? throw new InvalidDataException("Cadastro local inválido.");
            if (customer.Version < saved.Version)
                throw new InvalidDataException("A resposta do cadastro é anterior à versão local.");
            effective = customer.Version == saved.Version ? saved : customer;
            if (customer.Version > saved.Version)
            {
                current.CustomerJson = JsonSerializer.Serialize(customer, JsonOptions);
                current.UpdatedAtUtc = DateTime.UtcNow.ToString("O");
                connection.Update(current);
            }
        });
        return effective!;
    }

    public async Task MarkRejectedAsync(string errorCode)
    {
        await _db.CreateTableAsync<LocalCustomerIdentity>();
        var current = await _db.FindAsync<LocalCustomerIdentity>(1)
            ?? throw new InvalidOperationException("Nenhum cadastro local pendente.");
        if (current.RequestJson is null) throw new InvalidOperationException("Nenhum cadastro local pendente.");
        current.RequestJson = null;
        current.RequestKind = null;
        current.LastErrorCode = errorCode;
        current.UpdatedAtUtc = DateTime.UtcNow.ToString("O");
        await _db.UpdateAsync(current);
    }

    public sealed class LocalCustomerIdentity
    {
        [PrimaryKey] public int Id { get; set; }
        public string? CustomerId { get; set; }
        public string? RequestJson { get; set; }
        public string? RequestKind { get; set; }
        public string? CustomerJson { get; set; }
        public string? DraftJson { get; set; }
        public string? LastErrorCode { get; set; }
        public string UpdatedAtUtc { get; set; } = "";
    }
}

public sealed record CustomerIdentity(Guid? CustomerId, CustomerWriteRequest? PendingRequest,
    CustomerDto? Customer, CustomerWriteRequest? Draft, string? LastErrorCode,
    CustomerUpdateRequest? PendingUpdate = null);
