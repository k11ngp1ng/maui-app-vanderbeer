using SQLite;

namespace VanderBeer.App.Storage;

public sealed class CompanyOrderCommandStore
{
    private readonly SQLiteAsyncConnection _db;

    public CompanyOrderCommandStore(string? path = null) => _db = new(path ?? LocalDatabase.Path);

    public Task CloseAsync() => _db.CloseAsync();

    public async Task<PendingCommand?> PendingAsync(Guid orderId)
    {
        await _db.CreateTableAsync<PendingCommand>();
        return await _db.FindAsync<PendingCommand>(orderId.ToString("D"));
    }

    public async Task<PendingCommand> StartAsync(Guid orderId, string baseUrl, string requestJson)
    {
        await _db.CreateTableAsync<PendingCommand>();
        var row = new PendingCommand { OrderId = orderId.ToString("D"), BaseUrl = baseUrl,
            RequestJson = requestJson, CreatedAtUtc = DateTime.UtcNow.ToString("O") };
        await _db.RunInTransactionAsync(connection =>
        {
            if (connection.Find<PendingCommand>(row.OrderId) is not null)
                throw new InvalidOperationException("Verifique o resultado anterior antes de alterar este pedido.");
            connection.Insert(row);
        });
        return row;
    }

    public async Task CompleteAsync(Guid orderId) =>
        await _db.DeleteAsync<PendingCommand>(orderId.ToString("D"));

    public sealed class PendingCommand
    {
        [PrimaryKey] public string OrderId { get; set; } = "";
        public string BaseUrl { get; set; } = "";
        public string RequestJson { get; set; } = "";
        public string CreatedAtUtc { get; set; } = "";
    }
}
