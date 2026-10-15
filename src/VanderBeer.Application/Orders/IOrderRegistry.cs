using VanderBeer.Contracts.Operations;
using VanderBeer.Contracts.Orders;

namespace VanderBeer.Application.Orders;

public sealed record OrderCommandResult(int Status, OrderDto? Order, ApiProblemDto? Problem, bool Replay);

public interface IOrderRegistry
{
    Task<OrderDto?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<OrderPageDto?> ListCustomerAsync(Guid customerId, int page, int pageSize,
        CancellationToken cancellationToken);
    Task<OrderCommandResult> CreateAsync(CreateOrderRequest request, string traceId,
        CancellationToken cancellationToken);
    Task<OrderPageDto> ListCompanyAsync(int page, int pageSize, string? status,
        DateOnly? fromDate, DateOnly? toDate, CancellationToken cancellationToken);
    Task<OrderCommandResult> UpdateStatusAsync(Guid id, UpdateOrderStatusRequest request,
        string traceId, CancellationToken cancellationToken);
}
