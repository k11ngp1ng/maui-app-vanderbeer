using System.Globalization;

namespace VanderBeer.App.Features.Shared;

public static class MoneyInput
{
    private static readonly CultureInfo Brazilian = CultureInfo.GetCultureInfo("pt-BR");

    public static bool TryParse(string? input, out decimal value)
    {
        value = 0;
        var text = input?.Trim();
        if (string.IsNullOrEmpty(text)) return false;

        // Android's numeric keyboard may insert a dot even when the UI uses Brazilian reais.
        var culture = text.Contains('.') && !text.Contains(',')
            ? CultureInfo.InvariantCulture : Brazilian;
        var decimalSeparator = culture == CultureInfo.InvariantCulture ? '.' : ',';
        var separatorAt = text.LastIndexOf(decimalSeparator);
        if (separatorAt >= 0 && text.Length - separatorAt - 1 is < 1 or > 2) return false;
        var style = culture == CultureInfo.InvariantCulture
            ? NumberStyles.AllowDecimalPoint : NumberStyles.Number;
        return decimal.TryParse(text, style, culture, out value) && value >= 0 &&
            value * 100 == decimal.Truncate(value * 100);
    }
}
