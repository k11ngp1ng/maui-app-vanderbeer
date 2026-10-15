using Microsoft.Extensions.DependencyInjection;

namespace VanderBeer.App;

public partial class App : Application
{
    private readonly MainPage _mainPage;

    public App(IServiceProvider services)
    {
        InitializeComponent();
        // Pages use the application resources, which must exist before XAML is inflated.
        _mainPage = services.GetRequiredService<MainPage>();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(_mainPage) { Title = "VanderBeer" };
        window.Resumed += (_, _) => _mainPage.ResumeSynchronization();
        return window;
    }
}
