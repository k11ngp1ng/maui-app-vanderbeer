using VanderBeer.Application.Customers;
using VanderBeer.Contracts.Customers;

namespace VanderBeer.UnitTests;

public sealed class CustomerValidationTests
{
    private static CustomerWriteRequest Individual() => new(Guid.NewGuid(), "Individual", "Cliente Exemplo",
        null, "12345678909", null, "11999999999", null, null);

    [Fact]
    public void Valid_individual_and_business_are_accepted()
    {
        Assert.Empty(CustomerValidation.Validate(Individual()));
        var business = Individual() with { CustomerType = "Business", Cpf = null,
            Cnpj = "11222333000181", CompanyName = "Empresa Exemplo Ltda" };
        Assert.Empty(CustomerValidation.Validate(business));
        Assert.Empty(CustomerValidation.Validate(business with { Cnpj = "00000000E08G12" }));
    }

    [Fact]
    public void Invalid_documents_and_mixed_types_are_rejected()
    {
        Assert.Contains(CustomerValidation.Validate(Individual() with { Cpf = "11111111111" }),
            error => error.Field == "cpf");
        Assert.Contains(CustomerValidation.Validate(Individual() with { Cnpj = "11222333000181" }),
            error => error.Field == "customerType");
        Assert.Contains(CustomerValidation.Validate(Individual() with { CustomerType = "Business", Cpf = null,
            Cnpj = "11222333000180", CompanyName = "Empresa Exemplo Ltda" }),
            error => error.Field == "cnpj");
    }

    [Fact]
    public void Api_rejects_document_masks_and_normalizes_name()
    {
        var value = CustomerValidation.Normalize(Individual() with { Cpf = "123.456.789-09",
            FullName = "  Cliente Exemplo  " });
        Assert.Contains(CustomerValidation.Validate(value), error => error.Field == "cpf");
        Assert.Equal("Cliente Exemplo", value.FullName);
    }
}
