using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POS_MB.Business;
using POS_MB.Business.Payments;

namespace POS_MB.API.Controllers;

// Called directly by Paymob's servers, not by any of our own clients - there's
// no JWT here, the HMAC signature is the actual authentication. Kept as its
// own controller (not folded into StudentOrdersController) since the caller,
// the trust model, and the request shape are all completely different from
// every other endpoint in this API.
[ApiController]
[Route("api/payments")]
public class PaymentsController(
    PaymobClient paymobClient, clsOrderBusiness orderBusiness, clsStudentBusiness studentBusiness, ILogger<PaymentsController> logger) : ControllerBase
{
    // Paymob sends two genuinely different callback shapes to this same URL -
    // a payment result ("TRANSACTION", the only kind that existed here until
    // the "save card" feature) and a saved-card notification ("TOKEN"). Each
    // has its own HMAC field list (see PaymobClient), so the type has to be
    // read BEFORE picking which verification to even attempt - trying to
    // verify a token callback's signature with the transaction field list
    // (or vice versa) will simply never match, silently rejecting a
    // genuine callback as unauthorized.
    [HttpPost("paymob-webhook")]
    [AllowAnonymous]
    public async Task<IActionResult> PaymobWebhook([FromQuery] string? hmac, [FromBody] JsonElement payload)
    {
        if (string.IsNullOrEmpty(hmac))
            return Unauthorized();

        var type = payload.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : null;

        if (type == "TOKEN")
            return await HandleCardTokenCallbackAsync(payload, hmac);

        if (!paymobClient.VerifyTransactionCallback(payload, hmac))
        {
            logger.LogWarning("Rejected a Paymob webhook with an invalid/missing HMAC from {RemoteIp}",
                HttpContext.Connection.RemoteIpAddress);
            return Unauthorized();
        }

        if (!payload.TryGetProperty("obj", out var obj))
            return Ok(); // not a transaction-shaped callback - nothing for us to do, acknowledge anyway

        var success = obj.TryGetProperty("success", out var successProp) && successProp.ValueKind == JsonValueKind.True;
        var amountCents = obj.TryGetProperty("amount_cents", out var amountProp) ? amountProp.GetInt64() : 0;

        // Paymob's own transaction id - persisted on success so a later
        // cancellation can refund this exact transaction (see
        // clsOrderBusiness.RefundIfPaidAsync). Not our OrderId, not
        // Paymob's order id - obj.id is the transaction itself.
        var transactionId = obj.TryGetProperty("id", out var idProp) && idProp.TryGetInt64(out var tid) ? tid : (long?)null;

        // special_reference (see PaymobOrderReference - deliberately not the
        // raw OrderId, since Paymob's own emails display this to the
        // customer) comes back here.
        var merchantOrderId = obj.TryGetProperty("order", out var orderProp) && orderProp.TryGetProperty("merchant_order_id", out var moidProp)
            ? moidProp.GetString()
            : null;

        if (merchantOrderId is null || !PaymobOrderReference.TryParse(merchantOrderId, out var orderDate, out var serialNumber))
        {
            logger.LogWarning("Paymob webhook had a valid signature but no recognizable order reference: {Reference}", merchantOrderId);
            return Ok(); // valid callback, but not tied to one of our orders - nothing to retry
        }

        var order = await orderBusiness.GetByDateAndSerialNumberAsync(orderDate, serialNumber);
        if (order is null)
        {
            logger.LogWarning("Paymob webhook referenced an order that could not be found: {Reference}", merchantOrderId);
            return Ok();
        }

        try
        {
            await orderBusiness.MarkOrderPaymentResultAsync(order.OrderId, success, amountCents / 100m, transactionId);
        }
        catch (InvalidOperationException ex)
        {
            // Amount mismatch (see MarkOrderPaymentResultAsync) - logged, not
            // thrown back at Paymob, since retrying wouldn't change the
            // outcome and this needs a human to look at it.
            logger.LogError(ex, "Paymob webhook amount mismatch for OrderId={OrderId}", order.OrderId);
        }

        return Ok();
    }

    // Fired when a student checks "save this card" on Paymob's own checkout
    // page (see clsStudentBusiness.SaveCardTokenAsync). Matched by email,
    // not order id - Paymob's order_id here is THEIR numeric order id, not
    // our special_reference, so it can't be used to look up the order the
    // way the transaction callback does; email is the one field this
    // callback carries that correlates directly to one of our accounts.
    private async Task<IActionResult> HandleCardTokenCallbackAsync(JsonElement payload, string hmac)
    {
        if (!paymobClient.VerifyCardTokenCallback(payload, hmac))
        {
            logger.LogWarning("Rejected a Paymob card-token webhook with an invalid HMAC from {RemoteIp}",
                HttpContext.Connection.RemoteIpAddress);
            return Unauthorized();
        }

        if (!payload.TryGetProperty("obj", out var obj))
            return Ok();

        var token = obj.TryGetProperty("token", out var tokenProp) ? tokenProp.GetString() : null;
        var email = obj.TryGetProperty("email", out var emailProp) ? emailProp.GetString() : null;
        var maskedPan = obj.TryGetProperty("masked_pan", out var panProp) ? panProp.GetString() : null;
        var cardSubtype = obj.TryGetProperty("card_subtype", out var subtypeProp) ? subtypeProp.GetString() : null;

        if (token is null || email is null)
        {
            logger.LogWarning("Paymob card-token webhook had a valid signature but was missing a token or email - ignoring.");
            return Ok();
        }

        await studentBusiness.SaveCardTokenAsync(email, token, maskedPan, cardSubtype);
        return Ok();
    }
}
