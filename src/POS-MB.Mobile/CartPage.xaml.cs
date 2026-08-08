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

    private async void OnPlaceOrderClicked(object? sender, EventArgs e)
    {
        ErrorLabel.IsVisible = false;
        PlaceOrderButton.IsEnabled = false;

        var items = Cart.Lines
            .Select(l => new OrderItemLineDto(l.ItemId, l.Quantity, null))
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
        await DisplayAlert("Order placed", $"Order #{result.OrderId} placed successfully.", "OK");
        await Navigation.PopAsync(); // back to MenuPage, not all the way to LoginPage
    }
}
