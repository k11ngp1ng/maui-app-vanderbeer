using VanderBeer.Contracts.Operations;
using VanderBeer.Contracts.Orders;

namespace VanderBeer.Application.Orders;

public static class OrderStatusRules
{
    private static readonly HashSet<string> States =
        ["Received", "Confirmed", "Preparing", "OutForDelivery", "Delivered",
         "ReadyForPickup", "Collected", "Rejected", "Cancelled"];

    public static bool IsStatus(string status) => States.Contains(status);

    public static UpdateOrderStatusRequest Normalize(UpdateOrderStatusRequest request) =>
        request with { Reason = request.Reason?.Trim() };

    public static IReadOnlyList<FieldErrorDto> ValidateStructure(UpdateOrderStatusRequest request)
    {
        List<FieldErrorDto> errors = [];
        if (request.ClientRequestId == Guid.Empty) errors.Add(new("clientRequestId", "Informe uma chave válida."));
        if (request.ExpectedVersion < 1) errors.Add(new("expectedVersion", "Informe uma versão positiva."));
        if (request.Status is null || !States.Contains(request.Status))
            errors.Add(new("status", "Informe um estado válido."));
        if (request.Reason is { Length: > 300 }) errors.Add(new("reason", "Informe até 300 caracteres."));
        return errors;
    }

    public static bool CanMove(string current, string next, string fulfillment) => (current, next) switch
    {
        ("Received", "Confirmed" or "Rejected" or "Cancelled") => true,
        ("Confirmed", "Preparing" or "Cancelled") => true,
        ("Preparing", "Cancelled") => true,
        ("Preparing", "OutForDelivery") => fulfillment == "Delivery",
        ("Preparing", "ReadyForPickup") => fulfillment == "Pickup",
        ("OutForDelivery", "Delivered") => fulfillment == "Delivery",
        ("ReadyForPickup", "Collected") => fulfillment == "Pickup",
        _ => false
    };

    public static IReadOnlyList<FieldErrorDto> ValidateReason(UpdateOrderStatusRequest request)
    {
        var requires = request.Status is "Rejected" or "Cancelled";
        if (requires && (request.Reason is null || request.Reason.Length is < 3 or > 300))
            return [new("reason", "Informe um motivo de 3 a 300 caracteres.")];
        if (!requires && request.Reason is not null)
            return [new("reason", "Motivo permitido somente para rejeição ou cancelamento.")];
        return [];
    }
}
