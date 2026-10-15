using VanderBeer.App.Features.Shared;

namespace VanderBeer.UnitTests;

public sealed class OrderProgressTests
{
    [Theory]
    [InlineData("Rejected")]
    [InlineData("Cancelled")]
    public void InterruptedOrderDoesNotPromiseCompletion(string status)
    {
        var progress = OrderProgress.Describe(status, true);
        Assert.Single(progress);
        Assert.DoesNotContain(progress, x => x.Contains("Entregue") || x.Contains("Em preparo"));
        Assert.Contains("motivo", progress[0]);
    }

    [Fact]
    public void PickupUsesCollectionAndNeverDelivery()
    {
        var progress = OrderProgress.Describe("ReadyForPickup", false);
        Assert.Contains(progress, x => x.StartsWith("●") && x.Contains("Pronto para retirada"));
        Assert.Contains(progress, x => x.StartsWith("○") && x.Contains("Retirado"));
        Assert.DoesNotContain(progress, x => x.Contains("entrega", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DeliveryHighlightsOneCurrentStage()
    {
        var progress = OrderProgress.Describe("OutForDelivery", true);
        Assert.Single(progress, x => x.StartsWith("●"));
        Assert.Contains(progress, x => x.StartsWith("●") && x.Contains("Saiu para entrega"));
        Assert.Contains(progress, x => x.StartsWith("○") && x.Contains("Entregue"));
    }

    [Fact]
    public void PendingTransmissionDoesNotShowCommercialProgress()
    {
        var progress = OrderProgress.Describe(null, true);
        Assert.Single(progress);
        Assert.Contains("Aguardando recebimento", progress[0]);
        Assert.DoesNotContain(progress, x => x.StartsWith("✓") || x.StartsWith("●"));
    }

    [Fact]
    public void UnknownStatusDoesNotInventAStage()
    {
        Assert.Equal(["Andamento indisponível · Atualize o pedido"], OrderProgress.Describe("FutureStatus", true));
    }
}
