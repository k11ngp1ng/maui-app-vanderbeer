using System.Security.Cryptography;
using MySqlConnector;

if (args.Length != 1 || args[0] is not ("migrate" or "seed"))
{
    Console.Error.WriteLine("Uso: dotnet run --project tools/VanderBeer.DbMigrator -- migrate|seed");
    return 2;
}

var connectionString = Environment.GetEnvironmentVariable("VANDERBEER_DB_CONNECTION");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("Defina VANDERBEER_DB_CONNECTION para um banco de desenvolvimento existente.");
    return 2;
}

var settings = new MySqlConnectionStringBuilder(connectionString);
if (string.IsNullOrWhiteSpace(settings.Database))
{
    Console.Error.WriteLine("A connection string precisa informar Database.");
    return 2;
}

var databaseRoot = Path.Combine(Directory.GetCurrentDirectory(), "database");
if (!Directory.Exists(databaseRoot))
{
    Console.Error.WriteLine("Execute o comando da raiz do repositório.");
    return 2;
}

try
{
    await using var connection = new MySqlConnection(connectionString);
    await connection.OpenAsync();
    await using var lockCommand = new MySqlCommand("SELECT GET_LOCK('vanderbeer_migrations', 30)", connection);
    if (Convert.ToInt32(await lockCommand.ExecuteScalarAsync()) != 1)
    {
        throw new InvalidOperationException("Não foi possível obter o bloqueio de migrations.");
    }

    try
    {
        if (args[0] == "migrate")
        {
            await ApplyMigrationsAsync(connection, Path.Combine(databaseRoot, "migrations"));
        }
        else
        {
            await ApplySeedAsync(connection, Path.Combine(databaseRoot, "seeds", "demo.sql"));
        }
    }
    finally
    {
        await using var releaseCommand = new MySqlCommand("SELECT RELEASE_LOCK('vanderbeer_migrations')", connection);
        await releaseCommand.ExecuteScalarAsync();
    }

    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine($"Falha no banco de desenvolvimento: {error.Message}");
    return 1;
}

static async Task ApplyMigrationsAsync(MySqlConnection connection, string migrationDirectory)
{
    var files = Directory.GetFiles(migrationDirectory, "*.sql").OrderBy(Path.GetFileName, StringComparer.Ordinal).ToArray();
    if (files.Length == 0)
    {
        throw new InvalidOperationException("Nenhuma migration SQL foi encontrada.");
    }

    foreach (var file in files)
    {
        var version = Path.GetFileNameWithoutExtension(file);
        var contents = await File.ReadAllBytesAsync(file);
        var checksum = SHA256.HashData(contents);
        if (await TableExistsAsync(connection, "schema_migrations"))
        {
            await using var lookup = new MySqlCommand("SELECT checksum_sha256 FROM schema_migrations WHERE version = @version", connection);
            lookup.Parameters.AddWithValue("@version", version);
            if (await lookup.ExecuteScalarAsync() is byte[] savedChecksum)
            {
                if (!savedChecksum.SequenceEqual(checksum))
                {
                    throw new InvalidOperationException($"A migration {version} foi alterada após a aplicação.");
                }

                Console.WriteLine($"Migration {version}: já aplicada, checksum confirmado.");
                continue;
            }
        }

        if (version == "001_initial_schema" && await CountTablesAsync(connection) != 0)
        {
            throw new InvalidOperationException("O esquema não está vazio e a migration inicial não consta como aplicada. Corrija a instalação parcial sem apagar dados automaticamente.");
        }

        var statements = SplitStatements(System.Text.Encoding.UTF8.GetString(contents));
        for (var index = 0; index < statements.Length; index++)
        {
            try
            {
                await using var command = new MySqlCommand(statements[index], connection);
                await command.ExecuteNonQueryAsync();
            }
            catch (MySqlException error)
            {
                throw new InvalidOperationException($"Migration {version}, instrução {index + 1}: {error.Message}. DDL MySQL pode ter sido aplicado parcialmente; inspecione o banco antes de tentar novamente.", error);
            }
        }

        await using var record = new MySqlCommand("INSERT INTO schema_migrations (version, checksum_sha256, applied_at_utc) VALUES (@version, @checksum, UTC_TIMESTAMP(6))", connection);
        record.Parameters.AddWithValue("@version", version);
        record.Parameters.AddWithValue("@checksum", checksum);
        await record.ExecuteNonQueryAsync();
        Console.WriteLine($"Migration {version}: aplicada ({statements.Length} instruções).");
    }
}

static async Task ApplySeedAsync(MySqlConnection connection, string seedPath)
{
    if (!await TableExistsAsync(connection, "schema_migrations"))
    {
        throw new InvalidOperationException("Aplique as migrations antes do seed.");
    }

    var statements = SplitStatements(await File.ReadAllTextAsync(seedPath));
    await using var transaction = await connection.BeginTransactionAsync();
    try
    {
        foreach (var statement in statements)
        {
            await using var command = new MySqlCommand(statement, connection, transaction);
            await command.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
        Console.WriteLine($"Seed fictício aplicado ({statements.Length} instruções). Registros existentes não foram sobrescritos.");
    }
    catch
    {
        await transaction.RollbackAsync();
        throw;
    }
}

static async Task<bool> TableExistsAsync(MySqlConnection connection, string tableName)
{
    await using var command = new MySqlCommand("SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name = @table", connection);
    command.Parameters.AddWithValue("@table", tableName);
    return Convert.ToInt32(await command.ExecuteScalarAsync()) == 1;
}

static async Task<int> CountTablesAsync(MySqlConnection connection)
{
    await using var command = new MySqlCommand("SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE()", connection);
    return Convert.ToInt32(await command.ExecuteScalarAsync());
}

static string[] SplitStatements(string sql) => sql.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
