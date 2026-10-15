using VanderBeer.App.Storage;
using VanderBeer.Contracts.Customers;
using System.Net;

namespace VanderBeer.App.Services;

public sealed class CustomerRegistrationService(CustomerStore store, CustomerApiClient api)
{
    public Task<CustomerIdentity?> ReadAsync() => store.ReadAsync();

    public async Task<CustomerDto> CreateAsync(string baseUrl, CustomerWriteRequest request, CancellationToken token)
    {
        await store.StageCreationAsync(request.ClientRequestId == Guid.Empty
            ? request with { ClientRequestId = Guid.NewGuid() } : request);
        return await RetryPendingAsync(baseUrl, token);
    }

    public async Task<CustomerDto> UpdateAsync(string baseUrl, CustomerUpdateRequest request, CancellationToken token)
    {
        await store.StageUpdateAsync(request.ClientRequestId == Guid.Empty
            ? request with { ClientRequestId = Guid.NewGuid() } : request);
        return await RetryPendingAsync(baseUrl, token);
    }

    public async Task<CustomerDto> RetryPendingAsync(string baseUrl, CancellationToken token)
    {
        var identity = await store.ReadAsync();
        if (identity?.PendingRequest is null && identity?.PendingUpdate is null)
            throw new InvalidOperationException("Nenhum cadastro pendente.");
        try
        {
            var customer = identity.PendingUpdate is { } update
                ? await api.UpdateAsync(baseUrl, identity.CustomerId!.Value, update, token)
                : await api.CreateAsync(baseUrl, identity.PendingRequest!, token);
            await store.ConfirmAsync(customer);
            return customer;
        }
        catch (ApiException error) when (error.Status == HttpStatusCode.BadRequest ||
            error.Status == HttpStatusCode.Conflict && error.Problem?.Code is "DuplicateCpf" or "DuplicateCnpj" or "CustomerVersionConflict")
        {
            await store.MarkRejectedAsync(error.Problem?.Code ?? "ValidationFailed");
            throw;
        }
    }
}
