using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Text.Json;
using VanderBeer.App.Services;
using VanderBeer.Contracts.Customers;

namespace VanderBeer.App.Features.Customers;

public sealed class ProfileViewModel(CustomerRegistrationService service) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private bool _business, _hasAddress, _busy, _pending, _confirmed;
    private string _fullName = "", _companyName = "", _document = "", _phone = "", _email = "";
    private string _postalCode = "", _street = "", _number = "", _complement = "";
    private string _district = "", _city = "", _state = "", _reference = "";
    private string _status = "", _error = "";
    private CustomerDto? _customer;
    private string _section = "identity";
    private string _savedFingerprint = "";
    public Func<Task<bool>>? ConfirmDiscard { get; set; }
    private string Fingerprint => JsonSerializer.Serialize(new[] { IsBusiness.ToString(), FullName, CompanyName,
        Document, Phone, Email, HasAddress.ToString(), PostalCode, Street, Number, Complement, District, City, State, Reference });
    public bool HasUnsavedChanges => IsEditing && _savedFingerprint.Length > 0 && Fingerprint != _savedFingerprint;

    public bool HasLocalProfile => IsConfirmed || HasPending;
    public bool IsOverview => _section == "overview";
    public bool IsIdentityStep => _section == "identity";
    public bool IsContactStep => _section == "contact";
    public bool IsAddressStep => _section == "address";
    public bool IsEditing => !IsOverview;
    public bool CanGoBack => IsEditing && (!IsInitialRegistration || IsContactStep);
    public bool IsInitialRegistration => !HasLocalProfile;
    public string PageTitle => IsInitialRegistration ? "Bem-vindo à VanderBeer" : "Meu cadastro";
    public string StepTitle => _section switch
    {
        "identity" => IsInitialRegistration ? "Etapa 1 de 2 · Seus dados" : "Dados cadastrais",
        "contact" => IsInitialRegistration ? "Etapa 2 de 2 · Contato" : "Contato",
        "address" => "Endereço padrão",
        _ => "Seus dados para as próximas encomendas"
    };
    public string IdentitySummary => IsBusiness ? $"Pessoa jurídica · {CompanyName}\nResponsável: {FullName}" : $"Pessoa física · {FullName}";
    public string ContactSummary => Phone + (string.IsNullOrWhiteSpace(Email) ? "" : $"\n{Email}");
    public string AddressSummary => HasAddress ? $"{Street}, {Number}\n{District} · {City}/{State}\nCEP {PostalCode}" : "Nenhum endereço salvo. Você pode informá-lo ao escolher entrega.";
    public bool CanContinueIdentity => IsIdentityStep && IsInitialRegistration;
    public bool CanSaveSection => IsEditing && !CanContinueIdentity;
    public ICommand EditIdentityCommand => new Command(() => ShowSection("identity"));
    public ICommand EditContactCommand => new Command(() => ShowSection("contact"));
    public ICommand EditAddressCommand => new Command(() => ShowSection("address"));
    public ICommand ContinueCommand => new Command(() =>
    {
        Error = ValidateIdentity();
        if (Error.Length == 0) ShowSection("contact");
    });
    public ICommand PreviousStepCommand => new Command(async () =>
    {
        if (IsBusy) return;
        if (IsInitialRegistration) { ShowSection("identity"); return; }
        if (!HasUnsavedChanges || ConfirmDiscard is not null && await ConfirmDiscard()) await InitializeAsync();
    });

    public void ShowSection(string section)
    {
        _section = section;
        if (section == "overview") _savedFingerprint = Fingerprint;
        foreach (var name in new[] { nameof(IsOverview), nameof(IsIdentityStep), nameof(IsContactStep),
            nameof(IsAddressStep), nameof(IsEditing), nameof(StepTitle), nameof(CanContinueIdentity),
            nameof(CanSaveSection), nameof(PageTitle), nameof(IsInitialRegistration), nameof(IdentitySummary),
            nameof(ContactSummary), nameof(AddressSummary), nameof(CanGoBack) }) Notify(name);
    }

    public bool IsBusiness { get => _business; set { Set(ref _business, value); Notify(nameof(IsIndividual)); Notify(nameof(DocumentLabel)); Notify(nameof(NameLabel)); } }
    public bool IsIndividual => !IsBusiness;
    public string DocumentLabel => IsBusiness ? "CNPJ (obrigatório; 14 caracteres)" : "CPF (obrigatório; 11 dígitos)";
    public string NameLabel => IsBusiness ? "Nome do contato (obrigatório)" : "Nome completo (obrigatório)";
    public bool HasAddress { get => _hasAddress; set => Set(ref _hasAddress, value); }
    public string FullName { get => _fullName; set => Set(ref _fullName, value); }
    public string CompanyName { get => _companyName; set => Set(ref _companyName, value); }
    public string Document { get => _document; set => Set(ref _document, value); }
    public string Phone { get => _phone; set => Set(ref _phone, value); }
    public string Email { get => _email; set => Set(ref _email, value); }
    public string PostalCode { get => _postalCode; set => Set(ref _postalCode, value); }
    public string Street { get => _street; set => Set(ref _street, value); }
    public string Number { get => _number; set => Set(ref _number, value); }
    public string Complement { get => _complement; set => Set(ref _complement, value); }
    public string District { get => _district; set => Set(ref _district, value); }
    public string City { get => _city; set => Set(ref _city, value); }
    public string State { get => _state; set => Set(ref _state, value); }
    public string Reference { get => _reference; set => Set(ref _reference, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public string Error { get => _error; private set => Set(ref _error, value); }
    public bool IsBusy { get => _busy; private set => Set(ref _busy, value); }
    public bool HasPending { get => _pending; private set => Set(ref _pending, value); }
    public bool IsConfirmed { get => _confirmed; private set => Set(ref _confirmed, value); }
    public bool CanSave => !IsBusy && !HasPending;
    public ICommand IndividualCommand => new Command(() => IsBusiness = false);
    public ICommand BusinessCommand => new Command(() => IsBusiness = true);

    public async Task InitializeAsync()
    {
        var identity = await service.ReadAsync();
        _customer = identity?.Customer;
        IsConfirmed = _customer is not null;
        HasPending = identity?.PendingRequest is not null || identity?.PendingUpdate is not null;
        var value = identity?.PendingRequest ?? identity?.Draft;
        if (value is null && _customer is { } c)
            value = new(Guid.Empty, c.CustomerType, c.FullName, c.CompanyName, c.Cpf, c.Cnpj,
                c.Phone, c.Email, c.Address);
        if (value is not null) Fill(value);
        _savedFingerprint = Fingerprint;
        ShowSection(HasLocalProfile ? "overview" : "identity");
        Status = HasPending ? "Cadastro salvo neste aparelho; envio pendente. Use Reenviar cadastro."
            : IsConfirmed ? "Cadastro confirmado pela empresa e salvo neste aparelho."
            : "Preencha os dados para cadastrar este aparelho.";
        Error = identity?.LastErrorCode switch
        {
            "DuplicateCpf" or "DuplicateCnpj" => "Documento já cadastrado. Confira o número ou peça ajuda à empresa.",
            "CustomerVersionConflict" => "O cadastro mudou no servidor. Revise os dados antes de tentar novamente.",
            "ValidationFailed" => "Revise os dados informados antes de tentar novamente.",
            _ => ""
        };
    }

    public async Task SaveAsync(string baseUrl)
    {
        if (!CanSave) return;
        Error = Validate();
        if (Error.Length > 0) return;
        var request = BuildRequest();
        IsBusy = true;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            _customer = _customer is null
                ? await service.CreateAsync(baseUrl, request, timeout.Token)
                : await service.UpdateAsync(baseUrl, new(request.ClientRequestId, _customer.Version,
                    request.CustomerType, request.FullName, request.CompanyName, request.Cpf,
                    request.Cnpj, request.Phone, request.Email, request.Address), timeout.Token);
            IsConfirmed = true;
            HasPending = false;
            Status = "Cadastro confirmado pela empresa e salvo neste aparelho.";
            Error = "";
        }
        catch (Exception)
        {
            await ReloadStateAsync();
        }
        finally { IsBusy = false; }
    }

    public async Task RetryAsync(string baseUrl)
    {
        if (IsBusy || !HasPending) return;
        IsBusy = true;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            _customer = await service.RetryPendingAsync(baseUrl, timeout.Token);
            IsConfirmed = true;
            HasPending = false;
            Status = "Cadastro confirmado pela empresa e salvo neste aparelho.";
            Error = "";
        }
        catch (Exception) { await ReloadStateAsync(); }
        finally { IsBusy = false; }
    }

    private async Task ReloadStateAsync()
    {
        try
        {
            var identity = await service.ReadAsync();
            HasPending = identity?.PendingRequest is not null || identity?.PendingUpdate is not null;
            Status = HasPending ? "Envio sem confirmação. Os dados estão salvos neste aparelho; toque em Reenviar cadastro."
                : "Cadastro não confirmado. Corrija os dados e salve novamente.";
            Error = identity?.LastErrorCode switch
            {
                "DuplicateCpf" or "DuplicateCnpj" => "Documento já cadastrado. Confira o número ou peça ajuda à empresa.",
                "CustomerVersionConflict" => "O cadastro mudou no servidor. Revise os dados com a empresa antes de salvar novamente.",
                "ValidationFailed" => "Dados recusados pela loja. Confira todos os campos.",
                _ => HasPending ? "Confira a conexão com a loja." : "Não foi possível salvar o cadastro."
            };
        }
        catch (Exception) { Error = "Não foi possível ler o cadastro local. Tente novamente."; }
    }

    private string Validate()
    {
        var identityError = ValidateIdentity();
        if (identityError.Length > 0) { ShowSection("identity"); return identityError; }
        var phone = Digits(Phone);
        if (phone.Length is not (10 or 11)) { ShowSection("contact"); return "Informe telefone com DDD, com 10 ou 11 dígitos."; }
        if (Email.Trim().Length > 0 && !System.Net.Mail.MailAddress.TryCreate(Email.Trim(), out _))
        { ShowSection("contact"); return "Informe um e-mail válido."; }
        if (HasAddress && (Digits(PostalCode).Length != 8 || Street.Trim().Length < 2 ||
            Number.Trim().Length == 0 || District.Trim().Length < 2 || City.Trim().Length < 2 || State.Trim().Length != 2))
        { ShowSection("address"); return "Complete o endereço: CEP, rua, número, bairro, cidade e UF."; }
        return "";
    }

    private string ValidateIdentity()
    {
        if (FullName.Trim().Length is < 3 or > 120) return "Informe um nome de 3 a 120 caracteres.";
        if (IsBusiness && CompanyName.Trim().Length is < 3 or > 120) return "Informe a razão social de 3 a 120 caracteres.";
        return NormalizeDocument(Document).Length == (IsBusiness ? 14 : 11) ? "" : $"Informe {(IsBusiness ? "CNPJ" : "CPF")} completo.";
    }

    private CustomerWriteRequest BuildRequest() => new(Guid.NewGuid(), IsBusiness ? "Business" : "Individual",
        FullName.Trim(), IsBusiness ? CompanyName.Trim() : null,
        IsBusiness ? null : NormalizeDocument(Document), IsBusiness ? NormalizeDocument(Document) : null,
        Digits(Phone), Optional(Email), HasAddress ? new(Digits(PostalCode), Street.Trim(), Number.Trim(),
            Optional(Complement), District.Trim(), City.Trim(), State.Trim().ToUpperInvariant(), Optional(Reference)) : null);

    private void Fill(CustomerWriteRequest value)
    {
        IsBusiness = value.CustomerType == "Business";
        FullName = value.FullName; CompanyName = value.CompanyName ?? "";
        Document = value.Cnpj ?? value.Cpf ?? ""; Phone = value.Phone; Email = value.Email ?? "";
        HasAddress = value.Address is not null;
        PostalCode = value.Address?.PostalCode ?? ""; Street = value.Address?.Street ?? "";
        Number = value.Address?.Number ?? ""; Complement = value.Address?.Complement ?? "";
        District = value.Address?.District ?? ""; City = value.Address?.City ?? "";
        State = value.Address?.State ?? ""; Reference = value.Address?.Reference ?? "";
    }

    private static string Digits(string text) => new(text.Where(char.IsDigit).ToArray());
    private static string NormalizeDocument(string text) =>
        new(text.ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
    private static string? Optional(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value; Notify(name);
        if (name is nameof(IsBusy) or nameof(HasPending)) Notify(nameof(CanSave));
        if (name is nameof(IsConfirmed) or nameof(HasPending))
        {
            Notify(nameof(HasLocalProfile)); Notify(nameof(IsInitialRegistration)); Notify(nameof(PageTitle));
        }
    }
    private void Notify(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
