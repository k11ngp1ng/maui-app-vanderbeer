using MySqlConnector;
using VanderBeer.Application.Operations;

namespace VanderBeer.Infrastructure.Persistence;

public sealed class MySqlDatabaseProbe(MySqlConnectionFactory connectionFactory) : IDatabaseProbe
{
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await connectionFactory.OpenAsync(cancellationToken);
            await using var command = new MySqlCommand("SELECT 1", connection);
            await command.ExecuteScalarAsync(cancellationToken);
            return true;
        }
        catch (DataStoreUnavailableException)
        {
            return false;
        }
        catch (MySqlException)
        {
            return false;
        }
    }
}
