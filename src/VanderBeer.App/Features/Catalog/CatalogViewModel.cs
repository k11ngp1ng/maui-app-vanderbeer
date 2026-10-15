using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Input;
using VanderBeer.App.Services;
using VanderBeer.App.Storage;
using VanderBeer.App.Features.Customers;
using VanderBeer.App.Features.Cart;
using VanderBeer.App.Features.Checkout;
using VanderBeer.App.Features.Orders;
using VanderBeer.App.Features.Shared;
using VanderBeer.Contracts.Catalog;
using VanderBeer.Contracts.Orders;
using VanderBeer.Contracts.Operations;

namespace VanderBeer.App.Features.Catalog;

public sealed class CatalogViewModel(ProductApiClient api, CatalogStore store, ProfileViewModel profile,
    CartViewModel cart, CheckoutViewModel checkout, OrdersViewModel orders,
    CompanyOrdersViewModel companyOrders,
    LocalOrderStore localOrders, OrderApiClient orderApi, CartStore cartStore,
    CustomerStore customerStore, CustomerApiClient customerApi) : INotifyPropertyChanged
{
    private readonly CultureInfo _br = CultureInfo.GetCultureInfo("pt-BR");
    private bool _company;
    private bool _apiAddressLoaded;
    private string _screen = "loading";
    private bool _busy;
    private int _page = 1;
    private int _total;
    private string _search = "";
    private string _clientSearch = "", _companySearch = "";
    private Guid? _clientCategoryId, _companyCategoryId;
    private CategoryDto? _selectedCategory;
    private string _activeFilter = "Ativos";
    private string _companyActiveFilter = "Todos";
    private string _status = "Carregando catálogo...";
    private string _error = "";
    private ProductDto? _selectedProduct;
    private string _detailQuantity = "1";
    private bool _pending;
    private bool _reviewRequired;
    private ProductDto? _currentProduct;
    private EditorSnapshot? _openedEditor;
    private List<ReorderEntry> _reorderEntries = [];
    private string _reorderSource = "";
    private string _reorderError = "";
    private bool _reorderBusy;

    private string _sku = "", _name = "", _description = "", _unitLabel = "", _price = "", _imageUrl = "";
    private CategoryDto? _editCategory;
    private bool _isActive = true, _isAvailable = true;
    private string _skuError = "", _categoryError = "", _priceError = "";
    private string _nameError = "", _unitError = "", _descriptionError = "", _imageUrlError = "";

    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<ProductItem> Products { get; } = [];
    public ObservableCollection<ReorderLineViewModel> ReorderLines { get; } = [];
    public ObservableCollection<CategoryDto> Categories { get; } = [];
    public IReadOnlyList<string> ActiveFilters { get; } = ["Ativos", "Inativos", "Todos"];
    public ProfileViewModel Profile => profile;
    public CartViewModel Cart => cart;
    public CheckoutViewModel Checkout => checkout;
    public OrdersViewModel Orders => orders;
    public CompanyOrdersViewModel CompanyOrders => companyOrders;

    private string _apiAddress = DeviceInfo.Platform == DevicePlatform.Android
        ? "http://10.0.2.2:5080" : "http://127.0.0.1:5080";
    public string ApiAddress { get => _apiAddress; set => Set(ref _apiAddress, value); }
    public async Task SaveConnectionAsync(string address)
    {
        try { await store.SaveApiAddressAsync(address); ApiAddress = address; }
        catch (Exception) { Error = "Não foi possível salvar a configuração de conexão."; }
    }
    public bool IsCompany => _company;
    public bool IsClient => !_company;
    public bool IsList => _screen == "list";
    public bool IsInitializing => _screen == "loading";
    public bool IsDetail => _screen == "detail";
    public bool IsEditor => _screen == "editor";
    public bool IsProfile => _screen == "profile";
    public bool IsCart => _screen == "cart";
    public bool IsCheckout => _screen == "checkout";
    public bool IsLocalOrder => _screen == "local-order";
    public bool IsOrders => _screen == "orders";
    public bool IsReorder => _screen == "reorder";
    public bool IsCompanyOrders => _screen == "company-orders";
    public bool ShowClientNavigation => IsClient && profile.HasLocalProfile && !IsCheckout && !IsReorder;
    public bool ShowCompanyNavigation => IsCompany && !IsEditor;
    public bool ShowCatalogFeedback => IsList || IsDetail || IsEditor;
    public string AreaTitle => IsCompany ? "Gestão VanderBeer" : "VanderBeer";
    public string AreaSubtitle => IsCompany ? "Ambiente de demonstração · Atendimento e produtos" : "Escolha suas bebidas e acompanhe suas encomendas";
    public string CartTabTitle => cart.IsEmpty ? "Carrinho" : $"Carrinho ({cart.Items.Count})";
    public bool IsCatalogSelected => IsClient && (IsList || IsDetail);
    public bool IsCartSelected => IsCart;
    public bool IsOrdersSelected => IsOrders || IsLocalOrder;
    public bool IsProfileSelected => IsProfile;
    public bool IsProductsSelected => IsCompany && (IsList || IsDetail);
    public bool IsCompanyOrdersSelected => IsCompanyOrders;
    public bool IsCatalogEmpty => Products.Count == 0 && !IsBusy;
    public bool ShowPendingProduct => IsCompany && HasPending;
    public string AddToCartLabel => int.TryParse(DetailQuantity, out var quantity) && quantity is >= 1 and <= 999 && _selectedProduct is not null
        ? $"Adicionar • {(quantity * _selectedProduct.Price).ToString("C", _br)}" : "Adicionar ao carrinho";
    public bool IsReorderBusy { get => _reorderBusy; private set
        { Set(ref _reorderBusy, value); Notify(nameof(CanConfirmReorder)); } }
    public string ReorderSource { get => _reorderSource; private set => Set(ref _reorderSource, value); }
    public string ReorderError { get => _reorderError; private set => Set(ref _reorderError, value); }
    public bool CanConfirmReorder => !IsReorderBusy && _reorderEntries.Count > 0 &&
        _reorderEntries.All(x => x.Product is { IsActive: true, IsAvailable: true });
    public string ReorderTotal => _reorderEntries.Where(x => x.Product is { IsActive: true, IsAvailable: true })
        .Sum(x => x.Product!.Price * x.Original.Quantity).ToString("C", _br);
    public string LocalOrderId => checkout.SavedOrderId?.ToString("D") ?? "";
    public bool IsBusy { get => _busy; private set { Set(ref _busy, value); Notify(nameof(IsCatalogEmpty)); } }
    public bool HasPending { get => _pending; private set { Set(ref _pending, value); Notify(nameof(ShowPendingProduct)); } }
    public bool NeedsReview { get => _reviewRequired; private set => Set(ref _reviewRequired, value); }
    public string Search { get => _search; set => Set(ref _search, value); }
    public CategoryDto? SelectedCategory { get => _selectedCategory; set => Set(ref _selectedCategory, value); }
    public string ActiveFilter { get => _activeFilter; set => Set(ref _activeFilter, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public string Error { get => _error; private set => Set(ref _error, value); }
    public string PageText => $"Página {_page} · {_total} produto(s)";
    public bool CanPrevious => _page > 1;
    public bool CanNext => _page * 20 < _total;
    public string DetailName => _selectedProduct?.Name ?? "";
    public string DetailSku => _selectedProduct?.Sku ?? "";
    public string DetailDescription => _selectedProduct?.Description ?? "Sem descrição.";
    public string DetailUnit => _selectedProduct?.UnitLabel ?? "";
    public string DetailPrice => _selectedProduct?.Price.ToString("C", _br) ?? "";
    public string DetailAvailability => _selectedProduct is null ? "" :
        _selectedProduct.IsAvailable ? "Disponível" : "Indisponível";
    public string? DetailImage => _selectedProduct?.ImageUrl;
    public bool HasDetailImage => !string.IsNullOrWhiteSpace(_selectedProduct?.ImageUrl);
    public bool NoDetailImage => !HasDetailImage;
    public bool CanAddToCart => IsClient && _selectedProduct is { IsActive: true, IsAvailable: true };
    public bool IsUnavailableForCart => IsClient && _selectedProduct is not { IsActive: true, IsAvailable: true };
    public string DetailQuantity { get => _detailQuantity; set { Set(ref _detailQuantity, value); Notify(nameof(AddToCartLabel)); } }
    public string EditorTitle => _selectedProduct is null ? "Novo produto" : "Editar produto";
    public string Sku { get => _sku; set => Set(ref _sku, value); }
    public string Name { get => _name; set => Set(ref _name, value); }
    public string Description { get => _description; set => Set(ref _description, value); }
    public string UnitLabel { get => _unitLabel; set => Set(ref _unitLabel, value); }
    public string Price { get => _price; set => Set(ref _price, value); }
    public string ImageUrl { get => _imageUrl; set => Set(ref _imageUrl, value); }
    public CategoryDto? EditCategory { get => _editCategory; set => Set(ref _editCategory, value); }
    public bool IsActive { get => _isActive; set => Set(ref _isActive, value); }
    public bool IsAvailable { get => _isAvailable; set => Set(ref _isAvailable, value); }
    public string SkuError { get => _skuError; private set => Set(ref _skuError, value); }
    public string CategoryError { get => _categoryError; private set => Set(ref _categoryError, value); }
    public string PriceError { get => _priceError; private set => Set(ref _priceError, value); }
    public string NameError { get => _nameError; private set => Set(ref _nameError, value); }
    public string UnitError { get => _unitError; private set => Set(ref _unitError, value); }
    public string DescriptionError { get => _descriptionError; private set => Set(ref _descriptionError, value); }
    public string ImageUrlError { get => _imageUrlError; private set => Set(ref _imageUrlError, value); }

    public ICommand ClientCommand => new Command(async () => await SwitchAsync(false));
    public ICommand CompanyCommand => new Command(async () => await SwitchAsync(true));
    public ICommand RefreshCommand => new Command(async () => await RefreshAsync(true));
    public ICommand SearchCommand => new Command(async () => { _page = 1; await RefreshAsync(true); });
    public ICommand SelectCategoryCommand => new Command<CategoryDto>(async category =>
    {
        if (IsBusy || category is null) return;
        SelectedCategory = category;
        _page = 1;
        await RefreshAsync(false);
    });
    public ICommand NextCommand => new Command(async () => { if (CanNext) { _page++; await RefreshAsync(false); } });
    public ICommand PreviousCommand => new Command(async () => { if (CanPrevious) { _page--; await RefreshAsync(false); } });
    public ICommand BackCommand => new Command(async () => { Screen("list"); await RefreshAsync(false); });
    public ICommand NewCommand => new Command(() => OpenEditor(null));
    public ICommand EditCommand => new Command(() => OpenEditor(_selectedProduct));
    public ICommand SaveCommand => new Command(async () => await SaveAsync());
    public ICommand RecoverCommand => new Command(async () => await RecoverAsync());
    public ICommand CatalogCommand => new Command(async () =>
    { if (!await CanLeaveProfileAsync()) return; if (!profile.HasLocalProfile) { Screen("profile"); return; } Screen("list"); await RefreshAsync(true); });
    public ICommand OpenProfileCommand => new Command(async () =>
    { if (IsProfile) return; await profile.InitializeAsync(); Screen("profile"); });
    public ICommand SaveProfileCommand => new Command(async () =>
    {
        var initial = !profile.HasLocalProfile;
        await store.SaveApiAddressAsync(ApiAddress);
        await profile.SaveAsync(ApiAddress);
        if (profile.HasLocalProfile && (profile.Error.Length == 0 || profile.HasPending))
        {
            profile.ShowSection("overview");
            if (initial) { Screen("list"); await RefreshAsync(true); }
        }
        NotifyNavigation();
    });
    public ICommand RetryProfileCommand => new Command(async () =>
    { await store.SaveApiAddressAsync(ApiAddress); await profile.RetryAsync(ApiAddress); });
    public ICommand BackFromProfileCommand => new Command(async () =>
    {
        if (profile.IsEditing) profile.PreviousStepCommand.Execute(null);
        else if (await CanLeaveProfileAsync()) Screen("list");
    });
    public ICommand OpenCartCommand => new Command(async () => { if (!await CanLeaveProfileAsync()) return; await cart.ReloadAsync(); Screen("cart"); });
    public ICommand OpenOrdersCommand => new Command(async () => { if (await CanLeaveProfileAsync()) await OpenOrdersAsync(); });
    public ICommand OpenCompanyOrdersCommand => new Command(async () =>
    {
        await store.SaveApiAddressAsync(ApiAddress);
        companyOrders.BaseUrl = ApiAddress;
        Screen("company-orders");
        await companyOrders.RefreshAsync();
    });
    public ICommand BackFromReorderCommand => new Command(() => Screen("orders"));
    public ICommand AddToCartCommand => new Command(async () => await AddToCartAsync());
    public ICommand BackFromCartCommand => new Command(() => Screen("list"));
    public ICommand OpenCheckoutCommand => new Command(async () =>
    {
        if (!profile.HasLocalProfile) { Screen("profile"); return; }
        await store.SaveApiAddressAsync(ApiAddress);
        await checkout.ResumeAsync(ApiAddress);
        Screen("checkout");
    });
    public ICommand SaveCheckoutCommand => new Command(async () =>
    { await SaveCheckoutAsync(); });
    public async Task SaveCheckoutAsync()
    {
        await checkout.SaveAsync();
        if (checkout.SavedOrderId is null) return;
        await cart.ReloadAsync();
        Notify(nameof(LocalOrderId));
        Screen("local-order");
        await OpenOrdersAsync();
        await orders.OpenSavedAsync(checkout.SavedOrderId.Value);
    }
    private async Task OpenOrdersAsync()
    {
        orders.CloseDetail();
        await store.SaveApiAddressAsync(ApiAddress);
        orders.BaseUrl = ApiAddress;
        await orders.LoadAsync();
        Screen("orders");
        await orders.RefreshAsync();
        _ = orders.ProcessPendingAsync();
    }

    private async Task PrepareReorderAsync(Guid orderId)
    {
        Screen("reorder");
        IsReorderBusy = true;
        ReorderError = "";
        ReorderSource = "";
        _reorderEntries = [];
        ShowReorderLines();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var original = await orderApi.GetAsync(ApiAddress, orderId, timeout.Token);
            ReorderSource = $"Pedir novamente: {original.Protocol}. Confira os preços atuais antes de substituir o carrinho.";
            foreach (var line in original.Items)
            {
                var current = await api.ProductAsync(ApiAddress, line.ProductId, false, timeout.Token);
                _reorderEntries.Add(new(line, current));
            }
            ShowReorderLines();
            if (_reorderEntries.Any(x => x.Product is not { IsActive: true, IsAvailable: true }))
                ReorderError = "Remova os itens indisponíveis para continuar. O pedido anterior permanece intacto.";
        }
        catch (Exception)
        {
            _reorderEntries = [];
            ShowReorderLines();
            ReorderError = "Não foi possível consultar os produtos atuais. Confira a conexão com a loja e tente novamente.";
        }
        finally { IsReorderBusy = false; }
    }

    private void RemoveReorderLine(Guid productId)
    {
        _reorderEntries.RemoveAll(x => x.Original.ProductId == productId);
        ShowReorderLines();
        ReorderError = _reorderEntries.Count == 0 ? "Mantenha ao menos um produto para criar o novo carrinho." :
            CanConfirmReorder ? "" : "Remova os itens indisponíveis para continuar.";
    }

    private void ShowReorderLines()
    {
        ReorderLines.Clear();
        foreach (var entry in _reorderEntries)
            ReorderLines.Add(new ReorderLineViewModel(entry.Original, entry.Product, _br,
                new Command(() => RemoveReorderLine(entry.Original.ProductId))));
        Notify(nameof(CanConfirmReorder));
        Notify(nameof(ReorderTotal));
    }

    public async Task<bool> HasCartForReorderAsync() => (await cartStore.ListAsync()).Count > 0;

    public async Task ConfirmReorderAsync()
    {
        if (!CanConfirmReorder) return;
        IsReorderBusy = true;
        ReorderError = "";
        var replaced = false;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var refreshed = new List<ReorderEntry>();
            foreach (var entry in _reorderEntries)
                refreshed.Add(entry with
                { Product = await api.ProductAsync(ApiAddress, entry.Original.ProductId, false, timeout.Token) });
            var changed = refreshed.Where((item, index) => item.Product != _reorderEntries[index].Product).Any();
            if (changed)
            {
                _reorderEntries = refreshed;
                ShowReorderLines();
                ReorderError = "Preço ou disponibilidade mudou. Revise os itens antes de confirmar novamente.";
                return;
            }
            await cartStore.ReplaceAsync(refreshed.Select(x => (x.Product!, x.Original.Quantity)).ToArray());
            replaced = true;
            await cart.ReloadAsync();
            if (cart.Error.Length > 0) throw new IOException(cart.Error);
            Screen("cart");
        }
        catch (Exception error)
        {
            ReorderError = replaced ? "Novo carrinho salvo, mas não foi possível abrir. Volte ao carrinho." :
                error is InvalidOperationException ? error.Message :
                    "Não foi possível preparar o novo carrinho. O carrinho anterior permanece salvo.";
        }
        finally { IsReorderBusy = false; }
    }

    private sealed record ReorderEntry(OrderItemDto Original, ProductDto? Product);

    private async Task ReviewOrderAsync(Guid id)
    {
        try
        {
            var outbox = await localOrders.OutboxAsync(id)
                ?? throw new InvalidOperationException("Tentativa local não encontrada.");
            ApiAddress = outbox.BaseUrl ?? ApiAddress;
            var lines = await localOrders.ItemsAsync(id);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var profileChanged = outbox.LastErrorJson is { } errorJson &&
                JsonSerializer.Deserialize<ApiProblemDto>(errorJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))?.Code
                    == "CustomerVersionConflict";
            if (profileChanged)
            {
                var identity = await customerStore.ReadAsync();
                if (identity?.Customer is null)
                    throw new InvalidOperationException("Confirme o cadastro antes de revisar o pedido.");
                var currentCustomer = await customerApi.GetAsync(ApiAddress, identity.Customer.Id, timeout.Token);
                await customerStore.RefreshConfirmedAsync(currentCustomer);
            }
            var products = new Dictionary<Guid, ProductDto?>();
            foreach (var line in lines)
            {
                var productId = Guid.Parse(line.ProductId);
                products[productId] = await api.ProductAsync(ApiAddress, productId, false, timeout.Token);
            }
            var restored = await localOrders.RestoreForReviewAsync(id, products);
            await cart.ReloadAsync();
            if (!await checkout.LoadReviewAsync(ApiAddress, id, restored.Checkout, restored.Unavailable,
                profileChanged))
                throw new InvalidOperationException(checkout.Error);
            Screen("checkout");
        }
        catch (Exception error)
        {
            Error = error is InvalidOperationException ? error.Message :
                "Não foi possível carregar a revisão. Confira a conexão com a loja.";
        }
    }
    public void ResumeSynchronization()
    {
        if (!_apiAddressLoaded) return;
        orders.BaseUrl = ApiAddress;
        _ = orders.ProcessPendingAsync();
    }
    public ICommand BackFromCheckoutCommand => new Command(() => Screen("cart"));
    public ICommand DeliveryCommand => new Command(() => checkout.IsDelivery = true);
    public ICommand PickupCommand => new Command(() => checkout.IsDelivery = false);

    public bool HasUnsavedChanges => _openedEditor is not null && _openedEditor != Snapshot();

    public async Task ReturnToListAsync()
    {
        Screen("list");
        await RefreshAsync(false);
    }
    public ICommand ReviewCommand => new Command(() =>
    {
        if (_currentProduct is null) return;
        OpenEditor(_currentProduct);
        NeedsReview = false;
    });

    public async Task InitializeAsync()
    {
        orders.ReviewRequested = ReviewOrderAsync;
        orders.ReorderRequested = PrepareReorderAsync;
        ApiAddress = await store.ApiAddressAsync() ?? ApiAddress;
        _apiAddressLoaded = true;
        await cart.InitializeAsync();
        try { await profile.InitializeAsync(); }
        catch (Exception) { Error = "Não foi possível abrir o cadastro local."; }
        var localUnavailable = false;
        try { HasPending = await store.PendingAsync() is not null; }
        catch (Exception) { localUnavailable = true; }
        profile.PropertyChanged += (_, _) => NotifyNavigation();
        cart.PropertyChanged += (_, _) => Notify(nameof(CartTabTitle));
        Screen(profile.HasLocalProfile ? "list" : "profile");
        if (profile.HasLocalProfile) await RefreshAsync(true);
        orders.BaseUrl = ApiAddress;
        ResumeSynchronization();
        if (localUnavailable) Error = "Não foi possível ler as alterações locais pendentes. Evite editar até recuperar o armazenamento.";
    }

    public async Task RefreshAsync(bool refreshCategories)
    {
        if (IsBusy) return;
        IsBusy = true;
        Error = "";
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            if (refreshCategories)
            {
                var categories = await api.CategoriesAsync(ApiAddress, timeout.Token);
                await store.SaveCategoriesAsync(categories);
                SetCategories(categories);
            }
            var filter = _company ? _activeFilter switch
            { "Ativos" => true, "Inativos" => false, _ => (bool?)null } : null;
            var page = await api.ProductsAsync(ApiAddress, _company, _page, _search,
                FilterCategory(), filter, timeout.Token) ?? throw new InvalidDataException();
            await store.SaveProductsAsync(page.Items);
            SetProducts(page.Items);
            _total = checked((int)page.Total);
            Status = page.Items.Count == 0 ? "Nenhum produto encontrado." : "Catálogo atualizado.";
        }
        catch (Exception)
        {
            try
            {
                var cachedCategories = await store.CategoriesAsync();
                if (Categories.Count == 0) SetCategories(cachedCategories.Items);
                var filter = _company ? _activeFilter switch
                { "Ativos" => true, "Inativos" => false, _ => (bool?)null } : null;
                var cached = await store.ProductsAsync(_company, _search, FilterCategory(), filter, _page);
                SetProducts(cached.Items);
                _total = checked((int)cached.Total);
                var fetched = await store.LatestProductFetchAsync();
                Status = cached.Items.Count == 0
                    ? "Sem conexão e sem produtos armazenados para esta busca."
                    : $"Sem conexão · produtos salvos em {fetched?.ToLocalTime().ToString("g", _br) ?? "data desconhecida"}; preços e disponibilidade podem ter mudado.";
                Error = "Confira o endereço da loja e atualize quando houver conexão.";
            }
            catch (Exception)
            {
                Products.Clear();
                _total = 0;
                Status = "Catálogo indisponível.";
                Error = "Não foi possível consultar a loja nem ler os produtos salvos.";
            }
        }
        finally { IsBusy = false; Notify(nameof(PageText)); Notify(nameof(CanPrevious)); Notify(nameof(CanNext)); }
    }

    private async Task SwitchAsync(bool company)
    {
        if (IsBusy || _company == company) return;
        if (!await CanLeaveProfileAsync()) return;
        if (_company)
        {
            _companySearch = _search;
            _companyCategoryId = FilterCategory();
            _companyActiveFilter = _activeFilter;
        }
        else
        {
            _clientSearch = _search;
            _clientCategoryId = FilterCategory();
        }
        _company = company;
        _page = 1;
        _search = company ? _companySearch : _clientSearch;
        _selectedCategory = Categories.FirstOrDefault(x => x.Id == (company ? _companyCategoryId : _clientCategoryId))
            ?? Categories.FirstOrDefault();
        _activeFilter = company ? _companyActiveFilter : "Ativos";
        Notify(nameof(IsCompany)); Notify(nameof(IsClient)); Notify(nameof(Search));
        Notify(nameof(SelectedCategory)); Notify(nameof(ActiveFilter));
        if (company)
        {
            Error = "";
            Screen("company-orders");
            companyOrders.BaseUrl = ApiAddress;
            await companyOrders.RefreshAsync();
        }
        else { Screen(profile.HasLocalProfile ? "list" : "profile"); await RefreshAsync(true); }
        NotifyNavigation();
    }

    private async Task OpenProductAsync(Guid id)
    {
        if (IsBusy) return;
        IsBusy = true;
        Error = "";
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            _selectedProduct = await api.ProductAsync(ApiAddress, id, _company, timeout.Token);
            if (_selectedProduct is null)
            {
                await store.RemoveProductAsync(id);
                Status = "Produto removido do catálogo. Atualize a lista.";
                return;
            }
            await store.SaveProductsAsync([_selectedProduct]);
            DetailQuantity = "1";
            Screen("detail");
        }
        catch (Exception)
        {
            _selectedProduct = await store.ProductAsync(id);
            if (_selectedProduct is not null) { DetailQuantity = "1"; Screen("detail"); Error = "Detalhe armazenado; confirme valores quando houver conexão."; }
            else Error = "Não foi possível carregar o produto.";
        }
        finally { IsBusy = false; }
    }

    private void SetProducts(IReadOnlyList<ProductDto> products)
    {
        Products.Clear();
        foreach (var item in products)
            Products.Add(new ProductItem(item, _br, new Command(async () => await OpenProductAsync(item.Id))));
        Notify(nameof(IsCatalogEmpty));
    }

    private void SetCategories(IReadOnlyList<CategoryDto> items)
    {
        var selectedId = SelectedCategory?.Id;
        Categories.Clear();
        Categories.Add(new(Guid.Empty, "Todas as categorias"));
        foreach (var item in items) Categories.Add(item);
        SelectedCategory = Categories.FirstOrDefault(x => x.Id == selectedId) ?? Categories[0];
    }

    private Guid? FilterCategory() => SelectedCategory is { Id: var id } && id != Guid.Empty ? id : null;

    private void OpenEditor(ProductDto? product)
    {
        if (HasPending) { Error = "Recupere a alteração pendente antes de editar outro produto."; return; }
        _selectedProduct = product;
        Sku = product?.Sku ?? "";
        Name = product?.Name ?? "";
        Description = product?.Description ?? "";
        UnitLabel = product?.UnitLabel ?? "";
        Price = product?.Price.ToString("F2", _br) ?? "";
        ImageUrl = product?.ImageUrl ?? "";
        IsActive = product?.IsActive ?? true;
        IsAvailable = product?.IsAvailable ?? true;
        EditCategory = Categories.FirstOrDefault(x => x.Id == product?.CategoryId && x.Id != Guid.Empty);
        _openedEditor = Snapshot();
        SkuError = CategoryError = PriceError = "";
        NameError = UnitError = DescriptionError = ImageUrlError = "";
        Error = "";
        NeedsReview = false;
        Screen("editor");
    }

    private async Task SaveAsync()
    {
        if (IsBusy || HasPending) return;
        if (NeedsReview) { Error = "Carregue a versão atual e revise os campos antes de salvar."; return; }
        SkuError = CategoryError = PriceError = "";
        NameError = UnitError = DescriptionError = ImageUrlError = "";
        if (EditCategory is null || EditCategory.Id == Guid.Empty) CategoryError = "Selecione uma categoria.";
        if (string.IsNullOrWhiteSpace(Sku)) SkuError = "Informe o SKU.";
        if ((Name?.Trim().Length ?? 0) is < 2 or > 120) NameError = "Informe um nome de 2 a 120 caracteres.";
        if ((UnitLabel?.Trim().Length ?? 0) is < 1 or > 40) UnitError = "Informe uma embalagem de 1 a 40 caracteres.";
        if (!MoneyInput.TryParse(Price, out var price) || price is < 0.01m or > 99999.99m)
            PriceError = "Informe um preço válido em reais.";
        if (CategoryError.Length > 0 || SkuError.Length > 0 || NameError.Length > 0 ||
            UnitError.Length > 0 || PriceError.Length > 0) return;
        var key = Guid.NewGuid();
        var value = new ProductWriteRequest(key, EditCategory!.Id, Sku.Trim(), Name?.Trim() ?? "",
            string.IsNullOrWhiteSpace(Description) ? null : Description.Trim(), UnitLabel?.Trim() ?? "", price,
            string.IsNullOrWhiteSpace(ImageUrl) ? null : ImageUrl.Trim(), IsAvailable, IsActive);
        var path = _selectedProduct is null ? "/api/v1/admin/products" :
            $"/api/v1/admin/products/{_selectedProduct.Id:D}";
        var method = _selectedProduct is null ? "POST" : "PUT";
        var json = _selectedProduct is null ? ProductApiClient.Json(value) : ProductApiClient.Json(
            new ProductUpdateRequest(key, _selectedProduct.Version, value.CategoryId, value.Sku,
                value.Name, value.Description, value.UnitLabel, value.Price, value.ImageUrl,
                value.IsAvailable, value.IsActive));
        IsBusy = true;
        try
        {
            // A new company command starts only after this API has answered online.
            using (var onlineCheck = new CancellationTokenSource(TimeSpan.FromSeconds(8)))
                await api.CategoriesAsync(ApiAddress, onlineCheck.Token);
            // The exact body and key survive a lost response or app restart.
            await store.SavePendingAsync(new() { Path = path, Method = method, Json = json,
                BaseUrl = ApiAddress, CreatedAtUtc = DateTime.UtcNow.ToString("O") });
            HasPending = true;
            await SendPendingAsync();
        }
        catch (Exception)
        {
            Error = HasPending
                ? "Resultado desconhecido. Use Recuperar alteração para reenviar a mesma tentativa."
                : "A área Empresa precisa da loja online para iniciar a alteração; nenhum envio foi iniciado.";
        }
        finally { IsBusy = false; }
    }

    private async Task RecoverAsync()
    {
        if (IsBusy || !HasPending) return;
        IsBusy = true;
        await SendPendingAsync();
        IsBusy = false;
    }

    private async Task SendPendingAsync()
    {
        var pending = await store.PendingAsync();
        if (pending is null) { HasPending = false; return; }
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var product = await api.SendAsync(pending.BaseUrl, pending.Path, pending.Method, pending.Json, timeout.Token);
            await store.SaveProductsAsync([product]);
            await store.ClearPendingAsync();
            HasPending = false;
            Status = "Produto confirmado pela loja.";
            Error = "";
            Screen("list");
            await RefreshAfterSaveAsync();
        }
        catch (ApiException ex) when (ex.Problem is not null && ex.Status is System.Net.HttpStatusCode.BadRequest or
            System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Conflict)
        {
            if (ex.Problem.Code == "RequestKeyConflict")
            {
                Error = "Chave de alteração em conflito. Revise a tentativa armazenada.";
                return;
            }
            await store.ClearPendingAsync();
            HasPending = false;
            Error = ex.Problem.Detail;
            SkuError = ex.Problem.Errors.FirstOrDefault(x => x.Field == "sku")?.Message ?? "";
            CategoryError = ex.Problem.Errors.FirstOrDefault(x => x.Field == "categoryId")?.Message ?? "";
            PriceError = ex.Problem.Errors.FirstOrDefault(x => x.Field == "price")?.Message ?? "";
            NameError = ex.Problem.Errors.FirstOrDefault(x => x.Field == "name")?.Message ?? "";
            UnitError = ex.Problem.Errors.FirstOrDefault(x => x.Field == "unitLabel")?.Message ?? "";
            DescriptionError = ex.Problem.Errors.FirstOrDefault(x => x.Field == "description")?.Message ?? "";
            ImageUrlError = ex.Problem.Errors.FirstOrDefault(x => x.Field == "imageUrl")?.Message ?? "";
            if (ex.Problem.Code == "ProductVersionConflict")
            {
                try
                {
                    using var reload = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                    _currentProduct = _selectedProduct is null ? null :
                        await api.ProductAsync(ApiAddress, _selectedProduct.Id, true, reload.Token);
                }
                catch (Exception) { _currentProduct = null; }
                NeedsReview = _currentProduct is not null;
                Error = _currentProduct is null
                    ? "Versão alterada. Volte à lista e abra o produto para revisar."
                    : $"Versão alterada. Atual: {_currentProduct.Name}, {_currentProduct.Price.ToString("C", _br)}. Carregue e revise antes de salvar.";
            }
        }
        catch (Exception)
        {
            HasPending = true;
            Error = "Resultado desconhecido. Use Recuperar alteração para reenviar a mesma chave e os mesmos dados.";
        }
    }

    private async Task RefreshAfterSaveAsync()
    {
        // Called after the write is confirmed; RefreshAsync controls its own busy flag.
        IsBusy = false;
        await RefreshAsync(true);
    }

    private void Screen(string screen)
    {
        if (_screen == "checkout" && checkout.IsBusy) return;
        if (IsCompany && screen is "profile" or "cart" or "checkout" or "orders") return;
        _screen = screen;
        NotifyNavigation();
        foreach (var name in new[] { nameof(IsInitializing), nameof(IsList), nameof(IsDetail), nameof(IsEditor), nameof(IsProfile), nameof(IsCart), nameof(IsCheckout), nameof(IsLocalOrder), nameof(IsOrders), nameof(IsReorder), nameof(IsCompanyOrders),
            nameof(DetailName), nameof(DetailSku), nameof(DetailDescription), nameof(DetailUnit),
            nameof(DetailPrice), nameof(DetailAvailability), nameof(DetailImage),
            nameof(HasDetailImage), nameof(NoDetailImage), nameof(EditorTitle), nameof(CanAddToCart),
            nameof(IsUnavailableForCart) }) Notify(name);
        Notify(nameof(AddToCartLabel));
    }

    private void NotifyNavigation()
    {
        foreach (var name in new[] { nameof(ShowClientNavigation), nameof(ShowCompanyNavigation),
            nameof(AreaTitle), nameof(AreaSubtitle), nameof(ShowCatalogFeedback), nameof(IsCatalogSelected),
            nameof(IsCartSelected), nameof(IsOrdersSelected), nameof(IsProfileSelected),
            nameof(IsProductsSelected), nameof(IsCompanyOrdersSelected), nameof(ShowPendingProduct) }) Notify(name);
    }

    private async Task<bool> CanLeaveProfileAsync()
    {
        if (!IsProfile) return true;
        if (profile.IsBusy) return false;
        if (!profile.HasUnsavedChanges) return true;
        if (profile.ConfirmDiscard is null || !await profile.ConfirmDiscard()) return false;
        await profile.InitializeAsync();
        return true;
    }

    private async Task AddToCartAsync()
    {
        if (!CanAddToCart || _selectedProduct is null) return;
        if (!int.TryParse(DetailQuantity, NumberStyles.None, CultureInfo.InvariantCulture, out var quantity))
        {
            Error = "Informe quantidade inteira de 1 a 999.";
            return;
        }
        await cart.AddAsync(_selectedProduct, quantity);
        if (cart.Error.Length == 0) Screen("cart");
        else Error = cart.Error;
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        Notify(name);
    }
    private void Notify(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private EditorSnapshot Snapshot() => new(Sku, Name, Description, UnitLabel, Price, ImageUrl,
        EditCategory?.Id, IsAvailable, IsActive);
    private sealed record EditorSnapshot(string Sku, string Name, string Description, string UnitLabel,
        string Price, string ImageUrl, Guid? CategoryId, bool IsAvailable, bool IsActive);
}

