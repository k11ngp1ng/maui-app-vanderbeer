using VanderBeer.App.Storage;

namespace VanderBeer.UnitTests;

public sealed class CompanyOrderCommandStoreTests
{
    [Fact]
    public async Task Lost_response_keeps_identical_command_after_reopen_and_blocks_replacement()
    {
        var path = Path.Combine(Path.GetTempPath(), "vanderbeer-company-" + Guid.NewGuid().ToString("N") + ".db");
        var id = Guid.NewGuid();
        const string body = "{\"clientRequestId\":\"00000000-0000-0000-0000-000000000001\",\"expectedVersion\":1,\"status\":\"Confirmed\",\"reason\":null}";
        try
        {
            var first = new CompanyOrderCommandStore(path);
            await first.StartAsync(id, "http://localhost:5080", body);
            await first.CloseAsync();

            var reopened = new CompanyOrderCommandStore(path);
            var pending = await reopened.PendingAsync(id);
            Assert.NotNull(pending);
            Assert.Equal(body, pending.RequestJson);
            Assert.Equal("http://localhost:5080", pending.BaseUrl);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                reopened.StartAsync(id, "http://localhost:5080", "other command"));
            Assert.Equal(body, (await reopened.PendingAsync(id))!.RequestJson);
            await reopened.CompleteAsync(id);
            Assert.Null(await reopened.PendingAsync(id));
            await reopened.CloseAsync();
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
