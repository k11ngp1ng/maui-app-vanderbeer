using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using VanderBeer.Contracts.Orders;

namespace VanderBeer.App.Services;

public sealed class OrderApiClient(HttpClient http)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<OrderDto> SendAsync(string baseUrl, string path, string json, CancellationToken token)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var root) || root.Scheme is not ("http" or "https"))
            throw new ArgumentException("Informe um endereço HTTP ou HTTPS válido da API.");
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(root, path))
        { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        using var response = await http.SendAsync(request, token);
        if (!response.IsSuccessStatusCode) throw await ApiException.FromResponseAsync(response, token);
        return await response.Content.ReadFromJsonAsync<OrderDto>(JsonOptions, token)
            ?? throw new InvalidDataException("Resposta vazia da API.");
    }

    public async Task<OrderPageDto> ListCustomerAsync(string baseUrl, Guid customerId, int page,
        CancellationToken token) => await GetAsync<OrderPageDto>(baseUrl,
            $"/api/v1/customers/{customerId:D}/orders?page={page}&pageSize=20", token);

    public async Task<OrderDto> GetAsync(string baseUrl, Guid id, CancellationToken token) =>
        await GetAsync<OrderDto>(baseUrl, $"/api/v1/orders/{id:D}", token);

    public Task<OrderPageDto> ListCompanyAsync(string baseUrl, int page, string? status,
        DateOnly? from, DateOnly? to, CancellationToken token)
    {
        var parts = new List<string> { $"page={page}", "pageSize=20" };
        if (status is not null) parts.Add("status=" + Uri.EscapeDataString(status));
        if (from is not null) parts.Add("fromDate=" + from.Value.ToString("yyyy-MM-dd"));
        if (to is not null) parts.Add("toDate=" + to.Value.ToString("yyyy-MM-dd"));
        return GetAsync<OrderPageDto>(baseUrl, "/api/v1/admin/orders?" + string.Join("&", parts), token);
    }

    public Task<OrderDto> GetCompanyAsync(string baseUrl, Guid id, CancellationToken token) =>
        GetAsync<OrderDto>(baseUrl, $"/api/v1/admin/orders/{id:D}", token);

    public async Task<OrderDto> UpdateCompanyStatusAsync(string baseUrl, Guid id, string json,
        CancellationToken token)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var root) || root.Scheme is not ("http" or "https"))
            throw new ArgumentException("Informe um endereço HTTP ou HTTPS válido da API.");
        using var request = new HttpRequestMessage(HttpMethod.Patch,
            new Uri(root, $"/api/v1/admin/orders/{id:D}/status"))
        { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        using var response = await http.SendAsync(request, token);
        if (!response.IsSuccessStatusCode) throw await ApiException.FromResponseAsync(response, token);
        return await response.Content.ReadFromJsonAsync<OrderDto>(JsonOptions, token)
            ?? throw new InvalidDataException("Resposta vazia da API.");
    }

    private async Task<T> GetAsync<T>(string baseUrl, string path, CancellationToken token)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var root) || root.Scheme is not ("http" or "https"))
            throw new ArgumentException("Informe um endereço HTTP ou HTTPS válido da API.");
        using var response = await http.GetAsync(new Uri(root, path), token);
        if (!response.IsSuccessStatusCode) throw await ApiException.FromResponseAsync(response, token);
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, token)
            ?? throw new InvalidDataException("Resposta vazia da API.");
    }
}
