using VanderBeer.Contracts.Catalog;

namespace VanderBeer.Application.Catalog;

public interface ICategoryReader
{
    Task<IReadOnlyList<CategoryDto>> GetActiveAsync(CancellationToken cancellationToken);
}
