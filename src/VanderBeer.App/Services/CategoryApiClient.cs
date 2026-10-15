using System.Net.Http.Json;
using VanderBeer.Contracts.Catalog;

namespace VanderBeer.App.Services;

public sealed class CategoryApiClient(HttpClient httpClient)
{
    public async Task<IReadOnlyList<CategoryDto>> GetCategoriesAsync(string baseUrl, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var address) || address.Scheme != Uri.UriSchemeHttp)
        {
            throw new ArgumentException("Informe um endereço HTTP válido da API.", nameof(baseUrl));
        }

        var requestUrl = new Uri(address, "/api/v1/categories");
        return await httpClient.GetFromJsonAsync<List<CategoryDto>>(requestUrl, cancellationToken) ?? [];
    }
}
