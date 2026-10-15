using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VanderBeer.Application.Catalog;
using VanderBeer.Contracts.Catalog;

namespace VanderBeer.ApiTests;

public sealed class ProductEndpointTests
{
    [Fact]
    public async Task Unknown_request_field_returns_contract_validation_problem()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/v1/admin/products", new
        {
            clientRequestId = Guid.NewGuid(), categoryId = Guid.NewGuid(), sku = "VB-TEST",
            name = "Produto teste", description = (string?)null, unitLabel = "Unidade",
            price = 10.00m, imageUrl = (string?)null, isAvailable = true, isActive = true,
            unexpected = "rejeitar"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("ValidationFailed", json.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Unexpected_failure_returns_sanitized_500_problem()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/v1/products");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.Equal("InternalError", json.RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain("TEST_SECRET_SENTINEL", body);
    }

    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IProductCatalog>();
                services.AddSingleton<IProductCatalog, ThrowingCatalog>();
            }));

    private sealed class ThrowingCatalog : IProductCatalog
    {
        public Task<ProductPageDto> ListAsync(ProductQuery query, bool company, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("TEST_SECRET_SENTINEL");
        public Task<ProductDto?> GetAsync(Guid id, bool company, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<IReadOnlyList<PaymentMethodDto>> GetPaymentMethodsAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<ProductCommandResult> CreateAsync(ProductWriteRequest request, string traceId, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<ProductCommandResult> UpdateAsync(Guid id, ProductUpdateRequest request, string traceId, CancellationToken cancellationToken) => throw new NotImplementedException();
    }
}
