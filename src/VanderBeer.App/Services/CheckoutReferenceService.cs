using System.Net.Http.Json;
using System.Text.Json;
using VanderBeer.App.Storage;
using VanderBeer.Contracts.Catalog;

namespace VanderBeer.App.Services;

public sealed class CheckoutReferenceService(HttpClient http, CatalogStore store)
{
    public async Task<(StoreSettingsDto Settings, IReadOnlyList<PaymentMethodDto> Payments, bool Cached)>
        LoadAsync(string baseUrl, CancellationToken token)
    {
        try
        {
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) ||
                uri.Scheme is not ("http" or "https"))
                throw new ArgumentException("Endereço da API inválido.");
            using var settingsResponse = await http.GetAsync(new Uri(uri, "/api/v1/store-settings"), token);
            if (!settingsResponse.IsSuccessStatusCode) throw new HttpRequestException();
            var settings = await settingsResponse.Content.ReadFromJsonAsync<StoreSettingsDto>(cancellationToken: token)
                ?? throw new JsonException();
            using var paymentsResponse = await http.GetAsync(new Uri(uri, "/api/v1/payment-methods"), token);
            if (!paymentsResponse.IsSuccessStatusCode) throw new HttpRequestException();
            var payments = await paymentsResponse.Content.ReadFromJsonAsync<List<PaymentMethodDto>>(cancellationToken: token)
                ?? throw new JsonException();
            await store.SaveCheckoutReferencesAsync(settings, payments);
            return (settings, payments, false);
        }
        catch (Exception)
        {
            var cached = await store.CheckoutReferencesAsync();
            if (cached is null) throw new InvalidOperationException("Consulte as opções de pedido com conexão antes de finalizar.");
            return (cached.Value.Settings, cached.Value.Payments, true);
        }
    }
}
