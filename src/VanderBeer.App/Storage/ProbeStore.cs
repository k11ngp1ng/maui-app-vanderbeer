using SQLite;

namespace VanderBeer.App.Storage;

public sealed class ProbeStore
{
    private readonly SQLiteAsyncConnection _database;

    public ProbeStore()
    {
        _database = new SQLiteAsyncConnection(
            LocalDatabase.Path);
    }

    public async Task<int> RecordLaunchAsync()
    {
        await _database.CreateTableAsync<LaunchRecord>();
        var previous = await _database.FindAsync<LaunchRecord>(1);
        var launchCount = (previous?.LaunchCount ?? 0) + 1;
        await _database.InsertOrReplaceAsync(new LaunchRecord { Id = 1, LaunchCount = launchCount });
        var saved = await _database.FindAsync<LaunchRecord>(1);
        return saved?.LaunchCount ?? throw new InvalidOperationException("Não foi possível ler o registro local.");
    }

    public sealed class LaunchRecord
    {
        [PrimaryKey]
        public int Id { get; set; }

        public int LaunchCount { get; set; }
    }
}
