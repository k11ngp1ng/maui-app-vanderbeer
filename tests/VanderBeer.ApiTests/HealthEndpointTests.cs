using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VanderBeer.Application.Operations;

namespace VanderBeer.ApiTests;

public sealed class HealthEndpointTests
{
    [Fact]
    public async Task Healthy_database_returns_contract_response()
    {
        using var factory = CreateFactory(true);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Healthy", json.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Unavailable_database_returns_503_problem_without_connection_details()
    {
        using var factory = CreateFactory(false);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.Equal(503, json.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("ServiceUnavailable", json.RootElement.GetProperty("code").GetString());
        Assert.True(json.RootElement.GetProperty("errors").GetArrayLength() == 0);
        Assert.DoesNotContain("MySql", body, StringComparison.OrdinalIgnoreCase);
    }

    private static WebApplicationFactory<Program> CreateFactory(bool available)
        => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IDatabaseProbe>();
                services.AddSingleton<IDatabaseProbe>(new StubDatabaseProbe(available));
            }));

    private sealed class StubDatabaseProbe(bool available) : IDatabaseProbe
    {
        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => Task.FromResult(available);
    }
}
