using VanderBeer.App.Features.Shared;

namespace VanderBeer.UnitTests;

public sealed class OrderTimeFormatTests
{
    [Fact]
    public void UtcInstantUsesStoreTimeOnEveryDevice()
    {
        var instant = new DateTimeOffset(2026, 10, 14, 3, 15, 0, TimeSpan.Zero);

        Assert.Equal("14/10/2026 00:15", OrderTimeFormat.InStoreZone(instant));
        Assert.Equal("14/10/2026 00:15", OrderTimeFormat.InStoreZone(instant.UtcDateTime));
    }
}
