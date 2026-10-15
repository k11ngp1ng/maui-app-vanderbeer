using VanderBeer.App.Features.Shared;

namespace VanderBeer.UnitTests;

public sealed class MoneyInputTests
{
    [Theory]
    [InlineData("31.90", 31.90)]
    [InlineData("31,90", 31.90)]
    [InlineData("1.234,56", 1234.56)]
    [InlineData("3190", 3190)]
    public void Accepts_brazilian_and_android_decimal_separator(string input, double expected)
    {
        Assert.True(MoneyInput.TryParse(input, out var value));
        Assert.Equal((decimal)expected, value);
    }

    [Theory]
    [InlineData("1.234")]
    [InlineData("31.900")]
    [InlineData("31,900")]
    [InlineData("-1,00")]
    [InlineData("")]
    public void Rejects_ambiguous_or_invalid_money(string input)
        => Assert.False(MoneyInput.TryParse(input, out _));
}
