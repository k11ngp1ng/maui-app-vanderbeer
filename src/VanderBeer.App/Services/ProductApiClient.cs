using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using VanderBeer.Contracts.Catalog;
using VanderBeer.Contracts.Operations;

namespace VanderBeer.App.Services;

public sealed class ProductApiClient(HttpClient http)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<CategoryDto>> CategoriesAsync(string baseUrl, CancellationToken token) =>
        await GetAsync<List<CategoryDto>>(baseUrl, "/api/v1/categories", token) ?? [];

    public Task<ProductPageDto?> ProductsAsync(string baseUrl, bool company, int page, string? search,
        Guid? categoryId, bool? isActive, CancellationToken token)
    {
        var path = company ? "/api/v1/admin/products" : "/api/v1/products";
        var parts = new List<string> { $"page={page}", "pageSize=20" };
        if (!string.IsNullOrWhiteSpace(search)) parts.Add("search=" + Uri.EscapeDataString(search.Trim()));
        if (categoryId is not null) parts.Add("categoryId=" + categoryId.Value.ToString("D"));
        if (company && isActive is not null) parts.Add("isActive=" + (isActive.Value ? "true" : "false"));
        return GetAsync<ProductPageDto>(baseUrl, path + "?" + string.Join("&", parts), token);
    }

    public Task<ProductDto?> ProductAsync(string baseUrl, Guid id, bool company, CancellationToken token) =>
        GetAsync<ProductDto>(baseUrl, $"/api/v1/{(company ? "admin/products" : "products")}/{id:D}", token);

    public async Task<ProductDto> SendAsync(string baseUrl, string path, string method, string json,
        CancellationToken token)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), Address(baseUrl, path))
        { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        using var response = await http.SendAsync(request, token);
        if (!response.IsSuccessStatusCode)
            throw await ApiException.FromResponseAsync(response, token);
        return await response.Content.ReadFromJsonAsync<ProductDto>(JsonOptions, token)
            ?? throw new InvalidDataException("Resposta vazia da API.");
    }

    public static string Json<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);

    private async Task<T?> GetAsync<T>(string baseUrl, string path, CancellationToken token)
    {
        using var response = await http.GetAsync(Address(baseUrl, path), token);
        if (response.StatusCode == HttpStatusCode.NotFound) return default;
        if (!response.IsSuccessStatusCode) throw await ApiException.FromResponseAsync(response, token);
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, token);
    }

    private static Uri Address(string baseUrl, string path)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("Informe um endereço HTTP ou HTTPS válido da API.");
        return new Uri(uri, path);
    }
}

public sealed class ApiException(HttpStatusCode status, ApiProblemDto? problem, TimeSpan? retryAfter = null) : Exception(problem?.Detail ?? "Falha na API.")
{
    public HttpStatusCode Status { get; } = status;
    public ApiProblemDto? Problem { get; } = problem;
    public TimeSpan? RetryAfter { get; } = retryAfter;

    public static async Task<ApiException> FromResponseAsync(HttpResponseMessage response, CancellationToken token)
    {
        var header = response.Headers.RetryAfter;
        var retryAfter = header?.Delta ?? (header?.Date - DateTimeOffset.UtcNow);
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ApiProblemDto>(cancellationToken: token);
            return new(response.StatusCode, problem, retryAfter);
        }
        catch (JsonException) { return new(response.StatusCode, null, retryAfter); }
    }
}
