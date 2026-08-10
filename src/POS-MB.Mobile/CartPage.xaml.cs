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
        Cart.Remove(line);
        Refresh();
    }

    private void OnAddAnotherClicked(object? sender, EventArgs e)
    {
        if ((sender as Button)?.BindingContext is not CartLine line) return;
        Cart.AddAnother(line);
        Refresh();
    }

    private async void OnCommentClicked(object? sender, EventArgs e)
    {
        if ((sender as Button)?.BindingContext is not CartLine line) return;
        var result = await DisplayPromptAsync($"Comment for {line.ItemName}", "e.g. no onions",
            initialValue: line.Comment ?? "", maxLength: 50);
        if (result is null) return;
        line.Comment = string.IsNullOrWhiteSpace(result) ? null : result;
        Refresh();
    }

    private async void OnPlaceOrderClicked(object? sender, EventArgs e)
    {
        ErrorLabel.IsVisible = false;
        PlaceOrderButton.IsEnabled = false;

        var items = Cart.Lines.Select(l => new OrderItemLineDto(l.ItemId, l.Quantity, l.Comment)).ToList();
        var (result, error, unavailableItems) = await _apiClient.PlaceOrderAsync(items);

        if (result is null)
        {
            if (unavailableItems.Count > 0)
            {
                // Removes exactly the flagged lines rather than making the
                // student hunt through the cart for what was rejected - the
                // server told us precisely which items and why.
                var unavailableIds = unavailableItems.Select(i => i.ItemId).ToHashSet();
                Cart.Lines.RemoveAll(l => unavailableIds.Contains(l.ItemId));

                var names = string.Join(", ", unavailableItems.Select(i => $"{i.ItemName} ({i.Reason})"));
                ErrorLabel.Text = $"Removed from your cart - {names}. Review your cart and try again.";
                ErrorLabel.IsVisible = true;
                Refresh();
                return;
            }

            ErrorLabel.Text = error ?? "Something went wrong.";
            ErrorLabel.IsVisible = true;
            PlaceOrderButton.IsEnabled = true;
            return;
        }

        // The order exists now (price/items locked in), but isn't real until
        // payment succeeds - the cart is cleared regardless, since resuming a
        // half-paid cart isn't supported yet, but the order itself lives on
        // as AwaitingPayment until either payment completes or the auto-cancel
        // safety net eventually cleans it up.
        Cart.Clear();

        if (result.CheckoutUrl is null)
        {
            // Shouldn't happen for a real Mobile order, but fail toward
            // showing something sane rather than a blank/broken screen.
            await DisplayAlert("Order placed", $"Order #{result.SerialNumber ?? result.OrderId} placed successfully.", "OK");
            await Navigation.PopAsync();
            return;
        }

        await Navigation.PushAsync(new PaymentCheckoutPage(result.CheckoutUrl, result.OrderId));
    }
}
