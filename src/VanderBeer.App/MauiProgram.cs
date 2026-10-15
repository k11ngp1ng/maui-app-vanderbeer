using Microsoft.Extensions.Logging;
using VanderBeer.App.Features.Catalog;
using VanderBeer.App.Features.Customers;
using VanderBeer.App.Features.Cart;
using VanderBeer.App.Features.Checkout;
using VanderBeer.App.Features.Orders;
using VanderBeer.App.Services;
using VanderBeer.App.Storage;

namespace VanderBeer.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        builder.Services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromSeconds(10) });
        builder.Services.AddSingleton<CategoryApiClient>();
        builder.Services.AddSingleton<ProbeStore>();
        builder.Services.AddSingleton<ProductApiClient>();
        builder.Services.AddSingleton<CatalogStore>();
        builder.Services.AddSingleton<CustomerStore>();
        builder.Services.AddSingleton<CustomerApiClient>();
        builder.Services.AddSingleton<CustomerRegistrationService>();
        builder.Services.AddSingleton<ProfileViewModel>();
        builder.Services.AddSingleton<CartStore>();
        builder.Services.AddSingleton<CartViewModel>();
        builder.Services.AddSingleton<LocalOrderStore>();
        builder.Services.AddSingleton<CheckoutReferenceService>();
        builder.Services.AddSingleton<CheckoutViewModel>();
        builder.Services.AddSingleton<OrderApiClient>();
        builder.Services.AddSingleton(_ => new CompanyOrderCommandStore());
        builder.Services.AddSingleton<CompanyOrdersViewModel>();
        builder.Services.AddSingleton<OrdersViewModel>();
        builder.Services.AddSingleton<CatalogViewModel>();
        builder.Services.AddSingleton<MainPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
