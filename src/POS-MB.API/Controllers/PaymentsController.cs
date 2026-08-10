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
public class PaymentsController(PaymobClient paymobClient, clsOrderBusiness orderBusiness, ILogger<PaymentsController> logger) : ControllerBase
{
    [HttpPost("paymob-webhook")]
    [AllowAnonymous]
    public async Task<IActionResult> PaymobWebhook([FromQuery] string? hmac, [FromBody] JsonElement payload)
    {
        if (string.IsNullOrEmpty(hmac) || !paymobClient.VerifyTransactionCallback(payload, hmac))
        {
            logger.LogWarning("Rejected a Paymob webhook with an invalid/missing HMAC from {RemoteIp}",
                HttpContext.Connection.RemoteIpAddress);
            return Unauthorized();
        }

        if (!payload.TryGetProperty("obj", out var obj))
            return Ok(); // not a transaction-shaped callback - nothing for us to do, acknowledge anyway

        var success = obj.TryGetProperty("success", out var successProp) && successProp.ValueKind == JsonValueKind.True;
        var amountCents = obj.TryGetProperty("amount_cents", out var amountProp) ? amountProp.GetInt64() : 0;

        // special_reference (our own OrderId, set when the intention was
        // created) comes back here - see clsOrderBusiness.CreateOrderAsync's
        // Paymob path once that's wired up.
        var merchantOrderId = obj.TryGetProperty("order", out var orderProp) && orderProp.TryGetProperty("merchant_order_id", out var moidProp)
            ? moidProp.GetString()
            : null;

        if (merchantOrderId is null || !int.TryParse(merchantOrderId, out var orderId))
        {
            logger.LogWarning("Paymob webhook had a valid signature but no recognizable merchant_order_id");
            return Ok(); // valid callback, but not tied to one of our orders - nothing to retry
        }

        try
        {
            await orderBusiness.MarkOrderPaymentResultAsync(orderId, success, amountCents / 100m);
        }
        catch (InvalidOperationException ex)
        {
            // Amount mismatch (see MarkOrderPaymentResultAsync) - logged, not
            // thrown back at Paymob, since retrying wouldn't change the
            // outcome and this needs a human to look at it.
            logger.LogError(ex, "Paymob webhook amount mismatch for OrderId={OrderId}", orderId);
        }

        return Ok();
    }
}
