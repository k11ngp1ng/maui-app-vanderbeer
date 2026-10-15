using VanderBeer.Application.Orders;
using VanderBeer.Contracts.Orders;

namespace VanderBeer.UnitTests;

public sealed class OrderValidationTests
{
    [Fact]
    public void Store_date_uses_sao_paulo_and_accepts_both_window_edges()
    {
        var today = OrderValidation.TodayInStore(new DateTime(2026, 10, 8, 0, 35, 0, DateTimeKind.Utc));
        Assert.Equal(new DateOnly(2026, 10, 7), today);
        Assert.Empty(OrderValidation.ValidateBusiness(Request(today), today));
        Assert.Empty(OrderValidation.ValidateBusiness(Request(today.AddDays(30)), today));
        Assert.Contains(OrderValidation.ValidateBusiness(Request(today.AddDays(31)), today),
            error => error.Field == "requestedDate");
    }

    [Theory]
    [InlineData(2, 59, 59, 13)]
    [InlineData(3, 0, 0, 14)]
    public void Store_date_changes_at_sao_paulo_midnight(
        int hour, int minute, int second, int expectedDay)
    {
        var utc = new DateTime(2026, 10, 14, hour, minute, second, DateTimeKind.Utc);
        var today = OrderValidation.TodayInStore(utc);

        Assert.Equal(new DateOnly(2026, 10, expectedDay), today);
        Assert.Empty(OrderValidation.ValidateBusiness(Request(today), today));
        Assert.Contains(OrderValidation.ValidateBusiness(Request(today.AddDays(-1)), today),
            error => error.Field == "requestedDate");
    }

    [Fact]
    public void Duplicate_item_invalid_quantity_and_fractional_price_are_rejected_before_persistence()
    {
        var request = Request(new DateOnly(2026, 10, 7));
        var line = request.Items[0];
        request = request with { Items = [line with { Quantity = 0, ExpectedUnitPrice = 10.001m }, line] };
        var structure = OrderValidation.ValidateStructure(request);
        Assert.Contains(structure, error => error.Field == "items[0].quantity");
        Assert.Contains(structure, error => error.Field == "items[0].expectedUnitPrice");
        Assert.Contains(OrderValidation.ValidateBusiness(request, request.RequestedDate),
            error => error.Field == "items[1].productId");
    }

    private static CreateOrderRequest Request(DateOnly date)
    {
        var line = new OrderLineRequest(Guid.NewGuid(), 2, 10m);
        return new(Guid.NewGuid(), Guid.NewGuid(), 1, "Pickup", date, Guid.NewGuid(),
            null, null, null, [line]);
    }
}
