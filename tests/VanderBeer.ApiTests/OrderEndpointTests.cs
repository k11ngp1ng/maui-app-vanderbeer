using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace VanderBeer.ApiTests;

public sealed class OrderEndpointTests
{
    [Fact]
    public async Task Company_queue_rejects_invalid_filters_before_database_access()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(
            "/api/v1/admin/orders?page=0&status=Unknown&fromDate=2026-10-12&toDate=2026-10-11");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("ValidationFailed", json.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Company_transition_rejects_invalid_structure_before_database_access()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        using var response = await client.PatchAsJsonAsync(
            $"/api/v1/admin/orders/{Guid.NewGuid():D}/status",
            new { clientRequestId = Guid.Empty, expectedVersion = 0,
                status = "Unknown", reason = (string?)null });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Customer_order_list_rejects_invalid_pagination_before_database_access()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync($"/api/v1/customers/{Guid.NewGuid():D}/orders?page=0&pageSize=101");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("ValidationFailed", json.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Unknown_order_field_is_rejected_before_touching_the_database()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/v1/orders", new
        {
            clientRequestId = Guid.NewGuid(), customerId = Guid.NewGuid(), expectedCustomerVersion = 1,
            fulfillmentType = "Pickup", requestedDate = DateOnly.FromDateTime(DateTime.UtcNow),
            paymentMethodId = Guid.NewGuid(), cashTendered = (decimal?)null,
            deliveryAddress = (object?)null, notes = (string?)null,
            items = new[] { new { productId = Guid.NewGuid(), quantity = 1, expectedUnitPrice = 10m } },
            unexpected = "rejeitar"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("ValidationFailed", json.RootElement.GetProperty("code").GetString());
    }
}
