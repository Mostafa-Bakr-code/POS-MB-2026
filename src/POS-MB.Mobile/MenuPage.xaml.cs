using POS_MB.Mobile.Api;
using POS_MB.Mobile.Models;
using POS_MB.Mobile.Session;

namespace POS_MB.Mobile;

public partial class MenuPage : ContentPage
{
    private readonly ApiClient _apiClient = new();

    public MenuPage()
    {
        InitializeComponent();
        WelcomeLabel.Text = $"Hi, {AppSession.CurrentStudent?.Email}";
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadAsync();
        RefreshCartButton();
    }

    private void OnAddToCartClicked(object? sender, EventArgs e)
    {
        if ((sender as Button)?.BindingContext is not ItemDto item) return;

        Cart.Add(item);
        RefreshCartButton();
    }

    private void RefreshCartButton()
    {
        CartButton.Text = Cart.TotalItemCount == 0
            ? "Cart is empty"
            : $"View Cart ({Cart.TotalItemCount}) - {Cart.Total:0.00}";
        CartButton.IsEnabled = Cart.TotalItemCount > 0;
    }

    private async void OnCartClicked(object? sender, EventArgs e)
    {
        await Navigation.PushAsync(new CartPage());
    }

    private async Task LoadAsync()
    {
        var categories = await _apiClient.GetCategoriesAsync();
        CategoriesView.ItemsSource = categories;

        await LoadItemsAsync(categoryId: null);
    }

    private async Task LoadItemsAsync(int? categoryId)
    {
        var items = await _apiClient.GetItemsAsync(categoryId);
        ItemsView.ItemsSource = items;
    }

    private async void OnCategorySelected(object? sender, SelectionChangedEventArgs e)
    {
        var selected = e.CurrentSelection.FirstOrDefault() as CategoryDto;
        await LoadItemsAsync(selected?.CategoryId);
    }

    private async void OnRefreshing(object? sender, EventArgs e)
    {
        await LoadAsync();
        ItemsRefreshView.IsRefreshing = false;
    }
}
