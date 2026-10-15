using Dapper;
using MySqlConnector;

namespace VanderBeer.Infrastructure.Orders;

public interface IOrderClock
{
    Task<DateTime> UtcNowAsync(MySqlConnection connection, MySqlTransaction transaction,
        CancellationToken cancellationToken);
}

public sealed class MySqlOrderClock : IOrderClock
{
    public Task<DateTime> UtcNowAsync(MySqlConnection connection, MySqlTransaction transaction,
        CancellationToken cancellationToken) => connection.ExecuteScalarAsync<DateTime>(
            new CommandDefinition("SELECT UTC_TIMESTAMP(6)", transaction: transaction,
                cancellationToken: cancellationToken));
}
