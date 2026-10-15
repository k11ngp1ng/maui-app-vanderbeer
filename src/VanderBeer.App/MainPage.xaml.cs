using VanderBeer.App.Features.Catalog;

namespace VanderBeer.App;

public partial class MainPage : ContentPage
{
    private readonly CatalogViewModel _viewModel;
    private bool _initialized;

    public MainPage(CatalogViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
        _viewModel.Profile.ConfirmDiscard = () => DisplayAlertAsync("Descartar edição?",
            "Os dados alterados ainda não foram salvos.", "Descartar", "Continuar editando");
        SizeChanged += (_, _) => UpdateNavigationLayout();
        ContentScroll.SizeChanged += (_, _) =>
        {
            if (ContentScroll.Width > 0) ContentBody.WidthRequest = Math.Min(ContentScroll.Width, 720);
        };
        _viewModel.Profile.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Features.Customers.ProfileViewModel.StepTitle))
                Dispatcher.Dispatch(async () => await ContentScroll.ScrollToAsync(0, 0, false));
        };
        _viewModel.Checkout.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Features.Checkout.CheckoutViewModel.StepTitle))
                Dispatcher.Dispatch(async () => await ContentScroll.ScrollToAsync(0, 0, false));
        };
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CatalogViewModel.ShowClientNavigation)) UpdateNavigationLayout();
            if (e.PropertyName == nameof(CatalogViewModel.IsList))
                Dispatcher.Dispatch(async () => await ContentScroll.ScrollToAsync(0, 0, false));
        };
        _viewModel.CompanyOrders.RequestReason = () => DisplayPromptAsync("Motivo do atendimento",
            "Informe o motivo para rejeitar ou cancelar este pedido (3 a 300 caracteres).",
            "Continuar", "Voltar", maxLength: 300);
        _viewModel.CompanyOrders.ConfirmAction = (protocol, status) =>
            DisplayAlertAsync("Confirmar atendimento?",
                $"{protocol}: alterar para {Features.Orders.CompanyOrdersViewModel.StatusLabel(status)}?",
                "Confirmar", "Voltar");
        Connectivity.Current.ConnectivityChanged += (_, e) =>
        {
            if (e.NetworkAccess != NetworkAccess.None)
                MainThread.BeginInvokeOnMainThread(_viewModel.ResumeSynchronization);
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_initialized) { _viewModel.ResumeSynchronization(); return; }
        _initialized = true;
        await _viewModel.InitializeAsync();
    }

    public void ResumeSynchronization() => _viewModel.ResumeSynchronization();

    private void UpdateNavigationLayout()
    {
        var desktop = DeviceInfo.Platform == DevicePlatform.WinUI && Width >= 900 &&
            (_viewModel.ShowClientNavigation || _viewModel.ShowCompanyNavigation);
        DesktopNavigation.IsVisible = desktop;
        BottomNavigation.IsVisible = !desktop && (_viewModel.ShowClientNavigation || _viewModel.ShowCompanyNavigation);
    }

    private async void OnConnectionSettings(object? sender, EventArgs e)
    {
        var value = await DisplayPromptAsync("Conexão da demonstração", "Endereço do servidor VanderBeer:",
            "Salvar", "Cancelar", initialValue: _viewModel.ApiAddress, keyboard: Keyboard.Url);
        if (value is null) return;
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo))
        {
            await DisplayAlertAsync("Endereço inválido", "Informe um endereço HTTP ou HTTPS válido.", "OK");
            return;
        }
        await _viewModel.SaveConnectionAsync(value.Trim().TrimEnd('/'));
    }

    private void OnBackCheckout(object? sender, EventArgs e)
    {
        if (_viewModel.Checkout.IsBusy) return;
        if (!_viewModel.Checkout.PreviousStep()) _viewModel.BackFromCheckoutCommand.Execute(null);
    }

    private async void OnBackFromEditor(object? sender, EventArgs e)
    {
        if (_viewModel.HasUnsavedChanges &&
            !await DisplayAlertAsync("Descartar edição?", "Os campos alterados ainda não foram salvos.",
                "Descartar", "Continuar editando")) return;
        await _viewModel.ReturnToListAsync();
    }

    private async void OnSaveCheckout(object? sender, EventArgs e)
    {
        if (!_viewModel.Checkout.CanSave) return;
        if (!await _viewModel.Checkout.ValidateAsync()) return;
        if (!await DisplayAlertAsync("Registrar encomenda?",
            _viewModel.Checkout.Summary + "\n\n" + _viewModel.Checkout.ReceivingSummary + "\n\n" +
            _viewModel.Checkout.PaymentSummary + "\n\nO pedido será salvo neste aparelho e ficará aguardando envio.",
            "Registrar", "Revisar")) return;
        await _viewModel.SaveCheckoutAsync();
    }

    private async void OnConfirmReorder(object? sender, EventArgs e)
    {
        if (!_viewModel.CanConfirmReorder) return;
        try
        {
            if (await _viewModel.HasCartForReorderAsync() &&
                !await DisplayAlertAsync("Substituir carrinho?",
                    "A recompra substituirá os produtos que já estão no carrinho.",
                    "Substituir", "Cancelar")) return;
            await _viewModel.ConfirmReorderAsync();
        }
        catch (Exception)
        {
            await DisplayAlertAsync("Carrinho indisponível",
                "Não foi possível consultar o carrinho salvo. Tente novamente.", "OK");
        }
    }

    private void OnCloseCompanyOrder(object? sender, EventArgs e) => _viewModel.CompanyOrders.CloseDetail();

    protected override bool OnBackButtonPressed()
    {
        if (_viewModel.IsEditor)
        {
            Dispatcher.Dispatch(() => OnBackFromEditor(this, EventArgs.Empty));
            return true;
        }
        if (_viewModel.IsDetail)
        {
            Dispatcher.Dispatch(async () => await _viewModel.ReturnToListAsync());
            return true;
        }
        if (_viewModel.IsProfile)
        {
            Dispatcher.Dispatch(() => _viewModel.BackFromProfileCommand.Execute(null));
            return true;
        }
        if (_viewModel.IsCart)
        {
            Dispatcher.Dispatch(() => _viewModel.BackFromCartCommand.Execute(null));
            return true;
        }
        if (_viewModel.IsCheckout)
        {
            Dispatcher.Dispatch(() => OnBackCheckout(this, EventArgs.Empty));
            return true;
        }
        if (_viewModel.IsLocalOrder)
        {
            Dispatcher.Dispatch(() => _viewModel.BackFromCartCommand.Execute(null));
            return true;
        }
        if (_viewModel.IsOrders)
        {
            Dispatcher.Dispatch(() =>
            {
                if (_viewModel.Orders.HasDetail) _viewModel.Orders.CloseDetailCommand.Execute(null);
                else _viewModel.BackFromCartCommand.Execute(null);
            });
            return true;
        }
        if (_viewModel.IsReorder)
        {
            Dispatcher.Dispatch(() => _viewModel.BackFromReorderCommand.Execute(null));
            return true;
        }
        if (_viewModel.IsCompanyOrders)
        {
            Dispatcher.Dispatch(() =>
            {
                if (_viewModel.CompanyOrders.HasDetail) _viewModel.CompanyOrders.CloseDetail();
                else _viewModel.BackCommand.Execute(null);
            });
            return true;
        }
        return base.OnBackButtonPressed();
    }
}
