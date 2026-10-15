using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Input;
using VanderBeer.App.Services;
using VanderBeer.App.Storage;
using VanderBeer.App.Features.Shared;
using VanderBeer.Contracts.Catalog;
using VanderBeer.Contracts.Customers;

namespace VanderBeer.App.Features.Checkout;

public sealed class CheckoutViewModel(CheckoutReferenceService references, CustomerStore customers,
    CartStore cart, LocalOrderStore orders) : INotifyPropertyChanged
{
    private readonly CultureInfo _br = CultureInfo.GetCultureInfo("pt-BR");
    private StoreSettingsDto? _settings;
    private CustomerIdentity? _identity;
    private bool _busy, _delivery = true, _cached;
    private PaymentMethodDto? _payment;
    private DateTime _requestedDate = DateTime.Today;
    private string _cash = "", _notes = "", _error = "", _status = "";
    private string _baseUrl = "";
    private Guid? _reviewOrderId;
    private HashSet<Guid> _unavailableReviewItems = [];
    private string _postalCode = "", _street = "", _number = "", _complement = "";
    private string _district = "", _city = "", _state = "", _reference = "";
    private int _step;
    private string _draftFingerprint = "";
    public bool IsReceivingStep => _step == 0;
    public bool IsPaymentStep => _step == 1;
    public bool IsReviewStep => _step == 2;
    public bool IsBeforeReview => !IsReviewStep;
    public bool CanEdit => !IsBusy;
    public bool CanAdvance => !IsBusy && _settings is not null && !IsReviewStep;
    public bool CanSave => !IsBusy && _settings is not null && IsReviewStep && SavedOrderId is null;
    public string StepTitle => $"Etapa {_step + 1} de 3 · " + (_step switch { 0 => "Recebimento", 1 => "Pagamento", _ => "Revisão" });
    public string NextLabel => IsReceivingStep ? "Continuar para pagamento" : "Revisar pedido";
    public string ReceivingSummary => $"{(IsDelivery ? "Entrega" : "Retirada na loja")} · {RequestedDate:dd/MM/yyyy}" +
        (IsDelivery ? $"\n{Street}, {Number} {Complement}\n{District} · {City}/{State} · CEP {PostalCode}\n{Reference}" : "");
    public string PaymentSummary => (SelectedPayment?.Name ?? "Selecione o pagamento") +
        (IsCash ? string.IsNullOrWhiteSpace(CashTendered) ? " · Valor exato" : $" · Troco para R$ {CashTendered}" : "") +
        "\nPagamento no atendimento.";
    public ICommand NextStepCommand => new Command(async () =>
    {
        if (!CanAdvance) return;
        if (IsReceivingStep)
        {
            Error = ReceivingError();
            if (Error.Length > 0) return;
        }
        else if (!await ValidateAsync()) return;
        ShowStep(_step + 1);
    });
    public ICommand EditReceivingCommand => new Command(() => ShowStep(0));
    public ICommand EditPaymentCommand => new Command(() => ShowStep(1));
    public bool PreviousStep()
    {
        if (IsBusy || _step == 0) return false;
        ShowStep(_step - 1);
        return true;
    }
    private void ShowStep(int step)
    {
        _step = step;
        foreach (var name in new[] { nameof(IsReceivingStep), nameof(IsPaymentStep), nameof(IsReviewStep),
            nameof(StepTitle), nameof(NextLabel), nameof(CanAdvance), nameof(CanSave), nameof(IsBeforeReview),
            nameof(ReceivingSummary), nameof(PaymentSummary) }) Notify(name);
    }

    public async Task ResumeAsync(string baseUrl)
    {
        var reviewId = baseUrl == _baseUrl ? _reviewOrderId : null;
        var unavailableItems = _unavailableReviewItems;
        try
        {
            var fingerprint = JsonSerializer.Serialize(new { BaseUrl = baseUrl, Items = await cart.ListAsync(), Identity = await customers.ReadAsync() });
            if (_settings is not null && SavedOrderId is null && fingerprint == _draftFingerprint) return;
        }
        catch (Exception) { /* LoadAsync presents storage failures to the user. */ }
        await LoadAsync(baseUrl);
        if (reviewId is not null)
        {
            // Removing an unavailable cart item must not turn a review into a separate order.
            _reviewOrderId = reviewId;
            _unavailableReviewItems = unavailableItems;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<PaymentMethodDto> Payments { get; } = [];
    public bool IsBusy { get => _busy; private set => Set(ref _busy, value); }
    public bool IsDelivery { get => _delivery; set { Set(ref _delivery, value); Notify(nameof(IsPickup)); Notify(nameof(CanEnterAddress)); } }
    public bool IsPickup => !IsDelivery;
    public bool CanEnterAddress => IsDelivery;
    public bool IsCash => SelectedPayment?.Code == "Cash";
    public PaymentMethodDto? SelectedPayment { get => _payment; set { Set(ref _payment, value); if (!IsCash) CashTendered = ""; Notify(nameof(IsCash)); } }
    public DateTime RequestedDate { get => _requestedDate; set => Set(ref _requestedDate, value); }
    public DateTime MinDate => _settings?.MinRequestedDate.ToDateTime(TimeOnly.MinValue) ?? DateTime.Today;
    public DateTime MaxDate => _settings?.MaxRequestedDate.ToDateTime(TimeOnly.MinValue) ?? DateTime.Today.AddDays(30);
    public string CashTendered { get => _cash; set => Set(ref _cash, value); }
    public string Notes { get => _notes; set => Set(ref _notes, value); }
    public string PostalCode { get => _postalCode; set => Set(ref _postalCode, value); }
    public string Street { get => _street; set => Set(ref _street, value); }
    public string Number { get => _number; set => Set(ref _number, value); }
    public string Complement { get => _complement; set => Set(ref _complement, value); }
    public string District { get => _district; set => Set(ref _district, value); }
    public string City { get => _city; set => Set(ref _city, value); }
    public string State { get => _state; set => Set(ref _state, value); }
    public string Reference { get => _reference; set => Set(ref _reference, value); }
    public string Error { get => _error; private set => Set(ref _error, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public string ReferenceNotice => _cached ? "Opções salvas; confira preços e disponibilidade ao enviar." : "Opções atualizadas pela loja.";
    public string Summary { get; private set; } = "";
    public Guid? SavedOrderId { get; private set; }

    public async Task LoadAsync(string baseUrl)
    {
        _reviewOrderId = null;
        _unavailableReviewItems = [];
        _baseUrl = baseUrl;
        if (IsBusy) return;
        IsBusy = true;
        Error = "";
        SavedOrderId = null;
        _settings = null;
        ShowStep(0);
        IsDelivery = true;
        CashTendered = Notes = "";
        PostalCode = Street = Number = Complement = District = City = State = Reference = "";
        try
        {
            _identity = await customers.ReadAsync();
            if (_identity?.Customer is null && _identity?.PendingRequest is null)
                throw new InvalidOperationException("Salve seu cadastro antes de finalizar.");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var result = await references.LoadAsync(baseUrl, timeout.Token);
            _settings = result.Settings;
            _cached = result.Cached;
            Payments.Clear();
            foreach (var payment in result.Payments) Payments.Add(payment);
            SelectedPayment = Payments.FirstOrDefault();
            RequestedDate = MinDate;
            Notify(nameof(MinDate)); Notify(nameof(MaxDate)); Notify(nameof(ReferenceNotice));
            var address = _identity.Draft?.Address ?? _identity.Customer?.Address;
            if (address is not null)
            {
                PostalCode = address.PostalCode; Street = address.Street; Number = address.Number;
                Complement = address.Complement ?? ""; District = address.District; City = address.City;
                State = address.State; Reference = address.Reference ?? "";
            }
            var items = await cart.ListAsync();
            if (items.Count == 0) throw new InvalidOperationException("Adicione um produto ao carrinho antes de finalizar.");
            var customer = _identity.Customer;
            var profileSummary = customer is null ? "Cadastro pendente de confirmação" :
                $"Cliente: {customer.FullName}";
            Summary = profileSummary + "\n" +
                string.Join("\n", items.Select(x => $"{x.Quantity} × {x.ProductName}: {x.Subtotal.ToString("C", _br)}"))
                + $"\nTotal estimado: {items.Sum(x => x.Subtotal).ToString("C", _br)}";
            Notify(nameof(Summary));
            _draftFingerprint = JsonSerializer.Serialize(new { BaseUrl = baseUrl, Items = items, Identity = _identity });
            ShowStep(0);
            Status = "Revise os dados e registre o pedido neste aparelho.";
        }
        catch (Exception error)
        {
            _settings = null;
            Error = error is InvalidOperationException ? error.Message : "Não foi possível carregar a finalização.";
        }
        finally { IsBusy = false; }
    }

    public async Task<bool> LoadReviewAsync(string baseUrl, Guid id, CheckoutSnapshot previous,
        IReadOnlyList<Guid> unavailableIds, bool profileChanged = false)
    {
        await LoadAsync(baseUrl);
        if (_settings is null) return false;
        _reviewOrderId = id;
        _unavailableReviewItems = unavailableIds.ToHashSet();
        IsDelivery = previous.FulfillmentType == "Delivery";
        RequestedDate = previous.RequestedDate < _settings.MinRequestedDate ||
            previous.RequestedDate > _settings.MaxRequestedDate ? MinDate :
            previous.RequestedDate.ToDateTime(TimeOnly.MinValue);
        SelectedPayment = Payments.FirstOrDefault(x => x.Id == previous.PaymentMethodId) ?? Payments.FirstOrDefault();
        CashTendered = previous.CashTendered?.ToString("0.00", _br) ?? "";
        Notes = previous.Notes ?? "";
        if (previous.DeliveryAddress is { } address)
        {
            PostalCode = address.PostalCode; Street = address.Street; Number = address.Number;
            Complement = address.Complement ?? ""; District = address.District; City = address.City;
            State = address.State; Reference = address.Reference ?? "";
        }
        Status = profileChanged
            ? "Cadastro atualizado pela loja. Confira nome, telefone e dados do pedido acima antes de registrar a nova tentativa."
            : unavailableIds.Count == 0
            ? "Revise os preços atualizados, a data e o pagamento antes de confirmar uma nova tentativa."
            : "Há produto indisponível no carrinho. Remova-o antes de confirmar a revisão.";
        return true;
    }

    public async Task<bool> ValidateAsync() => await PrepareAsync() is not null;

    private string ReceivingError()
    {
        var date = DateOnly.FromDateTime(RequestedDate);
        if (_settings is null) return "Não foi possível carregar as opções da loja. Volte ao carrinho e tente novamente.";
        if (date < _settings.MinRequestedDate || date > _settings.MaxRequestedDate)
            return "Escolha uma data dentro da janela informada pela loja.";
        if (IsDelivery && (new string(PostalCode.Where(char.IsDigit).ToArray()).Length != 8 ||
            Street.Trim().Length is < 2 or > 120 || Number.Trim().Length is < 1 or > 20 ||
            District.Trim().Length is < 2 or > 80 || City.Trim().Length is < 2 or > 80 || State.Trim().Length != 2 ||
            (Empty(Complement)?.Length ?? 0) > 80 || (Empty(Reference)?.Length ?? 0) > 160))
            return "Complete CEP, rua, número, bairro, cidade e UF para entrega.";
        return "";
    }

    public async Task SaveAsync()
    {
        if (!CanSave) return;
        var snapshot = await PrepareAsync();
        if (snapshot is null) return;
        IsBusy = true;
        try
        {
            if (_reviewOrderId is { } reviewId)
            {
                await orders.ReviseAsync(reviewId, snapshot);
                SavedOrderId = reviewId;
                _reviewOrderId = null;
            }
            else SavedOrderId = await orders.SaveAsync(snapshot);
            Status = "Pedido salvo — aguardando envio. A empresa ainda não recebeu esta encomenda.";
        }
        catch (Exception error)
        {
            Error = error is InvalidOperationException ? error.Message :
                "Não foi possível salvar o pedido local. O carrinho foi preservado.";
        }
        finally { IsBusy = false; }
    }

    private async Task<CheckoutSnapshot?> PrepareAsync()
    {
        if (IsBusy || _settings is null || _identity is null ||
            (_identity.Customer is null && _identity.PendingRequest is null) || SavedOrderId is not null) return null;
        Error = "";
        try
        {
            var date = DateOnly.FromDateTime(RequestedDate);
            var receivingError = ReceivingError();
            if (receivingError.Length > 0) { ShowStep(0); throw new InvalidOperationException(receivingError); }
            if (SelectedPayment is null || !Payments.Any(x => x.Id == SelectedPayment.Id))
                throw new InvalidOperationException("Escolha uma forma de pagamento.");
            if (Notes.Trim().Length > 500)
                throw new InvalidOperationException("Observações devem ter até 500 caracteres.");
            decimal? tendered = null;
            if (IsCash && CashTendered.Trim().Length > 0)
            {
                if (!MoneyInput.TryParse(CashTendered, out var value) || value > 999999.99m)
                    throw new InvalidOperationException("Informe um valor válido em reais e centavos.");
                tendered = value;
            }
            AddressDto? address = null;
            if (IsDelivery)
            {
                var cep = new string(PostalCode.Where(char.IsDigit).ToArray());
                if (cep.Length != 8 || Street.Trim().Length is < 2 or > 120 ||
                    Number.Trim().Length is < 1 or > 20 || District.Trim().Length is < 2 or > 80 ||
                    City.Trim().Length is < 2 or > 80 || State.Trim().Length != 2 ||
                    (Empty(Complement)?.Length ?? 0) > 80 || (Empty(Reference)?.Length ?? 0) > 160)
                    throw new InvalidOperationException("Complete CEP, rua, número, bairro, cidade e UF para entrega.");
                address = new(cep, Street.Trim(), Number.Trim(), Empty(Complement), District.Trim(),
                    City.Trim(), State.Trim().ToUpperInvariant(), Empty(Reference));
            }
            var items = await cart.ListAsync();
            if (items.Count == 0) throw new InvalidOperationException("O carrinho está vazio.");
            if (items.Any(x => _unavailableReviewItems.Contains(x.ProductId)))
                throw new InvalidOperationException("Remova os produtos indisponíveis antes de confirmar a revisão.");
            if (tendered is not null && tendered < items.Sum(x => x.Subtotal))
                throw new InvalidOperationException("O valor em dinheiro é menor que o total estimado.");
            return new(IsDelivery ? "Delivery" : "Pickup", date,
                SelectedPayment.Id, SelectedPayment.Code, SelectedPayment.Name, tendered, address,
                Empty(Notes), _identity.Customer?.Id ?? Guid.Empty, _identity.Customer?.Version ?? 0,
                _identity.PendingUpdate?.ClientRequestId ?? _identity.PendingRequest?.ClientRequestId,
                _baseUrl);
        }
        catch (Exception error)
        {
            Error = error is InvalidOperationException ? error.Message : "Não foi possível validar o carrinho local.";
            return null;
        }
    }

    private static string? Empty(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value; Notify(name);
        if (name == nameof(IsBusy)) { Notify(nameof(CanEdit)); Notify(nameof(CanAdvance)); Notify(nameof(CanSave)); }
    }
    private void Notify(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
