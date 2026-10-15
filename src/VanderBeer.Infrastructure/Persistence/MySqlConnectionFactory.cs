using MySqlConnector;
using VanderBeer.Application.Operations;

namespace VanderBeer.Infrastructure.Persistence;

public sealed class MySqlConnectionFactory(string? connectionString)
{
    public async Task<MySqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new DataStoreUnavailableException();
        }

        var connection = new MySqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch (MySqlException error)
        {
            await connection.DisposeAsync();
            throw new DataStoreUnavailableException(error);
        }
    }
}
