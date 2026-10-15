namespace VanderBeer.App.Features.Shared;

public static class OrderProgress
{
    public static IReadOnlyList<string> Describe(string? status, bool delivery)
    {
        if (status is null) return ["○ Aguardando recebimento pela empresa"];
        if (status is "Rejected" or "Cancelled")
            return [status == "Rejected" ? "Pedido rejeitado · Consulte o motivo abaixo" : "Pedido cancelado · Consulte o motivo abaixo"];
        var steps = delivery
            ? new[] { ("Received", "Recebido"), ("Confirmed", "Confirmado"), ("Preparing", "Em preparo"), ("OutForDelivery", "Saiu para entrega"), ("Delivered", "Entregue") }
            : new[] { ("Received", "Recebido"), ("Confirmed", "Confirmado"), ("Preparing", "Em preparo"), ("ReadyForPickup", "Pronto para retirada"), ("Collected", "Retirado") };
        var current = Array.FindIndex(steps, x => x.Item1 == status);
        if (current < 0) return ["Andamento indisponível · Atualize o pedido"];
        return steps.Select((step, index) => index < current ? $"✓ {step.Item2}" :
            index == current ? $"● {step.Item2} · Etapa atual" : $"○ {step.Item2}").ToArray();
    }
}
