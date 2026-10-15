namespace VanderBeer.Contracts.Orders;

public sealed record UpdateOrderStatusRequest(Guid ClientRequestId, int ExpectedVersion,
    string Status, string? Reason);
