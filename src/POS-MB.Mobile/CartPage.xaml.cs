using POS_MB.Mobile.Api;
using POS_MB.Mobile.Models;
using POS_MB.Mobile.Session;

namespace POS_MB.Mobile;

public partial class CartPage : ContentPage
{
    // Matches the cashier side's own copy of this key - the setting that
    // marks which category's items show up behind a cart line's "+Extra"
    // button.
    private const string ExtrasCategorySettingKey = "ExtrasCategoryId";

    private readonly ApiClient _apiClient = new();
    private List<ItemDto> _extraItems = [];

    public CartPage()
    {
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        Refresh();
        await LoadExtrasAsync();
    }

    // Loaded once per cart visit, not re-fetched on every "+Extra" tap -
    // same tradeoff as the cashier side's own OrderTakingPage.
    private async Task LoadExtrasAsync()
    {
        var value = await _apiClient.GetSettingValueAsync(ExtrasCategorySettingKey);
        _extraItems = int.TryParse(value, out var categoryId)
            ? await _apiClient.GetItemsAsync(categoryId)
            : [];
    }

    private void Refresh()
    {
        CartView.ItemsSource = null;
        CartView.ItemsSource = Cart.Lines;
        TotalLabel.Text = $"Total: {Cart.Total:0.00}";
        PlaceOrderButton.IsEnabled = Cart.Lines.Count > 0;

        var maskedPan = AppSession.CurrentStudent?.SavedCardMaskedPan;
        SavedCardLayout.IsVisible = maskedPan is not null;
        if (maskedPan is not null)
        {
            var subtype = AppSession.CurrentStudent?.SavedCardSubtype;
            SavedCardLabel.Text = subtype is null
                ? $"Pay with saved card {maskedPan}"
                : $"Pay with saved {subtype} {maskedPan}";
        }
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

    // An extra always attaches to the ONE specific line it was requested
    // for - never to the cart generally - so two separate occurrences of
    // the same base item can carry two different extras without either
    // the in-app cart or the kitchen ticket being ambiguous about which
    // extra belongs to which.
    private async void OnAddExtraClicked(object? sender, EventArgs e)
    {
        if ((sender as Button)?.BindingContext is not CartLine line) return;

        if (_extraItems.Count == 0)
        {
            await DisplayAlert("Extras", "No extras are available right now.", "OK");
            return;
        }

        var labels = _extraItems.Select(i => $"{i.ItemName} ({i.Price:0.00})").ToArray();
        var choice = await DisplayActionSheet($"Add extra to {line.ItemName}", "Cancel", null, labels);
        if (choice is null || choice == "Cancel") return;

        var index = Array.IndexOf(labels, choice);
        if (index < 0) return;

        Cart.AddExtra(line, _extraItems[index]);
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

        // Flattened, not just Cart.Lines - each base item's own extras need
        // to travel to the server as their own order-item rows too, sent
        // immediately after their parent (not grouped by item) so the
        // order they come back in later (My Orders, the kitchen ticket)
        // still reads unambiguously which extra was for which specific
        // item, with no new field needed anywhere.
        var items = Cart.Flattened.Select(l => new OrderItemLineDto(l.ItemId, l.Quantity, l.Comment)).ToList();
        var useSavedCard = SavedCardLayout.IsVisible && SavedCardCheckBox.IsChecked;
        var (result, error, unavailableItems) = await _apiClient.PlaceOrderAsync(items, useSavedCard);

        if (result is null)
        {
            if (unavailableItems.Count > 0)
            {
                // Removes exactly the flagged lines rather than making the
                // student hunt through the cart for what was rejected - the
                // server told us precisely which items and why.
                var unavailableIds = unavailableItems.Select(i => i.ItemId).ToHashSet();
                Cart.RemoveAllWhere(l => unavailableIds.Contains(l.ItemId));

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

        // The cart is already empty at this point (cleared above) - leaving
        // this page in the stack meant pressing back from the payment
        // screen landed on a pointless empty cart, requiring a second back
        // tap to actually get anywhere. Removing it here means back goes
        // straight to the menu instead.
        Navigation.RemovePage(this);
    }

    private async void OnHomeClicked(object? sender, EventArgs e) =>
        await NavigationHelper.GoHomeAsync(Navigation);
}
