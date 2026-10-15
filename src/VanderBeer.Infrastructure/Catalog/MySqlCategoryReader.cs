using Dapper;
using MySqlConnector;
using VanderBeer.Application.Catalog;
using VanderBeer.Application.Operations;
using VanderBeer.Contracts.Catalog;
using VanderBeer.Infrastructure.Persistence;

namespace VanderBeer.Infrastructure.Catalog;

public sealed class MySqlCategoryReader(MySqlConnectionFactory connectionFactory) : ICategoryReader
{
    public async Task<IReadOnlyList<CategoryDto>> GetActiveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await connectionFactory.OpenAsync(cancellationToken);
            var rows = await connection.QueryAsync<CategoryRow>(new CommandDefinition(
                "SELECT id AS Id, name AS Name FROM categories WHERE is_active = 1 ORDER BY name, id",
                cancellationToken: cancellationToken));
            return rows.Select(row => new CategoryDto(row.Id, row.Name)).ToArray();
        }
        catch (MySqlException error)
        {
            throw new DataStoreUnavailableException(error);
        }
    }

    private sealed class CategoryRow
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }
}
