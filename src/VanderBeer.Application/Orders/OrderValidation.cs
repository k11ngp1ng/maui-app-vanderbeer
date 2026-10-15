using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using VanderBeer.Contracts.Customers;
using VanderBeer.Contracts.Operations;
using VanderBeer.Contracts.Orders;

namespace VanderBeer.Application.Orders;

public static class OrderValidation
{
    private static readonly HashSet<string> States =
        ["AC", "AL", "AP", "AM", "BA", "CE", "DF", "ES", "GO", "MA", "MT", "MS", "MG",
         "PA", "PB", "PR", "PE", "PI", "RJ", "RN", "RS", "RO", "RR", "SC", "SP", "SE", "TO"];

    public static CreateOrderRequest Normalize(CreateOrderRequest value) => value with
    {
        Notes = Clean(value.Notes),
        DeliveryAddress = value.DeliveryAddress is null ? null : value.DeliveryAddress with
        {
            PostalCode = Clean(value.DeliveryAddress.PostalCode) ?? "",
            Street = Clean(value.DeliveryAddress.Street) ?? "",
            Number = Clean(value.DeliveryAddress.Number) ?? "",
            Complement = Clean(value.DeliveryAddress.Complement),
            District = Clean(value.DeliveryAddress.District) ?? "",
            City = Clean(value.DeliveryAddress.City) ?? "",
            State = Clean(value.DeliveryAddress.State)?.ToUpperInvariant() ?? "",
            Reference = Clean(value.DeliveryAddress.Reference)
        }
    };

    public static IReadOnlyList<FieldErrorDto> ValidateStructure(CreateOrderRequest value)
    {
        List<FieldErrorDto> errors = [];
        if (value.ClientRequestId == Guid.Empty) errors.Add(new("clientRequestId", "Informe uma chave válida."));
        if (value.CustomerId == Guid.Empty) errors.Add(new("customerId", "Informe um cliente válido."));
        if (value.PaymentMethodId == Guid.Empty) errors.Add(new("paymentMethodId", "Informe uma forma de pagamento válida."));
        if (value.ExpectedCustomerVersion < 1) errors.Add(new("expectedCustomerVersion", "Informe uma versão positiva."));
        if (value.FulfillmentType is not ("Delivery" or "Pickup"))
            errors.Add(new("fulfillmentType", "Escolha entrega ou retirada."));
        if (value.RequestedDate == default) errors.Add(new("requestedDate", "Informe uma data válida."));
        if (value.Notes is not null && RuneLength(value.Notes) is < 1 or > 500)
            errors.Add(new("notes", "Informe até 500 caracteres."));
        if (value.CashTendered is { } cash && (!Money(cash) || cash > 999999.99m))
            errors.Add(new("cashTendered", "Informe um valor válido em reais."));
        if (value.Items is null || value.Items.Count is < 1 or > 50)
            errors.Add(new("items", "Informe de 1 a 50 produtos."));
        if (value.Items is not null)
            for (var i = 0; i < value.Items.Count; i++)
            {
                var line = value.Items[i];
                if (line is null) { errors.Add(new($"items[{i}]", "Informe um item válido.")); continue; }
                if (line.ProductId == Guid.Empty) errors.Add(new($"items[{i}].productId", "Informe um produto válido."));
                if (line.Quantity is < 1 or > 999)
                    errors.Add(new($"items[{i}].quantity", "Informe quantidade de 1 a 999."));
                if (!Money(line.ExpectedUnitPrice) || line.ExpectedUnitPrice > 99999.99m)
                    errors.Add(new($"items[{i}].expectedUnitPrice", "Informe um preço válido."));
            }
        if (value.DeliveryAddress is { } address) ValidateAddress(address, errors);
        return errors;
    }

    public static IReadOnlyList<FieldErrorDto> ValidateBusiness(CreateOrderRequest value, DateOnly today)
    {
        List<FieldErrorDto> errors = [];
        if (value.FulfillmentType == "Delivery" && value.DeliveryAddress is null)
            errors.Add(new("deliveryAddress", "Informe o endereço de entrega."));
        if (value.FulfillmentType == "Pickup" && value.DeliveryAddress is not null)
            errors.Add(new("deliveryAddress", "Retirada não utiliza endereço de entrega."));
        if (value.RequestedDate < today || value.RequestedDate > today.AddDays(30))
            errors.Add(new("requestedDate", "Escolha uma data entre hoje e os próximos 30 dias."));
        if (value.Items is not null)
        {
            var seen = new HashSet<Guid>();
            for (var i = 0; i < value.Items.Count; i++)
                if (!seen.Add(value.Items[i].ProductId))
                    errors.Add(new($"items[{i}].productId", "Produto repetido. Combine as quantidades."));
        }
        return errors;
    }

    public static DateOnly TodayInStore(DateTime utcNow) => DateOnly.FromDateTime(
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc),
            TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo")));

    private static void ValidateAddress(AddressDto address, List<FieldErrorDto> errors)
    {
        if (!Regex.IsMatch(address.PostalCode ?? "", "^[0-9]{8}$"))
            errors.Add(new("deliveryAddress.postalCode", "Informe 8 dígitos."));
        CheckLength(address.Street, "deliveryAddress.street", 2, 120, errors);
        CheckLength(address.Number, "deliveryAddress.number", 1, 20, errors);
        CheckLength(address.District, "deliveryAddress.district", 2, 80, errors);
        CheckLength(address.City, "deliveryAddress.city", 2, 80, errors);
        if (address.Complement is not null) CheckLength(address.Complement, "deliveryAddress.complement", 1, 80, errors);
        if (address.Reference is not null) CheckLength(address.Reference, "deliveryAddress.reference", 1, 160, errors);
        if (!States.Contains(address.State ?? "")) errors.Add(new("deliveryAddress.state", "Informe uma UF válida."));
    }

    private static void CheckLength(string? text, string field, int min, int max, List<FieldErrorDto> errors)
    {
        if (RuneLength(text) < min || RuneLength(text) > max)
            errors.Add(new(field, $"Informe de {min} a {max} caracteres."));
    }
    private static int RuneLength(string? value) => value?.EnumerateRunes().Count() ?? 0;
    private static string? Clean(string? text) => text?.Trim().Normalize(NormalizationForm.FormC);
    private static bool Money(decimal value) => value >= 0.01m && value * 100 == decimal.Truncate(value * 100);
}
