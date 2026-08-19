using POS_MB.Mobile.Session;

namespace POS_MB.Mobile;

public partial class PaymentCheckoutPage : ContentPage
{
    // Matches PaymobOptions.RedirectionUrl on the API side - this URL never
    // needs to actually resolve to anything real, it's purely a marker the
    // WebView watches for. Both sides need to agree on the same value.
    private const string RedirectionUrlPrefix = "https://posmb.app/payment-complete";

    private readonly int _orderId;
    private bool _redirected;

    public PaymentCheckoutPage(string checkoutUrl, int orderId)
    {
        InitializeComponent();
        _orderId = orderId;
        CheckoutWebView.Source = checkoutUrl;

        // Overrides the automatic back arrow Shell puts in the nav bar -
        // found live: a student backing out here mid-payment (specifically
        // during the bank's own 3D Secure/OTP step, before it ever
        // resolves) had no way to know that was any different from backing
        // out of an ordinary page. This is the one moment in the whole flow
        // where leaving actually matters, so it's the one place worth a
        // confirmation. Android's hardware back button is handled
        // separately below - Shell.BackButtonBehavior only covers the nav
        // bar arrow / iOS swipe.
        Shell.SetBackButtonBehavior(this, new BackButtonBehavior
        {
            Command = new Command(async () => await ConfirmAndLeaveAsync())
        });
    }

    protected override bool OnBackButtonPressed()
    {
        _ = ConfirmAndLeaveAsync();
        return true; // handled ourselves - the default immediate pop must not happen
    }

    private async Task ConfirmAndLeaveAsync()
    {
        // Once Paymob has actually redirected back to us, the attempt is
        // fully resolved one way or another - nothing left to warn about.
        if (_redirected)
        {
            await Navigation.PopAsync();
            return;
        }

        if (!await ConfirmLeavingAsync()) return;

        // Same as a successful redirect - lands on the order's own status
        // screen (which polls and self-reconciles) rather than a dead end,
        // since the whole point of this dialog is "the outcome is still
        // unresolved," not "this definitely failed."
        var orderDetailPage = new OrderDetailPage(_orderId);
        Navigation.InsertPageBefore(orderDetailPage, this);
        await Navigation.PopAsync();
    }

    private Task<bool> ConfirmLeavingAsync() => DisplayAlert(
        "Leave Payment?",
        "Your payment isn't finished yet. If you're in the middle of verifying with your bank, leaving now won't cancel it - it'll either succeed or expire on its own. You can check on it from your order.",
        "Leave", "Stay");

    private void OnNavigated(object? sender, WebNavigatedEventArgs e) => LoadingIndicator.IsVisible = false;

    // Fires before the WebView actually loads a URL - this is what lets the
    // redirect be caught and cancelled before it ever tries to load
    // posmb.app (which doesn't exist), instead of letting the WebView show
    // an error page for a real navigation attempt.
    private async void OnNavigating(object? sender, WebNavigatingEventArgs e)
    {
        if (_redirected || !e.Url.StartsWith(RedirectionUrlPrefix, StringComparison.OrdinalIgnoreCase))
            return;

        _redirected = true;
        e.Cancel = true;

        // Replaces this page with the order's own status screen rather than
        // popping back to it - OrderDetailPage already polls every 5s and
        // will show the order moving from AwaitingPayment to Placed itself
        // once the webhook lands, no extra "wait for payment" logic needed
        // here. Using InsertPageBefore + Pop (not just PushAsync) so the
        // spent checkout page isn't left sitting in the back-navigation
        // stack behind it.
        var orderDetailPage = new OrderDetailPage(_orderId);
        Navigation.InsertPageBefore(orderDetailPage, this);
        await Navigation.PopAsync();
    }

    private async void OnHomeClicked(object? sender, EventArgs e)
    {
        if (!_redirected && !await ConfirmLeavingAsync()) return;
        await NavigationHelper.GoHomeAsync(Navigation);
    }
}
