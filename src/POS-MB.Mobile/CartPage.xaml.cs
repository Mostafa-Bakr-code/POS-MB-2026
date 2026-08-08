using POS_MB.Mobile.Api;
using POS_MB.Mobile.Models;
using POS_MB.Mobile.Session;

namespace POS_MB.Mobile;

public partial class CartPage : ContentPage
{
    private readonly ApiClient _apiClient = new();

    public CartPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Refresh();
    }

    private void Refresh()
    {
        CartView.ItemsSource = null;
        CartView.ItemsSource = Cart.Lines;
        TotalLabel.Text = $"Total: {Cart.Total:0.00}";
        PlaceOrderButton.IsEnabled = Cart.Lines.Count > 0;
    }

    private void OnRemoveClicked(object? sender, EventArgs e)
    {
        if ((sender as Button)?.BindingContext is not CartLine line) return;

        Cart.Remove(line.ItemId);
        Refresh();
    }

    // Comment length matches OrderItems.Comment NVARCHAR(50) - the same limit
    // WinForms' comment dialog enforces.
    private async void OnCommentClicked(object? sender, EventArgs e)
    {
        if ((sender as Button)?.BindingContext is not CartLine line) return;

        var result = await DisplayPromptAsync(
            $"Comment for {line.ItemName}", "e.g. no onions",
            initialValue: line.Comment ?? "", maxLength: 50);

        if (result is null) return; // cancelled

        line.Comment = string.IsNullOrWhiteSpace(result) ? null : result;
        Refresh();
    }

    private async void OnPlaceOrderClicked(object? sender, EventArgs e)
    {
        ErrorLabel.IsVisible = false;
        PlaceOrderButton.IsEnabled = false;

        var items = Cart.Lines
            .Select(l => new OrderItemLineDto(l.ItemId, l.Quantity, l.Comment))
            .ToList();

        var (result, error) = await _apiClient.PlaceOrderAsync(items);

        if (result is null)
        {
            ErrorLabel.Text = error ?? "Something went wrong.";
            ErrorLabel.IsVisible = true;
            PlaceOrderButton.IsEnabled = true;
            return;
        }

        Cart.Clear();
        // SerialNumber is the customer-facing daily order number (resets each day,
        // wraps at 100 per the setting in WinForms) - OrderId is the permanent
        // database primary key and keeps climbing forever across every order ever
        // placed, which is meaningless to show a student here.
        await DisplayAlert("Order placed", $"Order #{result.SerialNumber ?? result.OrderId} placed successfully.", "OK");
        await Navigation.PopAsync(); // back to MenuPage, not all the way to LoginPage
    }
}
