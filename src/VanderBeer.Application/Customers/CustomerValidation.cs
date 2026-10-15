using System.Text;
using System.Text.RegularExpressions;
using VanderBeer.Contracts.Customers;
using VanderBeer.Contracts.Operations;

namespace VanderBeer.Application.Customers;

public static class CustomerValidation
{
    public static CustomerWriteRequest Normalize(CustomerWriteRequest request) => request with
    {
        FullName = Clean(request.FullName) ?? string.Empty,
        CompanyName = Clean(request.CompanyName),
        Cpf = Clean(request.Cpf),
        Cnpj = Clean(request.Cnpj),
        Phone = Clean(request.Phone) ?? string.Empty,
        Email = Clean(request.Email),
        Address = request.Address is null ? null : request.Address with
        {
            PostalCode = Clean(request.Address.PostalCode) ?? string.Empty,
            Street = Clean(request.Address.Street) ?? string.Empty,
            Number = Clean(request.Address.Number) ?? string.Empty,
            Complement = Clean(request.Address.Complement),
            District = Clean(request.Address.District) ?? string.Empty,
            City = Clean(request.Address.City) ?? string.Empty,
            State = Clean(request.Address.State)?.ToUpperInvariant() ?? string.Empty,
            Reference = Clean(request.Address.Reference)
        }
    };

    public static IReadOnlyList<FieldErrorDto> Validate(CustomerWriteRequest request, int? expectedVersion = null)
    {
        List<FieldErrorDto> errors = [];
        if (request.ClientRequestId == Guid.Empty) errors.Add(new("clientRequestId", "Informe uma chave válida."));
        if (expectedVersion is <= 0) errors.Add(new("expectedVersion", "Informe uma versão positiva."));
        CheckText(request.FullName, "fullName", 3, 120, errors);
        if (request.CustomerType == "Individual")
        {
            if (!ValidCpf(request.Cpf)) errors.Add(new("cpf", "Informe um CPF válido."));
            if (request.Cnpj is not null || request.CompanyName is not null)
                errors.Add(new("customerType", "Pessoa física não informa CNPJ nem razão social."));
        }
        else if (request.CustomerType == "Business")
        {
            if (!ValidCnpj(request.Cnpj)) errors.Add(new("cnpj", "Informe um CNPJ válido."));
            CheckText(request.CompanyName, "companyName", 3, 120, errors);
            if (request.Cpf is not null) errors.Add(new("cpf", "Pessoa jurídica não informa CPF neste cadastro."));
        }
        else errors.Add(new("customerType", "Escolha pessoa física ou jurídica."));
        if (!Regex.IsMatch(request.Phone ?? "", "^[0-9]{10,11}$", RegexOptions.CultureInvariant))
            errors.Add(new("phone", "Informe telefone com DDD e 10 ou 11 dígitos."));
        if (request.Email is not null && (request.Email.Length > 254 ||
            !System.Net.Mail.MailAddress.TryCreate(request.Email, out var email) || email.Address != request.Email))
            errors.Add(new("email", "Informe um e-mail válido."));
        if (request.Address is { } address)
        {
            if (!Regex.IsMatch(address.PostalCode, "^[0-9]{8}$")) errors.Add(new("address.postalCode", "Informe 8 dígitos."));
            CheckText(address.Street, "address.street", 2, 120, errors);
            CheckText(address.Number, "address.number", 1, 20, errors);
            CheckText(address.District, "address.district", 2, 80, errors);
            CheckText(address.City, "address.city", 2, 80, errors);
            if (!States.Contains(address.State)) errors.Add(new("address.state", "Informe uma UF válida."));
            if (address.Complement is not null) CheckText(address.Complement, "address.complement", 1, 80, errors);
            if (address.Reference is not null) CheckText(address.Reference, "address.reference", 1, 160, errors);
        }
        return errors;
    }

    private static string? Clean(string? value) => value?.Trim().Normalize(NormalizationForm.FormC);
    private static void CheckText(string? text, string field, int min, int max, List<FieldErrorDto> errors)
    {
        var length = text?.EnumerateRunes().Count() ?? 0;
        if (length < min || length > max) errors.Add(new(field, $"Informe de {min} a {max} caracteres."));
    }

    private static bool ValidCpf(string? value)
    {
        if (value is null || !Regex.IsMatch(value, "^[0-9]{11}$") || value.Distinct().Count() == 1) return false;
        var sum = Enumerable.Range(0, 9).Sum(i => (value[i] - '0') * (10 - i));
        var first = (sum * 10) % 11 % 10;
        sum = Enumerable.Range(0, 10).Sum(i => (value[i] - '0') * (11 - i));
        return first == value[9] - '0' && (sum * 10) % 11 % 10 == value[10] - '0';
    }

    private static bool ValidCnpj(string? value)
    {
        if (value is null || !Regex.IsMatch(value, "^[A-Z0-9]{12}[0-9]{2}$", RegexOptions.CultureInvariant) ||
            value.Distinct().Count() == 1) return false;
        int Digit(int count, int[] weights)
        {
            var sum = Enumerable.Range(0, count).Sum(i => (value[i] - '0') * weights[i]);
            var remainder = sum % 11;
            return remainder < 2 ? 0 : 11 - remainder;
        }
        return Digit(12, [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]) == value[12] - '0' &&
            Digit(13, [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]) == value[13] - '0';
    }

    private static readonly HashSet<string> States =
        ["AC", "AL", "AP", "AM", "BA", "CE", "DF", "ES", "GO", "MA", "MT", "MS", "MG",
         "PA", "PB", "PR", "PE", "PI", "RJ", "RN", "RS", "RO", "RR", "SC", "SP", "SE", "TO"];
}
