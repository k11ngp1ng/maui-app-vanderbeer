using VanderBeer.Contracts.Customers;
using VanderBeer.Contracts.Operations;

namespace VanderBeer.Application.Customers;

public sealed record CustomerCommandResult(int Status, CustomerDto? Customer, ApiProblemDto? Problem, bool Replay);

public interface ICustomerRegistry
{
    Task<CustomerDto?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<CustomerCommandResult> CreateAsync(CustomerWriteRequest request, string traceId, CancellationToken cancellationToken);
    Task<CustomerCommandResult> UpdateAsync(Guid id, CustomerUpdateRequest request, string traceId,
        CancellationToken cancellationToken);
}
