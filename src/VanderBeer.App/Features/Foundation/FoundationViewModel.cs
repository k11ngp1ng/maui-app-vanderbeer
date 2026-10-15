using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using VanderBeer.App.Services;
using VanderBeer.App.Storage;

namespace VanderBeer.App.Features.Foundation;

public sealed class FoundationViewModel(CategoryApiClient apiClient, ProbeStore probeStore) : INotifyPropertyChanged
{
    private string _apiAddress = DeviceInfo.Platform == DevicePlatform.Android
        ? "http://10.0.2.2:5080"
        : "http://127.0.0.1:5080";
    private string _localStatus = "Aguardando leitura local.";
    private string _apiStatus = "Aguardando consulta da API.";
    private bool _isBusy;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string ApiAddress
    {
        get => _apiAddress;
        set => SetField(ref _apiAddress, value);
    }

    public string LocalStatus
    {
        get => _localStatus;
        private set => SetField(ref _localStatus, value);
    }

    public string ApiStatus
    {
        get => _apiStatus;
        private set => SetField(ref _apiStatus, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetField(ref _isBusy, value);
    }

    public ICommand LoadCategoriesCommand => new Command(async () => await LoadCategoriesAsync());

    public async Task InitializeAsync()
    {
        try
        {
            var launchCount = await probeStore.RecordLaunchAsync();
            LocalStatus = $"SQLite: abertura {launchCount}. Feche e abra o app para conferir a persistência.";
        }
        catch (Exception)
        {
            LocalStatus = "Não foi possível gravar ou ler o SQLite local.";
        }

        await LoadCategoriesAsync();
    }

    private async Task LoadCategoriesAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        ApiStatus = "Consultando categorias...";
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var categories = await apiClient.GetCategoriesAsync(ApiAddress, timeout.Token);
            ApiStatus = categories.Count == 0
                ? "API conectada; nenhuma categoria ativa."
                : $"API conectada: {string.Join(", ", categories.Select(category => category.Name))}.";
        }
        catch (Exception)
        {
            ApiStatus = "API indisponível neste endereço. Verifique o servidor e a rede.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
