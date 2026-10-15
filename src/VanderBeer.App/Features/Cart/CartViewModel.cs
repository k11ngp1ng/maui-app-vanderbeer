using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using VanderBeer.App.Storage;
using VanderBeer.Contracts.Catalog;

namespace VanderBeer.App.Features.Cart;

public sealed class CartViewModel(CartStore store) : INotifyPropertyChanged
{
    private readonly CultureInfo _br = CultureInfo.GetCultureInfo("pt-BR");
    private bool _busy;
    private string _error = "";
    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<CartLineViewModel> Items { get; } = [];
    public bool IsBusy { get => _busy; private set => Set(ref _busy, value); }
    public string Error { get => _error; private set => Set(ref _error, value); }
    public bool IsEmpty => Items.Count == 0;
    public bool HasItems => !IsEmpty && !IsBusy;
    public string CountText => Items.Count == 1 ? "1 produto" : $"{Items.Count} produtos";
    public string EstimatedTotal => Items.Sum(x => x.Item.Subtotal).ToString("C", _br);

    public Task InitializeAsync() => ReloadAsync();

    public async Task AddAsync(ProductDto product, int quantity)
    {
        if (IsBusy) return;
        IsBusy = true;
        Error = "";
        try { await store.AddAsync(product, quantity); await LoadAsync(); }
        catch (Exception error) { Error = error is InvalidOperationException ? error.Message : "Não foi possível salvar o carrinho."; }
        finally { IsBusy = false; }
    }

    public async Task ReloadAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        Error = "";
        try { await LoadAsync(); }
        catch (Exception) { Error = "Não foi possível ler o carrinho local."; }
        finally { IsBusy = false; }
    }

    private async Task LoadAsync()
    {
        var items = await store.ListAsync();
        Items.Clear();
        foreach (var item in items) Items.Add(new CartLineViewModel(item, this, _br));
        Notify(nameof(IsEmpty)); Notify(nameof(CountText)); Notify(nameof(EstimatedTotal));
        Notify(nameof(HasItems));
    }

    internal async Task ChangeAsync(CartItem item, int quantity)
    {
        if (IsBusy) return;
        IsBusy = true;
        Error = "";
        try { await store.SetQuantityAsync(item.ProductId, quantity); await LoadAsync(); }
        catch (Exception error) { Error = error is InvalidOperationException ? error.Message : "Não foi possível alterar a quantidade."; }
        finally { IsBusy = false; }
    }

    internal async Task RemoveAsync(CartItem item)
    {
        if (IsBusy) return;
        IsBusy = true;
        Error = "";
        try { await store.RemoveAsync(item.ProductId); await LoadAsync(); }
        catch (Exception) { Error = "Não foi possível remover o produto."; }
        finally { IsBusy = false; }
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        Notify(name);
        if (name == nameof(IsBusy)) Notify(nameof(HasItems));
    }
    private void Notify(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class CartLineViewModel
{
    private readonly CartViewModel _cart;
    public CartLineViewModel(CartItem item, CartViewModel cart, CultureInfo culture)
    {
        Item = item;
        _cart = cart;
        Name = item.ProductName;
        UnitLabel = item.UnitLabel;
        Quantity = item.Quantity.ToString(CultureInfo.InvariantCulture);
        UnitPrice = item.UnitPrice.ToString("C", culture);
        Subtotal = item.Subtotal.ToString("C", culture);
        IncreaseCommand = new Command(async () => await _cart.ChangeAsync(Item, Item.Quantity + 1));
        DecreaseCommand = new Command(async () => await _cart.ChangeAsync(Item, Item.Quantity - 1));
        SaveQuantityCommand = new Command(async () =>
        {
            if (!int.TryParse(Quantity, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            {
                await _cart.ChangeAsync(Item, 0);
                return;
            }
            await _cart.ChangeAsync(Item, value);
        });
        RemoveCommand = new Command(async () => await _cart.RemoveAsync(Item));
    }
    public CartItem Item { get; }
    public string Name { get; }
    public string UnitLabel { get; }
    public string Quantity { get; set; }
    public string UnitPrice { get; }
    public string Subtotal { get; }
    public ICommand IncreaseCommand { get; }
    public ICommand DecreaseCommand { get; }
    public ICommand SaveQuantityCommand { get; }
    public ICommand RemoveCommand { get; }
}
