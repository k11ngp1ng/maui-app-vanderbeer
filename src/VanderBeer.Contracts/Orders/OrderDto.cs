using VanderBeer.Contracts.Customers;

namespace VanderBeer.Contracts.Orders;

public sealed record OrderLineRequest(Guid ProductId, int Quantity, decimal ExpectedUnitPrice);

public sealed record CreateOrderRequest(Guid ClientRequestId, Guid CustomerId, int ExpectedCustomerVersion,
    string FulfillmentType, DateOnly RequestedDate, Guid PaymentMethodId, decimal? CashTendered,
    AddressDto? DeliveryAddress, string? Notes, IReadOnlyList<OrderLineRequest> Items);

public sealed record CustomerSnapshotDto(string Name, string Phone);
public sealed record PaymentSnapshotDto(Guid Id, string Code, string Name);
public sealed record OrderItemDto(Guid ProductId, string ProductName, string UnitLabel, int Quantity,
    decimal UnitPrice, decimal Subtotal);
public sealed record StatusEventDto(int Sequence, string? FromStatus, string ToStatus,
    DateTime OccurredAtUtc, string? Reason);

public sealed record OrderDto(Guid Id, string Protocol, DateTime CreatedAtUtc, DateOnly RequestedDate,
    string FulfillmentType, string Status, decimal Total, int Version, Guid ClientRequestId,
    Guid CustomerId, CustomerSnapshotDto Customer, PaymentSnapshotDto PaymentMethod,
    AddressDto? DeliveryAddress, string? Notes, decimal? CashTendered, decimal ChangeDue,
    decimal DeliveryFee, DateTime UpdatedAtUtc, IReadOnlyList<OrderItemDto> Items,
    IReadOnlyList<StatusEventDto> StatusHistory);

public sealed record PriceChangeDto(Guid ProductId, decimal ExpectedUnitPrice, decimal CurrentUnitPrice);

public sealed record OrderSummaryDto(Guid Id, string Protocol, DateTime CreatedAtUtc,
    DateOnly RequestedDate, string FulfillmentType, string Status, decimal Total, int Version);

public sealed record OrderPageDto(IReadOnlyList<OrderSummaryDto> Items, int Page, int PageSize, long Total);
