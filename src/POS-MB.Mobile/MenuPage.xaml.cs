using POS_MB.Mobile.Api;
using POS_MB.Mobile.Models;
using POS_MB.Mobile.Session;

namespace POS_MB.Mobile;

public partial class MenuPage : ContentPage
{
    private static readonly TimeSpan AcceptingOrdersPollInterval = TimeSpan.FromSeconds(15);

    private readonly ApiClient _apiClient = new();
    private CancellationTokenSource? _pollCts;
    private string? _lastKnownReason = "not yet loaded"; // forces the very first check to always apply

    public MenuPage()
    {
        InitializeComponent();
        WelcomeLabel.Text = $"Hi, {AppSession.CurrentStudent?.Email}";
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadAsync();
        await RefreshAcceptingOrdersBannerAsync();
        RefreshCartButton();
        StartAcceptingOrdersPolling();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _pollCts?.Cancel();
        _pollCts = null;
    }

    // A staff member can flip the toggle from an entirely different device
    // (the WinForms Order Status screen) while a student is just sitting on
    // this menu - same reasoning as OrderDetailPage's status poll, just for
    // this one flag instead of an order's status.
    private void StartAcceptingOrdersPolling()
    {
        _pollCts?.Cancel();
        _pollCts = new CancellationTokenSource();
        _ = AcceptingOrdersPollLoopAsync(_pollCts.Token);
    }

    private async Task AcceptingOrdersPollLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(AcceptingOrdersPollInterval, token);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            if (token.IsCancellationRequested) return;

            try
            {
                await RefreshAcceptingOrdersBannerAsync();
            }
            catch (Exception)
            {
                // Transient failure - keep showing the last known state and
                // try again next tick.
            }
        }
    }

    // Warns before a student builds a whole cart, not just at the final
    // checkout step - the actual enforcement is server-side regardless
    // (clsOrderBusiness.CreateOrderAsync), this is purely a heads-up so
    // rejection isn't a surprise after they've already picked everything.
    // Reads the same combined toggle+heartbeat status CreateOrderAsync itself
    // enforces, so the banner text can never claim a different reason than
    // what actually happens at checkout.
    private async Task RefreshAcceptingOrdersBannerAsync()
    {
        var (isAccepting, reason) = await _apiClient.GetAcceptingOnlineOrdersStatusAsync();

        if (_lastKnownReason == reason) return; // nothing changed - don't touch the UI
        _lastKnownReason = reason;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            NotAcceptingOrdersBanner.Text = reason ?? "";
            NotAcceptingOrdersBanner.IsVisible = !isAccepting;
        });
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
