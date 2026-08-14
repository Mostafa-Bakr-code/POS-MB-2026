using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace POS_MB.Business.Payments;

public record PaymobIntentionResult(string ClientSecret, long PaymobOrderId);

// Success is whether Paymob actually processed the refund. RefundTransactionId
// is the refund's own transaction id - a separate record from the original
// charge being refunded (linked to it via Paymob's own parent_transaction
// field), only meaningful when Success is true.
public record PaymobRefundResult(bool Success, long? RefundTransactionId);

// Found is false when Paymob has no transaction at all for the given
// reference (nobody ever attempted payment, or the inquiry call itself
// failed) - Success/TransactionId/AmountEgp are only meaningful when Found
// is true, and even then Success reflects whether that transaction
// actually succeeded (a failed/declined attempt is still "found").
public record PaymobInquiryResult(bool Found, bool Success, long? TransactionId, decimal? AmountEgp);

// Talks to Paymob's Intention API (https://developers.paymob.com) - the
// Secret Key never leaves this class/the server it runs on. Base URL is
// configured on the injected HttpClient (see Program.cs), not hardcoded here,
// so region (Egypt/UAE/KSA/Oman) is a deployment concern, not a code one.
public class PaymobClient(HttpClient httpClient, PaymobOptions options)
{
    // A student order is locked in (price, items) before this is ever called -
    // see clsOrderBusiness - so amountEgp is always the exact, already-decided
    // total, converted here to the piasters/cents Paymob's API expects.
    // notification_url/redirection_url come from configuration (PaymobOptions),
    // not from the caller - they're deployment concerns (which webhook host,
    // which marker URL), not something specific to any one order.
    // virtual for the same reason RefundAsync/InquireByMerchantOrderIdAsync
    // are - ResumeCheckoutAsync can fall through to calling this, and tests
    // exercising that path must never risk a real call to Paymob's API.
    public virtual async Task<PaymobIntentionResult> CreateIntentionAsync(
        decimal amountEgp, string specialReference, string customerEmail, int expirationSeconds)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "v1/intention/");
        request.Headers.Add("Authorization", $"Token {options.SecretKey}");
        request.Content = JsonContent.Create(new
        {
            amount = (int)Math.Round(amountEgp * 100),
            currency = "EGP",
            payment_methods = new[] { options.CardIntegrationId },
            // Real billing address fields aren't meaningful for a digital food
            // pickup order - Paymob's own examples use placeholder values for
            // exactly this reason, only email is real (used for the student's
            // own receipt/communication from Paymob, not looked up by us).
            billing_data = new
            {
                apartment = "NA",
                floor = "NA",
                first_name = "Student",
                last_name = "Order",
                street = "NA",
                building = "NA",
                phone_number = "+201000000000",
                city = "NA",
                country = "NA",
                state = "NA",
                email = customerEmail
            },
            special_reference = specialReference,
            expiration = expirationSeconds,
            notification_url = options.WebhookUrl,
            redirection_url = options.RedirectionUrl
        });

        var response = await httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var clientSecret = body.GetProperty("client_secret").GetString()
            ?? throw new InvalidOperationException("Paymob response did not include a client_secret.");
        var paymobOrderId = body.GetProperty("intention_order_id").GetInt64();

        return new PaymobIntentionResult(clientSecret, paymobOrderId);
    }

    // Egypt-specific - see PaymobOptions/Program.cs if this ever needs to
    // support another region (UAE/KSA/Oman have their own checkout hosts).
    public string BuildCheckoutUrl(string clientSecret) =>
        $"https://eg.checkout.paymob.com/?publicKey={options.PublicKey}&clientSecret={clientSecret}";

    // Full refund of a previously successful transaction - see Paymob's
    // "Refund" API docs. transactionId is Paymob's own transaction id (the
    // webhook callback's obj.id, persisted as Orders.PaymobTransactionId
    // when payment succeeded), not our own OrderId or Paymob's order id -
    // those are three different numbers. Same SecretKey Bearer auth as
    // CreateIntentionAsync; no separate token exchange needed. Both fields
    // in the request body are documented as strings even though they carry
    // integer values - matches Paymob's own example exactly rather than
    // guessing at a numeric encoding.
    //
    // virtual so tests can substitute a fake that never touches the network -
    // this app has no automated coverage that's allowed to hit Paymob's real
    // API (same reasoning as CreateIntentionAsync never being called from a
    // test), and refunding is real money, not something to risk on a typo.
    public virtual async Task<PaymobRefundResult> RefundAsync(long transactionId, decimal amountEgp)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "api/acceptance/void_refund/refund");
        request.Headers.Add("Authorization", $"Token {options.SecretKey}");
        request.Content = JsonContent.Create(new
        {
            transaction_id = transactionId.ToString(CultureInfo.InvariantCulture),
            amount_cents = ((int)Math.Round(amountEgp * 100)).ToString(CultureInfo.InvariantCulture)
        });

        var response = await httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode) return new PaymobRefundResult(false, null);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var success = body.TryGetProperty("success", out var successProp) && successProp.ValueKind == JsonValueKind.True;
        // "id" here is the refund's own new transaction id, not the original
        // charge's - Paymob creates a separate transaction record for every
        // refund, linked back to the original via its own parent_transaction
        // field.
        var refundTransactionId = success && body.TryGetProperty("id", out var idProp) && idProp.TryGetInt64(out var rid) ? rid : (long?)null;

        return new PaymobRefundResult(success, refundTransactionId);
    }

    // Paymob's classic/legacy API (Transaction Inquiry, unlike Intention/
    // Refund) authenticates via a short-lived session token exchanged from
    // the ApiKey, not the SecretKey Bearer header - see "Authentication
    // Request (Generate Auth Token)" in their docs. Fetched fresh on every
    // call rather than cached - this is only ever called from
    // ResumeCheckoutAsync, a rare, low-traffic path where an extra ~1s
    // round trip doesn't matter, and it avoids having to reason about
    // whether a cached token has expired.
    private async Task<string> GetAuthTokenAsync()
    {
        var response = await httpClient.PostAsJsonAsync("api/auth/tokens", new { api_key = options.ApiKey });
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("token").GetString()
            ?? throw new InvalidOperationException("Paymob auth response did not include a token.");
    }

    // Asks Paymob directly whether a checkout attempt for this exact
    // special_reference has already succeeded - see
    // clsOrderBusiness.ResumeCheckoutAsync, which calls this before ever
    // opening a second payment window for an order, specifically to close
    // the double-charge race where a first payment succeeds right before
    // the student taps "Continue to Payment" again. Not found (no matching
    // transaction at all - nobody ever completed a payment attempt for this
    // reference, or the reference is simply invalid) is treated the same as
    // "not paid yet", not as an error - the caller should proceed with a
    // normal new checkout attempt in that case.
    //
    // virtual for the same reason RefundAsync is - tests must never risk a
    // real call to Paymob's API.
    public virtual async Task<PaymobInquiryResult> InquireByMerchantOrderIdAsync(string merchantOrderId)
    {
        var authToken = await GetAuthTokenAsync();

        var response = await httpClient.PostAsJsonAsync("api/ecommerce/orders/transaction_inquiry", new
        {
            auth_token = authToken,
            merchant_order_id = merchantOrderId
        });
        if (!response.IsSuccessStatusCode) return new PaymobInquiryResult(false, false, null, null);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return ParseInquiryResponse(body);
    }

    // Extracted so the (verified-live, not guessed) is_refund/is_void
    // handling below can be unit-tested against a raw JSON payload without
    // any network call - see PaymobInquiryParsingTests.
    //
    // This endpoint returns the LAST transaction for the reference, not
    // specifically the last payment - verified live against a real refunded
    // order: once cancelled-and-refunded, the "last transaction" it returns
    // IS the refund itself (is_refund: true), still carrying success: true,
    // with the original payment's id only reachable via parent_transaction.
    // Blindly trusting success here would misread "this order was refunded"
    // as "this order was just paid" - a refund/void is never a successful
    // NEW payment for ResumeCheckoutAsync's purposes, regardless of what
    // "success" says.
    public static PaymobInquiryResult ParseInquiryResponse(JsonElement body)
    {
        if (!body.TryGetProperty("id", out var idProp) || !idProp.TryGetInt64(out var transactionId))
            return new PaymobInquiryResult(false, false, null, null);

        var isRefundOrVoid =
            (body.TryGetProperty("is_refund", out var isRefundProp) && isRefundProp.ValueKind == JsonValueKind.True) ||
            (body.TryGetProperty("is_void", out var isVoidProp) && isVoidProp.ValueKind == JsonValueKind.True);

        var success = !isRefundOrVoid
            && body.TryGetProperty("success", out var successProp) && successProp.ValueKind == JsonValueKind.True;
        var amountEgp = body.TryGetProperty("amount_cents", out var amountProp) ? amountProp.GetInt64() / 100m : (decimal?)null;

        return new PaymobInquiryResult(true, success, transactionId, amountEgp);
    }

    // Exact field list/order from Paymob's "HMAC Transaction Callback" docs -
    // already lexicographically sorted by key name, not something to
    // re-derive. obj.id is the transaction id; order.id (nested under obj) is
    // the Paymob order id - both literally named "id" at different nesting
    // levels, easy to conflate, listed explicitly here to avoid that.
    private static readonly string[] HmacFieldPaths =
    [
        "obj.amount_cents",
        "obj.created_at",
        "obj.currency",
        "obj.error_occured",
        "obj.has_parent_transaction",
        "obj.id",
        "obj.integration_id",
        "obj.is_3d_secure",
        "obj.is_auth",
        "obj.is_capture",
        "obj.is_refunded",
        "obj.is_standalone_payment",
        "obj.is_voided",
        "obj.order.id",
        "obj.owner",
        "obj.pending",
        "obj.source_data.pan",
        "obj.source_data.sub_type",
        "obj.source_data.type",
        "obj.success"
    ];

    // Proves a transaction callback genuinely came from Paymob (and wasn't
    // spoofed/altered) before trusting anything in it - the callback is the
    // only thing this app ever trusts to mark an order as paid, so this check
    // is not optional. receivedHmacHex comes from the callback's own "hmac"
    // query-string parameter, not the JSON body.
    public bool VerifyTransactionCallback(JsonElement payload, string receivedHmacHex)
    {
        var concatenated = new StringBuilder();
        foreach (var path in HmacFieldPaths)
            concatenated.Append(GetRawValueAsString(payload, path));

        var computed = ComputeHmacSha512(concatenated.ToString(), options.HmacSecret);
        return string.Equals(computed, receivedHmacHex, StringComparison.OrdinalIgnoreCase);
    }

    // Public (not just private) specifically so the concatenation logic can be
    // unit-tested in isolation against Paymob's own documented sample, without
    // needing a real HMAC secret (which their docs never disclose for that
    // sample) - see PaymobHmacTests.
    public static string BuildHmacConcatenatedString(JsonElement payload)
    {
        var concatenated = new StringBuilder();
        foreach (var path in HmacFieldPaths)
            concatenated.Append(GetRawValueAsString(payload, path));
        return concatenated.ToString();
    }

    private static string GetRawValueAsString(JsonElement root, string dottedPath)
    {
        var current = root;
        foreach (var segment in dottedPath.Split('.'))
        {
            if (!current.TryGetProperty(segment, out var next))
                return ""; // field missing from this particular callback - treated as empty, not an error
            current = next;
        }

        return current.ValueKind switch
        {
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => "",
            JsonValueKind.String => current.GetString() ?? "",
            JsonValueKind.Number => current.GetRawText(),
            _ => current.GetRawText()
        };
    }

    private static string ComputeHmacSha512(string data, string secret)
    {
        using var hmac = new HMACSHA512(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
