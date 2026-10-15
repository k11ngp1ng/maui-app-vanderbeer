using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Input;
using VanderBeer.App.Services;
using VanderBeer.App.Features.Shared;
using VanderBeer.App.Storage;
using VanderBeer.Contracts.Orders;
using VanderBeer.Contracts.Operations;

namespace VanderBeer.App.Features.Orders;

public sealed class OrdersViewModel(LocalOrderStore store, CustomerStore customers,
    CustomerRegistrationService registration, OrderApiClient api) : INotifyPropertyChanged
{
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly CultureInfo _br = CultureInfo.GetCultureInfo("pt-BR");
    private bool _busy;
    private string _message = "";
    private string _detail = "";
    private Guid? _selectedLocalId;
    private Guid? _selectedServerId;
    private OrderDto? _selectedOrder;
    private bool _canSendSelected, _canReviewSelected;
    public bool CanSendSelected => HasDetail && _canSendSelected;
    public bool CanReviewSelected => HasDetail && _canReviewSelected;
    public ICommand SendSelectedCommand => new Command(async () =>
    {
        if (!CanSendSelected || _selectedLocalId is not { } id) return;
        await SendAsync(id);
        await OpenAsync(id, null, fetchRemote: false);
    });
    public ICommand ReviewSelectedCommand => new Command(async () =>
    { if (CanReviewSelected && _selectedLocalId is { } id && ReviewRequested is not null) await ReviewRequested(id); });
    public bool HasDetail => !string.IsNullOrEmpty(Detail);
    public bool ShowList => !HasDetail;
    public bool CanReorderSelected => _selectedOrder is not null;
    public ObservableCollection<string> Progress { get; } = [];
    public ICommand CloseDetailCommand => new Command(CloseDetail);
    public ICommand ReorderSelectedCommand => new Command(async () =>
    { if (_selectedOrder is not null && ReorderRequested is not null) await ReorderRequested(_selectedOrder.Id); });
    public void CloseDetail()
    {
        _selectedLocalId = null;
        _selectedServerId = null;
        _selectedOrder = null;
        Progress.Clear();
        Detail = "";
    }
    public Task OpenSavedAsync(Guid localId) => OpenAsync(localId, null, fetchRemote: false);
    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<OrderListItem> Items { get; } = [];
    public Func<Guid, Task>? ReviewRequested { get; set; }
    public Func<Guid, Task>? ReorderRequested { get; set; }
    public bool IsBusy { get => _busy; private set => Set(ref _busy, value); }
    public string Message { get => _message; private set => Set(ref _message, value); }
    public string Detail { get => _detail; private set => Set(ref _detail, value); }
    public string BaseUrl { get; set; } = "";
    public ICommand RefreshCommand => new Command(async () => await RefreshAsync());

    public async Task ProcessPendingAsync()
    {
        if (!await _sendGate.WaitAsync(0)) return;
        try
        {
            // One worker processes the ordered queue. Reopening starts a new bounded cycle.
            var rows = await store.ListAsync();
            foreach (var row in rows.Where(x => x.SyncStatus is "Pending" or "Sending"))
                await store.ResetCycleAsync(Guid.Parse(row.LocalId));
            foreach (var row in rows.OrderBy(x => x.CreatedAtUtc))
            {
                if (row.SyncStatus is not ("Pending" or "Sending")) continue;
                var id = Guid.Parse(row.LocalId);
                for (var attempt = 0; attempt < 5; attempt++)
                {
                    if (Connectivity.Current.NetworkAccess == NetworkAccess.None) return;
                    var queued = await store.OutboxAsync(id);
                    if (queued is null || queued.Status is "Succeeded" or "RequiresReview") break;
                    if (queued.NextAttemptAtUtc is { } due)
                    {
                        var wait = DateTime.Parse(due, CultureInfo.InvariantCulture,
                            DateTimeStyles.RoundtripKind) - DateTime.UtcNow;
                        if (wait > TimeSpan.FromMinutes(1)) return;
                        if (wait > TimeSpan.Zero) await Task.Delay(wait);
                    }
                    if (await SendCoreAsync(id)) break;
                }
            }
            await LoadAsync();
            if (HasDetail) await OpenAsync(_selectedLocalId, _selectedServerId, fetchRemote: false);
        }
        catch (Exception) { Message = "Sincronização interrompida. Os pedidos permanecem salvos para reenvio."; }
        finally { _sendGate.Release(); }
    }

    public async Task LoadAsync()
    {
        Items.Clear();
        HashSet<Guid> shown = [];
        foreach (var row in await store.ListAsync())
        {
            var id = Guid.Parse(row.LocalId);
            var receipt = await store.ReceiptAsync(id);
            var lines = await store.ItemsAsync(id);
            var total = receipt?.Total ?? lines.Sum(x => x.ExpectedUnitPriceCents * x.Quantity) / 100m;
            if (receipt is not null) shown.Add(receipt.Id);
            Items.Add(new OrderListItem(receipt is null ? "Pedido aguardando confirmação" :
                $"Protocolo {receipt.Protocol}", receipt is null ? $"Envio: {SyncLabel(row.SyncStatus)}" :
                $"Envio: {SyncLabel(row.SyncStatus)} · Atendimento: {StatusLabel(receipt.Status)}",
                $"{OrderTimeFormat.InStoreZone(DateTime.Parse(row.CreatedAtUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind))} · {total.ToString("C", _br)}",
                receipt is null && row.SyncStatus != "RequiresReview",
                row.SyncStatus == "RequiresReview",
                new Command(async () => await OpenAsync(id, receipt?.Id)),
                new Command(async () => await SendAsync(id)),
                new Command(async () => { if (ReviewRequested is not null) await ReviewRequested(id); }),
                receipt is not null,
                new Command(async () => { if (receipt is not null && ReorderRequested is not null)
                    await ReorderRequested(receipt.Id); })));
        }
        foreach (var remote in await store.RemoteOrdersAsync())
        {
            if (!shown.Add(remote.Id)) continue;
            Items.Add(new OrderListItem($"Protocolo {remote.Protocol}",
                $"Envio: sincronizado · Atendimento: {StatusLabel(remote.Status)}",
                $"{OrderTimeFormat.InStoreZone(remote.CreatedAtUtc)} · {remote.Total.ToString("C", _br)}",
                false, false, new Command(async () => await OpenAsync(null, remote.Id)),
                new Command(() => { }), new Command(() => { }), true,
                new Command(async () => { if (ReorderRequested is not null)
                    await ReorderRequested(remote.Id); })));
        }
        Message = Items.Count == 0 ? "Nenhum pedido salvo. Escolha produtos no catálogo." : "";
    }

    public async Task RefreshAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            await LoadAsync();
            var identity = await customers.ReadAsync();
            if (identity?.Customer is null)
            {
                Message = "Confirme seu cadastro para consultar o histórico da empresa.";
                return;
            }
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var page = 1;
            long total;
            do
            {
                var result = await api.ListCustomerAsync(BaseUrl, identity.Customer.Id, page, timeout.Token);
                total = result.Total;
                foreach (var summary in result.Items)
                {
                    var detail = await api.GetAsync(BaseUrl, summary.Id, timeout.Token);
                    if (detail.CustomerId != identity.Customer.Id)
                        throw new InvalidDataException("Histórico retornado para outro cliente.");
                    await store.SaveRemoteAsync(detail);
                }
                page++;
            } while ((long)(page - 1) * 20 < total);
            await LoadAsync();
            if (_selectedLocalId is not null || _selectedServerId is not null)
                await OpenAsync(_selectedLocalId, _selectedServerId, fetchRemote: false);
            Message = "Histórico atualizado pela loja.";
        }
        catch (Exception)
        {
            Message = "Não foi possível atualizar pela loja. Os pedidos salvos continuam disponíveis.";
        }
        finally { IsBusy = false; }
    }

    private async Task OpenAsync(Guid? localId, Guid? serverId, bool fetchRemote = true)
    {
        try
        {
            _selectedLocalId = localId;
            _selectedServerId = serverId;
            OrderDto? order = localId is null ?
                (await store.RemoteOrdersAsync()).FirstOrDefault(x => x.Id == serverId) :
                await store.ReceiptAsync(localId.Value);
            if (fetchRemote && serverId is not null)
            {
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                    var current = await api.GetAsync(BaseUrl, serverId.Value, timeout.Token);
                    await store.SaveRemoteAsync(current);
                    order = localId is null ?
                        (await store.RemoteOrdersAsync()).FirstOrDefault(x => x.Id == serverId) :
                        await store.ReceiptAsync(localId.Value);
                    await LoadAsync();
                }
                catch (Exception) { Message = "Detalhe salvo no aparelho; atualização indisponível."; }
            }
            if (order is not null)
            {
                _canSendSelected = _canReviewSelected = false;
                _selectedOrder = order;
                Progress.Clear();
                foreach (var step in OrderProgress.Describe(order.Status, order.FulfillmentType == "Delivery")) Progress.Add(step);
                var sync = localId is null ? "sincronizado" :
                    SyncLabel((await store.ReadAsync(localId.Value))?.SyncStatus ?? "Pending");
                Detail = $"Protocolo {order.Protocol}\nEnvio: {sync}\n" +
                    $"Atendimento: {StatusLabel(order.Status)}\n" +
                    $"{(order.FulfillmentType == "Delivery" ? "Entrega" : "Retirada")} em {order.RequestedDate:dd/MM/yyyy}\n" +
                    $"Pagamento: {order.PaymentMethod.Name}" +
                    (order.CashTendered is null ? "" : $" · Valor para troco: {order.CashTendered.Value.ToString("C", _br)}") +
                    $"\nTotal confirmado: {order.Total.ToString("C", _br)}" +
                    (order.DeliveryAddress is null ? "" :
                        $"\nEndereço: {order.DeliveryAddress.Street}, {order.DeliveryAddress.Number} - {order.DeliveryAddress.District}, {order.DeliveryAddress.City}/{order.DeliveryAddress.State}") +
                    (order.Notes is null ? "" : $"\nObservações: {order.Notes}") + "\n" +
                    string.Join("\n", order.Items.Select(x =>
                        $"{x.Quantity} × {x.ProductName} · {x.UnitPrice.ToString("C", _br)}: {x.Subtotal.ToString("C", _br)}")) +
                    "\n" + string.Join("\n", order.StatusHistory.Select(x =>
                        $"{OrderTimeFormat.InStoreZone(x.OccurredAtUtc)}: {StatusLabel(x.ToStatus)}" +
                        (x.Reason is null ? "" : $" · {x.Reason}")));
            }
            else if (localId is not null)
            {
                _selectedOrder = null;
                Progress.Clear();
                foreach (var step in OrderProgress.Describe(null, true)) Progress.Add(step);
                var lines = await store.ItemsAsync(localId.Value);
                var row = await store.ReadAsync(localId.Value);
                _canReviewSelected = row?.SyncStatus == "RequiresReview";
                _canSendSelected = row is not null && !_canReviewSelected;
                var outbox = await store.OutboxAsync(localId.Value);
                var checkout = row is null ? null : JsonSerializer.Deserialize<CheckoutSnapshot>(row.CheckoutJson,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web));
                var problem = outbox?.LastErrorJson is null ? null :
                    JsonSerializer.Deserialize<ApiProblemDto>(outbox.LastErrorJson);
                var priceChanges = problem?.PriceChanges.Select(change => change is JsonElement element ?
                    JsonSerializer.Deserialize<PriceChangeDto>(element.GetRawText(),
                        new JsonSerializerOptions(JsonSerializerDefaults.Web)) : null)
                    .Where(change => change is not null).Select(change =>
                    {
                        var name = lines.FirstOrDefault(x => x.ProductId == change!.ProductId.ToString("D"))?.ProductName
                            ?? change!.ProductId.ToString("D");
                        return $"{name}: {change!.ExpectedUnitPrice.ToString("C", _br)} → " +
                            change.CurrentUnitPrice.ToString("C", _br);
                    }).ToArray() ?? [];
                Detail = "Pedido salvo neste aparelho.\n" +
                    $"Envio: {SyncLabel(row?.SyncStatus ?? "Pending")}\n" +
                    "Atendimento: aguardando recebimento pela empresa.\n" +
                    (checkout is null ? "" :
                        $"{(checkout.FulfillmentType == "Delivery" ? "Entrega" : "Retirada")} em {checkout.RequestedDate:dd/MM/yyyy}" +
                        $" · Pagamento: {checkout.PaymentName}\n") +
                    string.Join("\n", lines.Select(x =>
                        $"{x.Quantity} × {x.ProductName}: {(x.ExpectedUnitPriceCents * x.Quantity / 100m).ToString("C", _br)}")) +
                    $"\nTotal estimado: {(lines.Sum(x => x.ExpectedUnitPriceCents * x.Quantity) / 100m).ToString("C", _br)}" +
                    (problem is null ? "" : $"\nRevisão necessária: {problem.Detail}") +
                    (priceChanges.Length == 0 ? "" : "\nPreços alterados: " + string.Join("; ", priceChanges)) +
                    (problem?.CurrentTotal is null ? "" :
                        $"\nNovo total informado pela empresa: {problem.CurrentTotal.Value.ToString("C", _br)}");
            }
        }
        catch (Exception) { Message = "Não foi possível abrir o detalhe do pedido."; }
    }

    private async Task SendAsync(Guid id)
    {
        if (IsBusy) return;
        if (!await _sendGate.WaitAsync(0)) return;
        IsBusy = true;
        try
        {
            await store.ResetCycleAsync(id);
            var queued = await store.OutboxAsync(id);
            if (queued?.NextAttemptAtUtc is { } due && DateTime.Parse(due,
                CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind) > DateTime.UtcNow)
            {
                Message = "A loja pediu uma espera antes do próximo envio.";
                return;
            }
            await SendCoreAsync(id);
            await LoadAsync();
        }
        finally { IsBusy = false; _sendGate.Release(); }
    }

    private async Task<bool> SendCoreAsync(Guid id)
    {
        try
        {
            var identity = await customers.ReadAsync();
            var waiting = await store.OutboxAsync(id);
            var targetUrl = waiting?.BaseUrl ?? BaseUrl;
            if (waiting?.RequestJson is null && waiting?.DependsOnId is not null &&
                identity?.LastErrorCode is not null)
            {
                Message = "Revise o cadastro recusado antes de enviar este pedido.";
                return true;
            }
            if (waiting?.RequestJson is null &&
                (identity?.PendingRequest is not null || identity?.PendingUpdate is not null))
            {
                try
                {
                    using var customerTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    await registration.RetryPendingAsync(targetUrl, customerTimeout.Token);
                }
                catch (ApiException error) when (error.Problem is not null &&
                    error.Status is System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.Conflict)
                {
                    Message = "Revise o cadastro antes de enviar o pedido: " + error.Problem.Detail;
                    return true;
                }
                identity = await customers.ReadAsync();
            }
            if (identity?.Customer is null && waiting?.RequestJson is null)
            {
                Message = "Confirme o cadastro antes de enviar o pedido.";
                return true;
            }
            var outbox = await store.PrepareSendAsync(id, identity?.Customer);
            if (outbox.Status == "Succeeded") return true;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var result = await api.SendAsync(targetUrl, outbox.RequestPath!, outbox.RequestJson!, timeout.Token);
            await store.ConfirmAsync(id, result);
            Message = $"Pedido recebido pela empresa. Protocolo {result.Protocol}.";
            return true;
        }
        catch (ApiException error) when (error.Problem is not null &&
            error.Status is System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.NotFound or
                System.Net.HttpStatusCode.Conflict)
        {
            // A registration rejection has no frozen order request yet. Keep the order queued.
            var queued = await store.OutboxAsync(id);
            if (queued?.RequestJson is null)
                Message = "Revise o cadastro antes de enviar o pedido: " + error.Problem.Detail;
            else
            {
                await store.MarkReviewAsync(id, JsonSerializer.Serialize(error.Problem));
                Message = error.Problem.Code == "RequestKeyConflict"
                    ? "Chave de envio em conflito. A tentativa foi preservada para verificação."
                    : "O pedido precisa de revisão: " + error.Problem.Detail;
            }
            return true;
        }
        catch (Exception error)
        {
            var queued = await store.OutboxAsync(id);
            var attempts = queued?.CycleAttempts ?? 0;
            var seconds = attempts switch { <= 1 => 2, 2 => 5, 3 => 15, _ => 30 };
            var delay = TimeSpan.FromSeconds(seconds);
            if (error is ApiException { RetryAfter: { } retryAfter } && retryAfter > delay)
                delay = retryAfter;
            await store.MarkTransientAsync(id, delay);
            Message = error is InvalidOperationException ? error.Message :
                "Sem confirmação da loja. A mesma tentativa será reenviada quando possível.";
            return false;
        }
    }

    private static string SyncLabel(string code) => code switch
    {
        "Pending" => "pendente", "Sending" => "verificando envio",
        "Synchronized" => "sincronizado",
        "RequiresReview" => "revisão necessária", _ => code
    };
    private static string StatusLabel(string code) => code switch
    {
        "Received" => "Recebido", "Confirmed" => "Confirmado",
        "Preparing" => "Em preparo", "OutForDelivery" => "Saiu para entrega",
        "Delivered" => "Entregue", "ReadyForPickup" => "Pronto para retirada",
        "Collected" => "Retirado", "Rejected" => "Rejeitado",
        "Cancelled" => "Cancelado", _ => code
    };
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value; PropertyChanged?.Invoke(this, new(name));
        if (name == nameof(Detail))
            foreach (var changed in new[] { nameof(HasDetail), nameof(ShowList), nameof(CanReorderSelected), nameof(CanSendSelected), nameof(CanReviewSelected) })
                PropertyChanged?.Invoke(this, new(changed));
    }
}

public sealed record OrderListItem(string Title, string State, string Summary, bool CanSend,
    bool CanReview, ICommand OpenCommand, ICommand SendCommand, ICommand ReviewCommand,
    bool CanReorder, ICommand ReorderCommand);
