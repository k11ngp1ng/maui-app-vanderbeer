using System.Net.Http.Json;
using System.Text.Json;
using VanderBeer.Contracts.Customers;

namespace VanderBeer.App.Services;

public sealed class CustomerApiClient(HttpClient http)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<CustomerDto> GetAsync(string baseUrl, Guid customerId, CancellationToken token)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var root) ||
            root.Scheme is not ("http" or "https"))
            throw new ArgumentException("Informe um endereço HTTP ou HTTPS válido da API.");
        using var response = await http.GetAsync(new Uri(root, $"/api/v1/customers/{customerId:D}"), token);
        if (!response.IsSuccessStatusCode) throw await ApiException.FromResponseAsync(response, token);
        return await response.Content.ReadFromJsonAsync<CustomerDto>(JsonOptions, token)
            ?? throw new InvalidDataException("Resposta vazia da API.");
    }

    public async Task<CustomerDto> CreateAsync(string baseUrl, CustomerWriteRequest request, CancellationToken token)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var root) ||
            root.Scheme is not ("http" or "https"))
            throw new ArgumentException("Informe um endereço HTTP ou HTTPS válido da API.");
        using var response = await http.PostAsJsonAsync(new Uri(root, "/api/v1/customers"), request, JsonOptions, token);
        if (!response.IsSuccessStatusCode) throw await ApiException.FromResponseAsync(response, token);
        return await response.Content.ReadFromJsonAsync<CustomerDto>(JsonOptions, token)
            ?? throw new InvalidDataException("Resposta vazia da API.");
    }

    public async Task<CustomerDto> UpdateAsync(string baseUrl, Guid customerId,
        CustomerUpdateRequest request, CancellationToken token)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var root) || root.Scheme is not ("http" or "https"))
            throw new ArgumentException("Informe um endereço HTTP ou HTTPS válido da API.");
        using var response = await http.PutAsJsonAsync(new Uri(root, $"/api/v1/customers/{customerId:D}"),
            request, JsonOptions, token);
        if (!response.IsSuccessStatusCode) throw await ApiException.FromResponseAsync(response, token);
        return await response.Content.ReadFromJsonAsync<CustomerDto>(JsonOptions, token)
            ?? throw new InvalidDataException("Resposta vazia da API.");
    }
}
