using VanderBeer.Contracts.Catalog;
using VanderBeer.Contracts.Operations;

namespace VanderBeer.Application.Catalog;

public sealed record ProductQuery(int Page, int PageSize, string? Search, Guid? CategoryId, bool? IsActive);
public sealed record ProductCommandResult(int Status, ProductDto? Product, ApiProblemDto? Problem, bool Replay);

public interface IProductCatalog
{
    Task<ProductPageDto> ListAsync(ProductQuery query, bool company, CancellationToken cancellationToken);
    Task<ProductDto?> GetAsync(Guid id, bool company, CancellationToken cancellationToken);
    Task<IReadOnlyList<PaymentMethodDto>> GetPaymentMethodsAsync(CancellationToken cancellationToken);
    Task<ProductCommandResult> CreateAsync(ProductWriteRequest request, string traceId, CancellationToken cancellationToken);
    Task<ProductCommandResult> UpdateAsync(Guid id, ProductUpdateRequest request, string traceId, CancellationToken cancellationToken);
}
