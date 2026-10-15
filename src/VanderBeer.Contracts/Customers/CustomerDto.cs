namespace VanderBeer.Contracts.Customers;

public sealed record AddressDto(string PostalCode, string Street, string Number, string? Complement,
    string District, string City, string State, string? Reference);

public sealed record CustomerWriteRequest(Guid ClientRequestId, string CustomerType, string FullName,
    string? CompanyName, string? Cpf, string? Cnpj, string Phone, string? Email, AddressDto? Address);

public sealed record CustomerUpdateRequest(Guid ClientRequestId, int ExpectedVersion, string CustomerType,
    string FullName, string? CompanyName, string? Cpf, string? Cnpj, string Phone, string? Email,
    AddressDto? Address);

public sealed record CustomerDto(Guid Id, string CustomerType, string FullName, string? CompanyName,
    string? Cpf, string? Cnpj, string Phone, string? Email, AddressDto? Address, int Version,
    DateTime CreatedAtUtc, DateTime UpdatedAtUtc);
