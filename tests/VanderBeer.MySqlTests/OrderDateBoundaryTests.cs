using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MySqlConnector;
using VanderBeer.Infrastructure.Orders;

namespace VanderBeer.MySqlTests;

public sealed class OrderDateBoundaryTests
{
    [Fact]
    public async Task SaoPaulo_midnight_changes_accepted_date_and_preserves_replay()
    {
        var connectionString = Environment.GetEnvironmentVariable("VANDERBEER_TEST_DB_CONNECTION");
        Assert.False(string.IsNullOrWhiteSpace(connectionString),
            "Defina VANDERBEER_TEST_DB_CONNECTION para um banco MySQL isolado com migrations e seed.");
        var previousConnection = Environment.GetEnvironmentVariable("VANDERBEER_DB_CONNECTION");
        try
        {
            Environment.SetEnvironmentVariable("VANDERBEER_DB_CONNECTION", connectionString);
            await RunAsync(connectionString);
        }
        finally
        {
            Environment.SetEnvironmentVariable("VANDERBEER_DB_CONNECTION", previousConnection);
        }
    }

    private static async Task RunAsync(string connectionString)
    {
        var clock = new FixedOrderClock(new DateTime(2026, 10, 14, 2, 59, 59, DateTimeKind.Utc));
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IOrderClock>();
                services.AddSingleton<IOrderClock>(clock);
            }));
        using var client = factory.CreateClient();

        var customerResponse = await client.PostAsJsonAsync("/api/v1/customers", new
        {
            clientRequestId = Guid.NewGuid(), fullName = "Cliente de teste da virada",
            cpf = NewCpf(), phone = "11999999999", email = (string?)null,
            address = (object?)null, customerType = "Individual",
            companyName = (string?)null, cnpj = (string?)null
        });
        Assert.True(customerResponse.StatusCode == HttpStatusCode.Created,
            $"Cadastro retornou {(int)customerResponse.StatusCode}: {await customerResponse.Content.ReadAsStringAsync()}");
        using var customer = JsonDocument.Parse(await customerResponse.Content.ReadAsStringAsync());
        var customerId = customer.RootElement.GetProperty("id").GetGuid();
        var version = customer.RootElement.GetProperty("version").GetInt32();

        var firstKey = Guid.NewGuid();
        var first = Order(firstKey, customerId, version, "2026-10-13");
        using var acceptedBeforeMidnight = await client.PostAsJsonAsync("/api/v1/orders", first);
        Assert.Equal(HttpStatusCode.Created, acceptedBeforeMidnight.StatusCode);
        using var created = JsonDocument.Parse(await acceptedBeforeMidnight.Content.ReadAsStringAsync());
        var firstId = created.RootElement.GetProperty("id").GetGuid();
        Assert.Equal("2026-10-13", created.RootElement.GetProperty("requestedDate").GetString());
        Assert.Equal("2026-10-14T02:59:59Z", created.RootElement.GetProperty("createdAtUtc").GetString());

        clock.UtcNow = new DateTime(2026, 10, 14, 3, 0, 0, DateTimeKind.Utc);
        using var replay = await client.PostAsJsonAsync("/api/v1/orders", first);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal("true", replay.Headers.GetValues("Idempotent-Replay").Single());
        using var replayBody = JsonDocument.Parse(await replay.Content.ReadAsStringAsync());
        Assert.Equal(firstId, replayBody.RootElement.GetProperty("id").GetGuid());

        var rejectedKey = Guid.NewGuid();
        using var rejected = await client.PostAsJsonAsync("/api/v1/orders",
            Order(rejectedKey, customerId, version, "2026-10-13"));
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        using var problem = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync());
        Assert.Equal("ValidationFailed", problem.RootElement.GetProperty("code").GetString());

        var secondKey = Guid.NewGuid();
        using var acceptedAfterMidnight = await client.PostAsJsonAsync("/api/v1/orders",
            Order(secondKey, customerId, version, "2026-10-14"));
        Assert.Equal(HttpStatusCode.Created, acceptedAfterMidnight.StatusCode);
        using var secondOrder = JsonDocument.Parse(await acceptedAfterMidnight.Content.ReadAsStringAsync());
        Assert.Equal("2026-10-14T03:00:00Z", secondOrder.RootElement.GetProperty("createdAtUtc").GetString());

        await using var db = new MySqlConnection(connectionString);
        await db.OpenAsync();
        await using var command = db.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM orders WHERE creation_request_id IN (@first, @rejected, @second)";
        command.Parameters.AddWithValue("@first", firstKey.ToString());
        command.Parameters.AddWithValue("@rejected", rejectedKey.ToString());
        command.Parameters.AddWithValue("@second", secondKey.ToString());
        Assert.Equal(2L, Convert.ToInt64(await command.ExecuteScalarAsync()));
    }

    private static object Order(Guid key, Guid customerId, int version, string date) => new
    {
        clientRequestId = key, customerId, expectedCustomerVersion = version,
        fulfillmentType = "Pickup", requestedDate = date,
        paymentMethodId = Guid.Parse("00000000-0000-4000-8000-000000000006"),
        cashTendered = (decimal?)null, deliveryAddress = (object?)null,
        notes = (string?)null,
        items = new[] { new { productId = Guid.Parse("00000000-0000-4000-8000-000000000009"),
            quantity = 1, expectedUnitPrice = 29.90m } }
    };

    private static string NewCpf()
    {
        var digits = Enumerable.Range(0, 9).Select(_ => Random.Shared.Next(10)).ToList();
        if (digits.All(x => x == digits[0])) digits[0] = (digits[0] + 1) % 10;
        for (var length = 9; length <= 10; length++)
        {
            var sum = Enumerable.Range(0, length).Sum(i => digits[i] * (length + 1 - i));
            var check = (sum * 10) % 11;
            digits.Add(check == 10 ? 0 : check);
        }
        return string.Concat(digits);
    }

    private sealed class FixedOrderClock(DateTime utcNow) : IOrderClock
    {
        public DateTime UtcNow { get; set; } = utcNow;

        public Task<DateTime> UtcNowAsync(MySqlConnection connection, MySqlTransaction transaction,
            CancellationToken cancellationToken) => Task.FromResult(UtcNow);
    }
}
