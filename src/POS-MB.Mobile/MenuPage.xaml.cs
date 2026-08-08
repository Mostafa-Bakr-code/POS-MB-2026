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

    private async void OnMyOrdersClicked(object? sender, EventArgs e)
    {
        await Navigation.PushAsync(new OrdersPage());
    }

    private async void OnLogoutClicked(object? sender, EventArgs e)
    {
        var confirmed = await DisplayAlert("Log Out", "Are you sure you want to log out?", "Yes", "No");
        if (!confirmed) return;

        // Revoke server-side first, while AppSession.Token is still set (the
        // logout endpoint requires it) - then clear locally regardless of
        // whether the revoke call actually succeeded.
        if (AppSession.RefreshToken is not null)
            await _apiClient.LogoutAsync(AppSession.RefreshToken);

        AppSession.Clear();
        AppSession.ClearPersisted();
        Cart.Clear();

        await Navigation.PopToRootAsync();
    }

    private async Task LoadAsync()
    {
        var categories = await _apiClient.GetCategoriesAsync();
        CategoriesView.ItemsSource = categories;

        // Items only load once a category is picked (see OnCategorySelected) -
        // reloading (e.g. pull-to-refresh) drops back to the same empty,
        // no-category-selected state rather than guessing which one to keep.
        CategoriesView.SelectedItem = null;
        ItemsView.ItemsSource = null;
        PlaceholderLabel.IsVisible = true;
    }

    private async void OnCategorySelected(object? sender, SelectionChangedEventArgs e)
    {
        var selected = e.CurrentSelection.FirstOrDefault() as CategoryDto;
        if (selected is null)
        {
            ItemsView.ItemsSource = null;
            PlaceholderLabel.IsVisible = true;
            return;
        }

        PlaceholderLabel.IsVisible = false;
        var items = await _apiClient.GetItemsAsync(selected.CategoryId);
        ItemsView.ItemsSource = items;
    }

    private async void OnRefreshing(object? sender, EventArgs e)
    {
        await LoadAsync();
        ItemsRefreshView.IsRefreshing = false;
    }
}