public sealed class ProductItem
{
    public ProductItem(ProductDto product, CultureInfo culture, ICommand openCommand)
    {
        Name = product.Name;
        Subtitle = $"{product.UnitLabel} · {product.Sku}";
        Price = product.Price.ToString("C", culture);
        Availability = product.IsActive ? (product.IsAvailable ? "Disponível" : "Indisponível") : "Inativo";
        ImageUrl = product.ImageUrl;
        HasImage = !string.IsNullOrWhiteSpace(product.ImageUrl);
        NoImage = !HasImage;
        OpenCommand = openCommand;
    }
    public string Name { get; }
    public string Subtitle { get; }
    public string Price { get; }
    public string Availability { get; }
    public string? ImageUrl { get; }
    public bool HasImage { get; }
    public bool NoImage { get; }
    public ICommand OpenCommand { get; }
}

public sealed class ReorderLineViewModel
{
    public ReorderLineViewModel(OrderItemDto previous, ProductDto? current,
        CultureInfo culture, ICommand removeCommand)
    {
        Name = $"{previous.Quantity} × {previous.ProductName}";
        Price = current is null
            ? $"Preço anterior: {previous.UnitPrice.ToString("C", culture)} · Produto não encontrado na loja"
            : $"Preço anterior: {previous.UnitPrice.ToString("C", culture)} · " +
                $"Preço atual: {current.Price.ToString("C", culture)}";
        Availability = current is { IsActive: true, IsAvailable: true }
            ? "Disponível para o novo carrinho" : "Indisponível — remova para continuar";
        RemoveCommand = removeCommand;
    }
    public string Name { get; }
    public string Price { get; }
    public string Availability { get; }
    public ICommand RemoveCommand { get; }
}
