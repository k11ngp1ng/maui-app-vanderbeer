using VanderBeer.App.Storage;
using VanderBeer.Contracts.Customers;

namespace VanderBeer.UnitTests;

public sealed class CustomerStoreTests
{
    [Fact]
    public async Task Refresh_for_order_review_adopts_newer_server_profile_without_changing_identity()
    {
        var path = Path.Combine(Path.GetTempPath(), "vanderbeer-customer-" + Guid.NewGuid().ToString("N") + ".db");
        var store = new CustomerStore(path);
        try
        {
            var request = new CustomerWriteRequest(Guid.NewGuid(), "Individual", "Cliente Exemplo",
                null, "12345678909", null, "11999999999", null, null);
            await store.StageCreationAsync(request);
            var original = new CustomerDto(Guid.NewGuid(), "Individual", request.FullName, null,
                request.Cpf, null, request.Phone, null, null, 1, DateTime.UtcNow, DateTime.UtcNow);
            await store.ConfirmAsync(original);
            var changed = original with { FullName = "Cliente Atualizado", Phone = "11888887777", Version = 2 };

            Assert.Equal(changed, await store.RefreshConfirmedAsync(changed));
            var identity = await new CustomerStore(path).ReadAsync();
            Assert.Equal(original.Id, identity?.CustomerId);
            Assert.Equal(changed, identity?.Customer);
            Assert.Null(identity?.PendingRequest);
            Assert.Null(identity?.PendingUpdate);
            await Assert.ThrowsAsync<InvalidDataException>(() => store.RefreshConfirmedAsync(original));
            Assert.Equal(changed, (await store.ReadAsync())?.Customer);
        }
        finally
        {
            await store.CloseAsync();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task Refresh_for_order_review_preserves_pending_profile_edit()
    {
        var path = Path.Combine(Path.GetTempPath(), "vanderbeer-customer-" + Guid.NewGuid().ToString("N") + ".db");
        var store = new CustomerStore(path);
        try
        {
            var request = new CustomerWriteRequest(Guid.NewGuid(), "Individual", "Cliente Exemplo",
                null, "12345678909", null, "11999999999", null, null);
            await store.StageCreationAsync(request);
            var original = new CustomerDto(Guid.NewGuid(), "Individual", request.FullName, null,
                request.Cpf, null, request.Phone, null, null, 1, DateTime.UtcNow, DateTime.UtcNow);
            await store.ConfirmAsync(original);
            var pending = new CustomerUpdateRequest(Guid.NewGuid(), 1, "Individual", "Rascunho Local",
                null, request.Cpf, null, request.Phone, null, null);
            await store.StageUpdateAsync(pending);

            await Assert.ThrowsAsync<InvalidOperationException>(() => store.RefreshConfirmedAsync(
                original with { FullName = "Outro Perfil", Version = 2 }));
            var identity = await store.ReadAsync();
            Assert.Equal(original, identity?.Customer);
            Assert.Equal(pending, identity?.PendingUpdate);
        }
        finally
        {
            await store.CloseAsync();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task Replay_of_older_profile_update_keeps_newer_cached_version()
    {
        var path = Path.Combine(Path.GetTempPath(), "vanderbeer-customer-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var request = new CustomerWriteRequest(Guid.NewGuid(), "Individual", "Cliente Exemplo",
                null, "12345678909", null, "11999999999", null, null);
            var store = new CustomerStore(path);
            await store.StageCreationAsync(request);
            var original = new CustomerDto(Guid.NewGuid(), "Individual", request.FullName, null,
                request.Cpf, null, request.Phone, null, null, 1, DateTime.UtcNow, DateTime.UtcNow);
            await store.ConfirmAsync(original);
            await store.StageUpdateAsync(new CustomerUpdateRequest(Guid.NewGuid(), 1, "Individual",
                "Cliente Atualizado", null, request.Cpf, null, request.Phone, null, null));
            var newer = original with { FullName = "Cliente Mais Novo", Version = 3 };
            using (var db = new SQLite.SQLiteConnection(path))
            {
                var row = db.Find<CustomerStore.LocalCustomerIdentity>(1)!;
                row.CustomerJson = System.Text.Json.JsonSerializer.Serialize(newer,
                    new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
                db.Update(row);
            }
            await store.ConfirmAsync(original with { FullName = "Cliente Atualizado", Version = 2 });
            Assert.Equal(newer, (await store.ReadAsync())?.Customer);
            await store.CloseAsync();
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task Pending_profile_update_survives_restart_without_losing_confirmed_customer()
    {
        var path = Path.Combine(Path.GetTempPath(), "vanderbeer-customer-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var create = new CustomerWriteRequest(Guid.NewGuid(), "Individual", "Cliente Exemplo",
                null, "12345678909", null, "11999999999", null, null);
            var store = new CustomerStore(path);
            await store.StageCreationAsync(create);
            var customer = new CustomerDto(Guid.NewGuid(), "Individual", create.FullName, null,
                create.Cpf, null, create.Phone, null, null, 1, DateTime.UtcNow, DateTime.UtcNow);
            await store.ConfirmAsync(customer);
            var update = new CustomerUpdateRequest(Guid.NewGuid(), 1, "Individual", "Cliente Atualizado",
                null, create.Cpf, null, create.Phone, null, null);
            await store.StageUpdateAsync(update);

            var reopened = await new CustomerStore(path).ReadAsync();
            Assert.Equal(update, reopened?.PendingUpdate);
            Assert.Equal(customer, reopened?.Customer);
            await Assert.ThrowsAsync<InvalidOperationException>(() => new CustomerStore(path).StageUpdateAsync(
                update with { ClientRequestId = Guid.NewGuid() }));

            var confirmed = customer with { FullName = update.FullName, Version = 2 };
            await new CustomerStore(path).ConfirmAsync(confirmed);
            var result = await new CustomerStore(path).ReadAsync();
            Assert.Null(result?.PendingUpdate);
            Assert.Equal(confirmed, result?.Customer);
        }
        finally
        {
            await new CustomerStore(path).CloseAsync();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task Pending_key_and_body_survive_restart_then_confirmed_identity_is_saved()
    {
        var path = Path.Combine(Path.GetTempPath(), "vanderbeer-customer-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var request = new CustomerWriteRequest(Guid.NewGuid(), "Individual", "Cliente Exemplo",
                null, "12345678909", null, "11999999999", null, null);
            await new CustomerStore(path).StageCreationAsync(request);

            var afterRestart = await new CustomerStore(path).ReadAsync();
            Assert.Equal(request, afterRestart?.PendingRequest);
            Assert.Null(afterRestart?.CustomerId);

            var customer = new CustomerDto(Guid.NewGuid(), "Individual", request.FullName, null,
                request.Cpf, null, request.Phone, null, null, 1, DateTime.UtcNow, DateTime.UtcNow);
            await new CustomerStore(path).ConfirmAsync(customer);

            var confirmed = await new CustomerStore(path).ReadAsync();
            Assert.Null(confirmed?.PendingRequest);
            Assert.Equal(customer.Id, confirmed?.CustomerId);
            Assert.Equal(customer.Id, confirmed?.Customer?.Id);
        }
        finally
        {
            await new CustomerStore(path).CloseAsync();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task Definitive_rejection_keeps_draft_and_allows_a_new_key()
    {
        var path = Path.Combine(Path.GetTempPath(), "vanderbeer-customer-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var request = new CustomerWriteRequest(Guid.NewGuid(), "Business", "Contato Exemplo",
                "Empresa Exemplo", null, "11222333000181", "1133334444", null, null);
            var store = new CustomerStore(path);
            await store.StageCreationAsync(request);
            await store.MarkRejectedAsync("DuplicateCnpj");

            var rejected = await new CustomerStore(path).ReadAsync();
            Assert.Null(rejected?.PendingRequest);
            Assert.Equal(request, rejected?.Draft);
            Assert.Equal("DuplicateCnpj", rejected?.LastErrorCode);

            var corrected = request with { ClientRequestId = Guid.NewGuid(), Cnpj = "00000000E08G12" };
            await new CustomerStore(path).StageCreationAsync(corrected);
            Assert.Equal(corrected, (await new CustomerStore(path).ReadAsync())?.PendingRequest);
        }
        finally
        {
            await new CustomerStore(path).CloseAsync();
            if (File.Exists(path)) File.Delete(path);
        }
    }
}

