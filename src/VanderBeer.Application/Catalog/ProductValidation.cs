using System.Text;
using System.Text.RegularExpressions;
using VanderBeer.Contracts.Catalog;
using VanderBeer.Contracts.Operations;

namespace VanderBeer.Application.Catalog;

public static class ProductValidation
{
    public static IReadOnlyList<FieldErrorDto> ValidateQuery(ProductQuery query)
    {
        List<FieldErrorDto> errors = [];
        if (query.Page < 1 || query.PageSize is < 1 or > 100 || (long)(query.Page - 1) * query.PageSize > int.MaxValue)
            errors.Add(new("page", "Informe uma página válida e até 100 itens."));
        if (query.Search is { Length: > 100 }) errors.Add(new("search", "A busca aceita até 100 caracteres."));
        if (query.CategoryId == Guid.Empty) errors.Add(new("categoryId", "Informe uma categoria válida."));
        return errors;
    }

    public static IReadOnlyList<FieldErrorDto> Validate(ProductWriteRequest request, int? expectedVersion = null)
    {
        List<FieldErrorDto> errors = [];
        if (request.ClientRequestId == Guid.Empty) errors.Add(new("clientRequestId", "Informe uma chave válida."));
        if (request.CategoryId == Guid.Empty) errors.Add(new("categoryId", "Informe uma categoria válida."));
        if (expectedVersion is <= 0) errors.Add(new("expectedVersion", "Informe uma versão positiva."));
        if (request.Sku is null || !Regex.IsMatch(request.Sku, "^[A-Z0-9][A-Z0-9_-]{0,39}$", RegexOptions.CultureInvariant))
            errors.Add(new("sku", "Use 1 a 40 letras maiúsculas, números, _ ou -."));
        CheckText(request.Name, "name", 2, 120, errors);
        CheckText(request.UnitLabel, "unitLabel", 1, 40, errors);
        if (request.Description is not null) CheckText(request.Description, "description", 1, 2000, errors);
        if (request.Price is < 0.01m or > 99999.99m || decimal.Round(request.Price, 2) != request.Price)
            errors.Add(new("price", "Informe um preço de R$ 0,01 a R$ 99.999,99 com centavos."));
        if (request.ImageUrl is not null && (request.ImageUrl.Length > 1024 ||
            !Uri.TryCreate(request.ImageUrl, UriKind.Absolute, out var url) ||
            url.Scheme is not ("http" or "https")))
            errors.Add(new("imageUrl", "Informe uma URL HTTP ou HTTPS válida."));
        return errors;
    }

    public static ProductWriteRequest Normalize(ProductWriteRequest request) => request with
    {
        Name = request.Name?.Trim().Normalize(NormalizationForm.FormC) ?? string.Empty,
        UnitLabel = request.UnitLabel?.Trim().Normalize(NormalizationForm.FormC) ?? string.Empty,
        Description = request.Description?.Trim().Normalize(NormalizationForm.FormC),
        ImageUrl = request.ImageUrl?.Trim()
    };

    private static void CheckText(string? value, string field, int min, int max, List<FieldErrorDto> errors)
    {
        var length = value?.Trim().Normalize(NormalizationForm.FormC).EnumerateRunes().Count() ?? 0;
        if (length < min || length > max) errors.Add(new(field, $"Informe entre {min} e {max} caracteres."));
    }
}
