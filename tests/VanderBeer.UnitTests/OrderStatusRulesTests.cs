using VanderBeer.Application.Orders;
using VanderBeer.Contracts.Orders;

namespace VanderBeer.UnitTests;

public sealed class OrderStatusRulesTests
{
    [Theory]
    [InlineData("Received", "Confirmed", "Delivery", true)]
    [InlineData("Received", "Rejected", "Pickup", true)]
    [InlineData("Confirmed", "Preparing", "Delivery", true)]
    [InlineData("Preparing", "OutForDelivery", "Delivery", true)]
    [InlineData("Preparing", "OutForDelivery", "Pickup", false)]
    [InlineData("Preparing", "ReadyForPickup", "Pickup", true)]
    [InlineData("Preparing", "ReadyForPickup", "Delivery", false)]
    [InlineData("OutForDelivery", "Delivered", "Delivery", true)]
    [InlineData("ReadyForPickup", "Collected", "Pickup", true)]
    [InlineData("Delivered", "Confirmed", "Delivery", false)]
    [InlineData("Cancelled", "Preparing", "Pickup", false)]
    public void Transitions_respect_state_and_fulfillment(string current, string next,
        string fulfillment, bool expected) =>
        Assert.Equal(expected, OrderStatusRules.CanMove(current, next, fulfillment));

    [Fact]
    public void Rejection_requires_reason_and_other_actions_forbid_it()
    {
        var request = new UpdateOrderStatusRequest(Guid.NewGuid(), 1, "Rejected", "  ");
        Assert.NotEmpty(OrderStatusRules.ValidateReason(OrderStatusRules.Normalize(request)));
        Assert.Empty(OrderStatusRules.ValidateReason(request with { Reason = "  Sem estoque  " }));
        Assert.NotEmpty(OrderStatusRules.ValidateReason(request with
        { Status = "Confirmed", Reason = "Motivo indevido" }));
    }
}
