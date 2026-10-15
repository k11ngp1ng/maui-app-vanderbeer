using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Input;
using VanderBeer.App.Services;
using VanderBeer.App.Storage;
using VanderBeer.Contracts.Orders;

namespace VanderBeer.App.Features.Orders;

public sealed class CompanyOrdersViewModel(OrderApiClient api, CompanyOrderCommandStore store)
    : INotifyPropertyChanged
{
    private static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly (string Status, string Label)[] Actions =
    [
        ("Confirmed", "Confirmar"), ("Rejected", "Rejeitar"), ("Preparing", "Iniciar preparo"),
        ("OutForDelivery", "Enviar para entrega"), ("ReadyForPickup", "Marcar pronto para retirada"),
        ("Delivered", "Concluir entrega"), ("Collected", "Concluir retirada"), ("Cancelled", "Cancelar")
    ];
    private bool _busy;
    private int _page = 1;
    private long _total;
    private string _message = "";
    private string _fromDate = "";
    private string _toDate = "";
    private string _reason = "";
    private string _selectedFilter = "Todos";
    private OrderDto? _selected;
    private bool _hasPending;

    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<CompanyOrderItem> Items { get; } = [];
    public ObservableCollection<CompanyStatusAction> AvailableActions { get; } = [];
    private static readonly string[] FilterCodes =
        ["Todos", "Received", "Confirmed", "Preparing", "OutForDelivery", "ReadyForPickup",
         "Delivered", "Collected", "Rejected", "Cancelled"];
    public IReadOnlyList<string> Filters => FilterCodes.Select(x => x == "Todos" ? x : StatusLabel(x)).ToArray();
    private string? FilterCode => FilterCodes.FirstOrDefault(x => (x == "Todos" ? x : StatusLabel(x)) == SelectedFilter) is { } code && code != "Todos" ? code : null;
    public bool ShowList => !HasDetail;
    public string BaseUrl { get; set; } = "";
    public Func<string, string, Task<bool>>? ConfirmAction { get; set; }
    public Func<Task<string?>>? RequestReason { get; set; }
    public bool IsBusy { get => _busy; private set => Set(ref _busy, value); }
    public bool HasDetail => _selected is not null;
    public bool HasPending { get => _hasPending; private set => Set(ref _hasPending, value); }
    public string Message { get => _message; private set => Set(ref _message, value); }
    public string FromDate { get => _fromDate; set => Set(ref _fromDate, value); }
    public string ToDate { get => _toDate; set => Set(ref _toDate, value); }
    public string Reason { get => _reason; set => Set(ref _reason, value); }
    public string SelectedFilter { get => _selectedFilter; set => Set(ref _selectedFilter, value); }
    public string PageLabel => $"Página {_page} · {_total} pedido(s)";
    public bool CanPrevious => _page > 1;
    public bool CanNext => _page * 20 < _total;
    public string Detail => _selected is null ? "" : FormatDetail(_selected);
    public ICommand RefreshCommand => new Command(async () => { _page = 1; await RefreshAsync(); });
    public ICommand PreviousCommand => new Command(async () => { if (CanPrevious) { _page--; await RefreshAsync(); } });
    public ICommand NextCommand => new Command(async () => { if (CanNext) { _page++; await RefreshAsync(); } });
    public ICommand RecoverCommand => new Command(async () => await RecoverAsync());

    public async Task RefreshAsync()
    {
        if (IsBusy) return;
        if (!ParseDate(FromDate, out var from) || !ParseDate(ToDate, out var to) || from > to)
        { Message = "Informe datas válidas em dd/MM/aaaa e um intervalo em ordem."; return; }
        IsBusy = true;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var page = await api.ListCompanyAsync(BaseUrl, _page,
                FilterCode, from, to, timeout.Token);
            Items.Clear();
            foreach (var order in page.Items)
                Items.Add(new CompanyOrderItem(order, new Command(async () => await OpenAsync(order.Id))));
            _total = page.Total;
            Notify(nameof(PageLabel)); Notify(nameof(CanPrevious)); Notify(nameof(CanNext));
            Message = page.Items.Count == 0 ? "Nenhum pedido encontrado." : "Fila atualizada pela loja.";
        }
        catch (Exception) { Message = "Não foi possível carregar a fila. Confira a conexão com a loja."; }
        finally { IsBusy = false; }
    }

    public async Task OpenAsync(Guid id)
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            _selected = await api.GetCompanyAsync(BaseUrl, id, timeout.Token);
            HasPending = await store.PendingAsync(id) is not null;
            Message = HasPending ? "Não foi possível confirmar o resultado. Verifique antes de outra ação." : "";
            ShowDetail();
        }
        catch (Exception) { Message = "Não foi possível carregar o pedido. Confira a conexão com a loja."; }
        finally { IsBusy = false; }
    }

    public void CloseDetail() { if (IsBusy) return; _selected = null; HasPending = false; ShowDetail(); }

    private void ShowDetail()
    {
        Notify(nameof(HasDetail)); Notify(nameof(ShowList)); Notify(nameof(Detail));
        AvailableActions.Clear();
        if (_selected is null || HasPending) return;
        foreach (var action in Actions.Where(x => CanMove(_selected.Status,
                     x.Status, _selected.FulfillmentType)).OrderBy(x => x.Status is "Rejected" or "Cancelled" ? 1 : 0))
            AvailableActions.Add(new CompanyStatusAction(action.Label, action.Status is "Rejected" or "Cancelled",
                new Command(async () => await ApplyAsync(action.Status))));
    }

    private async Task ApplyAsync(string next)
    {
        if (_selected is null || IsBusy || HasPending) return;
        if (next is "Rejected" or "Cancelled" && RequestReason is not null)
        {
            var value = await RequestReason();
            if (value is null) return;
            Reason = value;
        }
        var reason = next is "Rejected" or "Cancelled" ? Reason.Trim() : null;
        if (reason is not null && reason.Length is < 3 or > 300)
        { Message = "Informe um motivo de 3 a 300 caracteres."; return; }
        if (Connectivity.Current.NetworkAccess == NetworkAccess.None)
        { Message = "Conecte-se à API antes de alterar o atendimento."; return; }
        IsBusy = true;
        AvailableActions.Clear();
        var id = _selected.Id;
        try
        {
            if (ConfirmAction is not null && !await ConfirmAction(_selected.Protocol, next)) return;
            var request = new UpdateOrderStatusRequest(Guid.NewGuid(), _selected.Version, next, reason);
            await store.StartAsync(id, BaseUrl, JsonSerializer.Serialize(request, JsonOptions));
            HasPending = true;
            ShowDetail();
            await RecoverCoreAsync(id);
        }
        catch (InvalidOperationException error) { Message = error.Message; }
        catch (Exception)
        {
            Message = HasPending
                ? "Não foi possível confirmar o resultado. Use Verificar resultado com conexão."
                : "Não foi possível salvar o comando localmente. Nenhuma alteração foi enviada.";
        }
        finally { IsBusy = false; ShowDetail(); }
    }

    public async Task RecoverAsync()
    {
        if (_selected is null || IsBusy || !HasPending) return;
        IsBusy = true;
        try { await RecoverCoreAsync(_selected.Id); }
        catch (Exception) { Message = "Não foi possível verificar o resultado. O comando permanece salvo."; }
        finally { IsBusy = false; }
    }

    private async Task RecoverCoreAsync(Guid id)
    {
        var pending = await store.PendingAsync(id);
        if (pending is null) return;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await api.UpdateCompanyStatusAsync(pending.BaseUrl, id, pending.RequestJson, timeout.Token);
            await store.CompleteAsync(id);
            HasPending = false;
            Reason = "";
            Message = "Resultado confirmado pela loja.";
            try { _selected = await api.GetCompanyAsync(pending.BaseUrl, id, timeout.Token); }
            catch (Exception) { Message += " Atualize o detalhe quando houver conexão."; }
            UpdateListFromDetail();
            ShowDetail();
        }
        catch (ApiException error) when ((int)error.Status is >= 400 and < 500)
        {
            await store.CompleteAsync(id);
            HasPending = false;
            Message = error.Problem?.Detail ?? "A alteração foi recusada. Atualize o pedido.";
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                _selected = await api.GetCompanyAsync(pending.BaseUrl, id, timeout.Token);
            }
            catch (Exception) { Message += " Atualize quando houver conexão."; }
            UpdateListFromDetail();
            ShowDetail();
        }
        catch (Exception)
        {
            Message = "Não foi possível confirmar o resultado. Use Verificar resultado com conexão.";
            HasPending = true;
            ShowDetail();
        }
    }

    private void UpdateListFromDetail()
    {
        if (_selected is null) return;
        var index = Items.ToList().FindIndex(x => x.Order.Id == _selected.Id);
        if (index < 0) return;
        if (FilterCode is not null && FilterCode != _selected.Status)
        {
            Items.RemoveAt(index);
            _total = Math.Max(0, _total - 1);
        }
        else
        {
            var order = _selected;
            var summary = new OrderSummaryDto(order.Id, order.Protocol, order.CreatedAtUtc,
                order.RequestedDate, order.FulfillmentType, order.Status, order.Total, order.Version);
            Items[index] = new CompanyOrderItem(summary, new Command(async () => await OpenAsync(order.Id)));
        }
        Notify(nameof(PageLabel)); Notify(nameof(CanNext));
    }

    private static bool ParseDate(string value, out DateOnly? date)
    {
        date = null;
        if (string.IsNullOrWhiteSpace(value)) return true;
        if (!DateOnly.TryParseExact(value.Trim(), "dd/MM/yyyy", Br, DateTimeStyles.None, out var parsed)) return false;
        date = parsed;
        return true;
    }

    private static bool CanMove(string current, string next, string fulfillment) => (current, next) switch
    {
        ("Received", "Confirmed" or "Rejected" or "Cancelled") => true,
        ("Confirmed", "Preparing" or "Cancelled") => true,
        ("Preparing", "Cancelled") => true,
        ("Preparing", "OutForDelivery") => fulfillment == "Delivery",
        ("Preparing", "ReadyForPickup") => fulfillment == "Pickup",
        ("OutForDelivery", "Delivered") => fulfillment == "Delivery",
        ("ReadyForPickup", "Collected") => fulfillment == "Pickup",
        _ => false
    };

    private static string FormatDetail(OrderDto order)
    {
        var lines = order.Items.Select(x => $"{x.Quantity} × {x.ProductName} ({x.UnitLabel}) · {x.Subtotal.ToString("C", Br)}");
        var address = order.DeliveryAddress is null ? "Retirada na loja" :
            $"{order.DeliveryAddress.Street}, {order.DeliveryAddress.Number}" +
            (string.IsNullOrWhiteSpace(order.DeliveryAddress.Complement) ? "" :
                $", {order.DeliveryAddress.Complement}") +
            $" · {order.DeliveryAddress.District} · {order.DeliveryAddress.City}/{order.DeliveryAddress.State}" +
            $" · CEP {order.DeliveryAddress.PostalCode}";
        var history = order.StatusHistory.Select(x =>
            $"{VanderBeer.App.Features.Shared.OrderTimeFormat.InStoreZone(x.OccurredAtUtc)} · {StatusLabel(x.ToStatus)}" +
            (x.Reason is null ? "" : $" · {x.Reason}"));
        return $"{order.Protocol} · versão {order.Version}\n" +
            $"Criado: {VanderBeer.App.Features.Shared.OrderTimeFormat.InStoreZone(order.CreatedAtUtc)}\n" +
            $"Data desejada: {order.RequestedDate:dd/MM/yyyy} · {order.FulfillmentType}\n" +
            $"Atendimento: {StatusLabel(order.Status)}\nContato: {order.Customer.Name} · {order.Customer.Phone}\n" +
            $"Endereço: {address}\nPagamento: {order.PaymentMethod.Name}" +
            (order.CashTendered is null ? "" : $" · Receber {order.CashTendered.Value.ToString("C", Br)} · Troco {order.ChangeDue.ToString("C", Br)}") +
            $"\nItens:\n{string.Join("\n", lines)}\nTotal: {order.Total.ToString("C", Br)}" +
            (string.IsNullOrWhiteSpace(order.Notes) ? "" : $"\nObservações: {order.Notes}") +
            $"\nHistórico:\n{string.Join("\n", history)}";
    }

    public static string StatusLabel(string status) => status switch
    {
        "Received" => "Recebido", "Confirmed" => "Confirmado", "Preparing" => "Em preparo",
        "OutForDelivery" => "Saiu para entrega", "Delivered" => "Entregue",
        "ReadyForPickup" => "Pronto para retirada", "Collected" => "Retirado",
        "Rejected" => "Rejeitado", "Cancelled" => "Cancelado", _ => status
    };

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    { if (EqualityComparer<T>.Default.Equals(field, value)) return; field = value; Notify(name); }
    private void Notify(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed record CompanyOrderItem(OrderSummaryDto Order, ICommand OpenCommand)
{
    public string Title => $"{Order.Protocol} · {CompanyOrdersViewModel.StatusLabel(Order.Status)}";
    public string Subtitle => $"{Order.RequestedDate:dd/MM/yyyy} · {(Order.FulfillmentType == "Delivery" ? "Entrega" : "Retirada")} · {Order.Total.ToString("C", CultureInfo.GetCultureInfo("pt-BR"))}";
}

public sealed record CompanyStatusAction(string Label, bool IsDestructive, ICommand Command);
