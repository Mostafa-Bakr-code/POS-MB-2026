namespace POS_MB.Business.Payments;

// Bound from the "Paymob" configuration section (user secrets locally, real
// secrets manager once this moves to the cloud) - never stored in the
// database-backed Settings table like DefaultTaxRate/TimeZoneOffsetHours,
// since these are infrastructure credentials, not app behavior settings.
public class PaymobOptions
{
    public string SecretKey { get; set; } = "";
    public string PublicKey { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string HmacSecret { get; set; } = "";
    public int CardIntegrationId { get; set; }

    // Where Paymob POSTs the transaction result - must be a real, publicly
    // reachable URL (a deployed API, or an ngrok tunnel while testing
    // locally). See PaymentsController.PaymobWebhook.
    public string WebhookUrl { get; set; } = "";

    // Where the student's in-app WebView gets redirected once payment
    // finishes - this is only ever a marker the mobile app watches for
    // navigation to (see the checkout page), it never needs to resolve to a
    // real, working website.
    public string RedirectionUrl { get; set; } = "https://posmb.app/payment-complete";
}
