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
    }

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
}
