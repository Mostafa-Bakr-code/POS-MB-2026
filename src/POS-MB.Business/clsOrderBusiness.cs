using System.Globalization;
using Microsoft.Extensions.Logging;
using POS_MB.Business.Payments;
using POS_MB.DataAccess;
using POS_MB.DataAccess.Models;

namespace POS_MB.Business;

public class clsOrderBusiness(clsOrderDataAccess dataAccess, clsSettingsBusiness settingsBusiness, PaymobClient paymobClient, ILogger<clsOrderBusiness> logger, clsStudentBusiness studentBusiness)
{
    // Paymob's own intention has an "expiration" too - matches this app's
    // own auto-cancel default (see MobileOrderAutoCancelMinutesSettingKey)
    // so neither one meaningfully outlives the other.
    private const int PaymentExpirationSeconds = 600;


    // Key is missing entirely until staff first touches the toggle - treated as
    // "accepting" (the safe/normal default) so nothing needs seeding/migrating.
    public const string AcceptingOnlineOrdersSettingKey = "AcceptingOnlineOrders";

    // Updated every ~15s by the WinForms Order Status screen while it's open
    // (see RecordHeartbeatAsync) - this is deliberately tied to that specific
    // screen, not just "the API is reachable from the shop", since an order
    // sitting Placed with nobody watching the queue is exactly the "black
    // hole" scenario this is meant to catch (the chef's Accept step can't
    // protect against it - accepting requires seeing the order first).
    public const string ShopHeartbeatSettingKey = "ShopHeartbeatUtc";

    // A few missed 15s ticks (a slow request, a brief blip) shouldn't trip
    // this - only a real gap should. Comfortably above 15s*a few, comfortably
    // under the "try again in a few minutes" the mobile message promises.
    private static readonly TimeSpan HeartbeatStaleThreshold = TimeSpan.FromSeconds(90);

    public async Task<int> CreateOrderAsync(OrderSource orderSource, int? userId, int? studentId, bool isComplimentary, IReadOnlyList<NewOrderItem> items)
    {
        if (items.Count == 0)
            throw new ArgumentException("An order must have at least one item.", nameof(items));

        foreach (var item in items)
        {
            if (item.Quantity <= 0)
                throw new ArgumentException($"Quantity for item {item.ItemId} must be greater than zero.", nameof(items));
        }

        if (orderSource == OrderSource.Cashier && userId is null)
            throw new ArgumentException("A cashier order must specify which staff user placed it.", nameof(userId));
        if (orderSource == OrderSource.Mobile && studentId is null)
            throw new ArgumentException("A mobile order must specify which student placed it.", nameof(studentId));

        // Checked here, not just reflected in the mobile menu banner, since a
        // student's app could already have a cart built from before the
        // toggle flipped or the shop went quiet. Cashier orders are never
        // affected - a cashier taking an order in person isn't "online".
        if (orderSource == OrderSource.Mobile)
        {
            var (isAccepting, reason) = await GetAcceptingOnlineOrdersStatusAsync();
            if (!isAccepting)
                throw new ArgumentException(reason, nameof(orderSource));
        }

        return await dataAccess.CreateOrderAsync(orderSource, userId, studentId, isComplimentary, items);
    }

    // Single source of truth for "can a mobile order be placed right now" -
    // used both to enforce CreateOrderAsync above and to drive the mobile
    // menu's banner, so the two can never disagree about why. Two independent
    // reasons a mobile order can be blocked: staff manually paused it, or
    // nobody's been watching the Order Status queue recently enough to trust
    // that a new order would actually be seen.
    public async Task<(bool IsAccepting, string? Reason)> GetAcceptingOnlineOrdersStatusAsync()
    {
        var toggle = await settingsBusiness.GetByKeyAsync(AcceptingOnlineOrdersSettingKey);
        if (toggle?.Value == "false")
            return (false, "We're not accepting online orders right now - please try again later.");

        var heartbeat = await settingsBusiness.GetByKeyAsync(ShopHeartbeatSettingKey);
        var isStale = heartbeat?.Value is null
            || !DateTime.TryParse(heartbeat.Value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var lastSeenUtc)
            || DateTime.UtcNow - lastSeenUtc > HeartbeatStaleThreshold;

        if (isStale)
            return (false, "We're temporarily unable to accept online orders - please try again in a few minutes.");

        return (true, null);
    }

    public Task RecordHeartbeatAsync() =>
        settingsBusiness.SetAsync(ShopHeartbeatSettingKey, DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));

    // Complimentary (free) orders are a staff-only concept - a student ordering
    // for themselves is never "complimentary", that would mean giving away food
    // for free with no staff decision behind it.
    //
    // Price/items are locked in by CreateOrderAsync exactly like any other
    // order, but a mobile order isn't real yet until Paymob confirms payment -
    // moved to AwaitingPayment immediately after creation, invisible to the
    // kitchen/Order Status screen, until the webhook (PaymentsController)
    // confirms it and moves it to Placed. Deliberately doesn't change
    // CreateOrderAsync/the DataAccess layer's own default status for a Mobile
    // order - that stays Placed, since plenty of other call sites (tests,
    // any future direct-creation path) rely on that and have nothing to do
    // with payment.
    // useSavedCard: the student explicitly chose "pay with my saved card" at
    // checkout - not the default, since a student should always know
    // whether they're about to skip re-entering card details or not, never
    // have it silently assumed for them. Silently falls back to a normal
    // checkout (asking for card details) if they don't actually have one
    // saved - the mobile app is expected to only offer this option when it
    // already knows a saved card exists, so this is a defensive fallback,
    // not the primary guard.
    public async Task<(int OrderId, string CheckoutUrl)> CreateStudentOrderAsync(int studentId, string studentEmail, IReadOnlyList<NewOrderItem> items, bool useSavedCard = false)
    {
        var orderId = await CreateOrderAsync(OrderSource.Mobile, userId: null, studentId, isComplimentary: false, items);
        await dataAccess.UpdateStatusAsync(orderId, OrderStatus.AwaitingPayment);

        var order = await dataAccess.GetByIdAsync(orderId)
            ?? throw new InvalidOperationException($"Order {orderId} was just created but could not be found.");

        string? savedCardToken = null;
        if (useSavedCard)
        {
            var student = await studentBusiness.GetByIdAsync(studentId);
            savedCardToken = student?.SavedCardToken;
        }

        var checkoutUrl = await StartPaymobCheckoutAsync(order, studentEmail, isRetry: false, savedCardToken);
        return (orderId, checkoutUrl);
    }

    // A student backing out of the in-app payment screen (or the app
    // crashing, a connectivity blip mid-checkout) leaves the order sitting
    // at AwaitingPayment with no way forward otherwise - this starts a fresh
    // Paymob checkout session for that same order rather than making them
    // wait out the auto-cancel timeout with no recourse.
    //
    // Before ever opening that second payment window, this asks Paymob
    // directly whether the previous attempt already succeeded - closing a
    // real double-charge race: our own database might still show
    // AwaitingPayment for a few seconds after a payment has genuinely
    // succeeded on Paymob's side, simply because the webhook confirming it
    // hasn't arrived yet. Trusting our own stale copy here would hand out a
    // second, real, working payment link for an order that's already paid.
    // useSavedCard: whether THIS retry should offer the saved card again -
    // not remembered from whatever the original attempt chose (that isn't
    // tracked anywhere), so the caller (mobile app) decides based on whether
    // the student currently has one on file. Without this, a resumed
    // checkout would always silently fall back to asking for card details
    // again, even for a student who specifically chose "pay with saved
    // card" the first time - a real bug found live: the original attempt
    // had actually succeeded, but the student, unsure it was working,
    // backed out and hit "Continue Payment" while waiting - which used to
    // drop the saved-card choice entirely.
    // AlreadyPaid: true whenever there's nothing left to resume because the
    // order genuinely already succeeded - found live as a real UX bug: this
    // used to throw for that case, which a client had no way to distinguish
    // from an actual error, surfacing as a scary "Error" alert for what's
    // actually good news (the payment worked). A student backing out of a
    // saved-card payment mid-processing and tapping "Continue Payment"
    // before the webhook had a chance to land was exactly this situation.
    public async Task<(string? CheckoutUrl, bool AlreadyPaid)> ResumeCheckoutAsync(int orderId, int studentId, string studentEmail, bool useSavedCard = false)
    {
        var order = await dataAccess.GetByIdForStudentAsync(orderId, studentId)
            ?? throw new ArgumentException("This order could not be found.", nameof(orderId));

        if (order.Status != OrderStatus.AwaitingPayment)
        {
            // Cancelled is deliberately excluded even if it somehow carries
            // a transaction id (e.g. a very late payment landed after
            // cancellation and was refunded) - that one genuinely has
            // nothing to resume to, and "already paid" would be a
            // misleading thing to tell the student about a cancelled order.
            if (order.PaymobTransactionId is not null && order.Status != OrderStatus.Cancelled)
                return (null, true);

            throw new ArgumentException("This order is not waiting for payment.", nameof(orderId));
        }

        if (order.LastPaymobReference is not null)
        {
            var inquiry = await paymobClient.InquireByMerchantOrderIdAsync(order.LastPaymobReference);
            if (inquiry is { Found: true, Success: true, TransactionId: long transactionId })
            {
                // Reuses the exact same race-safe reconciliation a late
                // webhook goes through - this genuinely is that same
                // situation, just discovered by asking instead of waiting
                // to be told.
                await MarkOrderPaymentResultAsync(orderId, paymentSucceeded: true, inquiry.AmountEgp ?? order.Total, transactionId);
                return (null, true);
            }
        }

        string? savedCardToken = null;
        if (useSavedCard)
        {
            var student = await studentBusiness.GetByIdAsync(studentId);
            savedCardToken = student?.SavedCardToken;
        }

        var checkoutUrl = await StartPaymobCheckoutAsync(order, studentEmail, isRetry: true, savedCardToken);
        return (checkoutUrl, false);
    }

    private async Task<string> StartPaymobCheckoutAsync(Order order, string studentEmail, bool isRetry, string? savedCardToken = null)
    {
        var serialNumber = order.SerialNumber
            ?? throw new InvalidOperationException($"Order {order.OrderId} has no SerialNumber - cannot start a Paymob checkout for it.");
        var reference = isRetry
            ? PaymobOrderReference.BuildRetry(order.Date, serialNumber)
            : PaymobOrderReference.Build(order.Date, serialNumber);

        var orderItems = await dataAccess.GetItemsWithNamesByOrderIdAsync(order.OrderId);
        // Per-unit price, not the line total - verified live against
        // Paymob's real API: it multiplies amount by quantity itself to
        // validate the items sum to the order total, and rejects the
        // request (406 "unmatched_item_prices") if given the line total
        // instead.
        var itemLines = orderItems
            .Select(oi => new PaymobItemLine(oi.ItemName ?? $"Item #{oi.ItemId}", oi.Price, oi.Quantity, oi.Comment ?? ""))
            .ToList();

        var intention = await paymobClient.CreateIntentionAsync(order.Total, reference, studentEmail, PaymentExpirationSeconds, itemLines, savedCardToken);

        // Remembered so a future ResumeCheckoutAsync call can ask Paymob
        // whether THIS specific attempt succeeded before opening yet
        // another one.
        await dataAccess.SetLastPaymobReferenceAsync(order.OrderId, reference);

        return paymobClient.BuildCheckoutUrl(intention.ClientSecret);
    }

    // Defaults to today only (see StudentOrdersController) so a student isn't
    // shown their entire order history every time they open the app - same
    // timezone-resolution logic as the staff-facing GetAllAsync, so "today"
    // means the student's actual local day, not the server's UTC day.
    public async Task<IEnumerable<Order>> GetAllForStudentAsync(int studentId, DateTime? startDate = null, DateTime? endDate = null)
    {
        var (utcStart, utcEndExclusive) = await TimeZoneHelper.ResolveUtcRangeAsync(settingsBusiness, startDate, endDate);

        return await dataAccess.GetAllForStudentAsync(studentId, utcStart, utcEndExclusive);
    }

    // Lets a student's order-detail screen (which polls this every few
    // seconds) notice a payment succeeding as soon as Paymob confirms it,
    // instead of only finding out passively once the webhook lands - the
    // same reconciliation the auto-cancel sweep already uses (see
    // WasActuallyPaidAsync), just triggered by "someone's watching" rather
    // than "the timeout expired". Found live: without this, a saved-card
    // payment that took a few minutes to confirm left the student staring
    // at a stale "Continue Payment" button the whole time, tempting them to
    // tap it and hit the (now-fixed, but still needless) already-paid path.
    // Best-effort and silent by design - a poll that doesn't notice this
    // instant just tries again next tick, same as any other polled update.
    public async Task<Order> ReconcileIfAwaitingPaymentAsync(Order order)
    {
        if (order.Status != OrderStatus.AwaitingPayment) return order;

        return await WasActuallyPaidAsync(order)
            ? await dataAccess.GetByIdAsync(order.OrderId) ?? order
            : order;
    }

    public Task<Order?> GetByIdForStudentAsync(int orderId, int studentId) =>
        dataAccess.GetByIdForStudentAsync(orderId, studentId);

    // A student can self-cancel while AwaitingPayment (nothing committed yet -
    // not even payment) or Placed (order exists, but the kitchen hasn't
    // started on it) - once it's Preparing, real resources (ingredients, the
    // chef's time) are already committed, so cancelling at that point wastes
    // them for nothing. Staff retain a broader override via
    // clsOrderBusiness.CancelAsync (used from the WinForms Order Status
    // screen) - that's a deliberate difference in privilege, not an oversight.
    public async Task<bool> CancelForStudentAsync(int orderId, int studentId)
    {
        var order = await dataAccess.GetByIdForStudentAsync(orderId, studentId);
        if (order is null) return false; // not found / not theirs - controller returns 404

        if (order.Status is not (OrderStatus.AwaitingPayment or OrderStatus.Placed))
            throw new ArgumentException("This order can no longer be cancelled - the kitchen has already started preparing it.", nameof(orderId));

        var cancelled = await dataAccess.CancelForStudentAsync(orderId, studentId);
        if (cancelled)
            await RefundIfPaidAsync(order);

        return cancelled;
    }

    public Task<Order?> GetByDateAndSerialNumberAsync(DateTime orderDateUtc, int serialNumber) =>
        dataAccess.GetByDateAndSerialNumberAsync(orderDateUtc, serialNumber);

    public Task<Order?> GetByIdAsync(int id) =>
        dataAccess.GetByIdAsync(id);

    public Task<IEnumerable<OrderItem>> GetItemsByOrderIdAsync(int orderId) =>
        dataAccess.GetItemsByOrderIdAsync(orderId);

    public Task<IEnumerable<Order>> GetOrdersNeedingKitchenTicketAsync() =>
        dataAccess.GetOrdersNeedingKitchenTicketAsync();

    public Task<bool> MarkKitchenTicketPrintedAsync(int orderId) =>
        dataAccess.MarkKitchenTicketPrintedAsync(orderId);

    public async Task<IEnumerable<Order>> GetAllAsync(DateTime? startDate = null, DateTime? endDate = null, OrderSource? orderSource = null)
    {
        var (utcStart, utcEndExclusive) = await TimeZoneHelper.ResolveUtcRangeAsync(settingsBusiness, startDate, endDate);

        return await dataAccess.GetAllAsync(utcStart, utcEndExclusive, orderSource);
    }

    public Task<bool> UpdateStatusAsync(int id, OrderStatus status) =>
        dataAccess.UpdateStatusAsync(id, status);

    // Called by the webhook controller after it has already verified the
    // Paymob callback's HMAC signature - this method trusts its inputs, HMAC
    // verification is the caller's job (see PaymentsController), not
    // duplicated here.
    //
    // Idempotent on purpose: Paymob (like most webhook systems) can retry
    // delivery, so a second delivery of a callback we've already fully
    // processed must be a harmless no-op instead of double-processing an
    // order that already moved on.
    public async Task MarkOrderPaymentResultAsync(int orderId, bool paymentSucceeded, decimal amountEgpPaid, long? transactionId)
    {
        var order = await dataAccess.GetByIdAsync(orderId);
        if (order is null) return;

        if (order.Status == OrderStatus.AwaitingPayment)
        {
            if (!paymentSucceeded)
            {
                await dataAccess.UpdateStatusAsync(orderId, OrderStatus.Cancelled);
                return;
            }

            // The HMAC proves the callback is genuinely from Paymob, but not
            // that it's reporting the amount we actually asked for - this is
            // a cheap extra check that the two agree before treating the
            // order as real, rather than trusting "success: true" alone.
            if (Math.Abs(amountEgpPaid - order.Total) > 0.01m)
                throw new InvalidOperationException($"Paid amount {amountEgpPaid} does not match order total {order.Total} for OrderId={orderId}.");

            // obj.id is one of the fields HMAC-verified as part of the
            // callback itself, so a genuine successful callback always
            // carries it - this is a defensive guard, not an expected path.
            if (transactionId is null)
                throw new InvalidOperationException($"Paymob webhook reported success but had no transaction id for OrderId={orderId}.");

            // Reading Status above and writing here are two separate steps,
            // not one atomic operation - a concurrent cancel can land in
            // between them. MarkPaidAsync's own WHERE clause re-checks
            // Status = AwaitingPayment at the moment it actually writes, so
            // if that guard fails, the order genuinely moved on between our
            // read and this write (a real race, not just a hypothetical
            // one) - re-fetch the current truth and handle it exactly like
            // any other "payment landed after the order moved on" case
            // below, instead of assuming success.
            if (await dataAccess.MarkPaidAsync(orderId, transactionId.Value))
                return;

            order = await dataAccess.GetByIdAsync(orderId);
            if (order is null) return;
        }

        await HandleLateOrDuplicatePaymentAsync(order, paymentSucceeded, amountEgpPaid, transactionId);
    }

    // Reached whenever a payment result callback arrives for an order that
    // is not (or is no longer) AwaitingPayment - either it already moved on
    // before the callback arrived, or it moved on in the narrow window
    // between this method's initial read and its own write (see the
    // MarkPaidAsync race-guard above).
    private async Task HandleLateOrDuplicatePaymentAsync(Order order, bool paymentSucceeded, decimal amountEgpPaid, long? transactionId)
    {
        // A failed payment for an order that's no longer AwaitingPayment is
        // a harmless no-op - nothing was ever charged, so there's nothing
        // to undo (same as a duplicate delivery of a callback already
        // processed).
        if (!paymentSucceeded || transactionId is null) return;

        // The exact same successful transaction we've already recorded for
        // this order, delivered again - Paymob's own retry behavior, not a
        // second real charge. True no-op.
        if (order.PaymobTransactionId == transactionId) return;

        if (order.PaymobTransactionId is null)
        {
            // First time we're hearing about ANY charge for this order, and
            // it's no longer AwaitingPayment - most commonly, it's already
            // Cancelled and the charge landed just after (a student self-
            // cancelling in the same window the payment was completing, or
            // the abandoned-payment sweep firing at an unlucky moment).
            // Silently dropping this would mean real money taken with zero
            // record and no refund - instead, record it and refund it
            // immediately, exactly like any other paid-then-cancelled
            // order (see RefundIfPaidAsync). Deliberately does NOT
            // resurrect a Cancelled order back to Placed - the kitchen/
            // system already moved on from it, possibly with real
            // consequences (ingredients reallocated), so quietly
            // un-cancelling it would be its own kind of surprise.
            await dataAccess.RecordPaymobTransactionIdAsync(order.OrderId, transactionId.Value);
            order.PaymobTransactionId = transactionId;
            // amountEgpPaid, not order.Total - refund exactly what this
            // transaction actually holds. Paymob rejects a refund request
            // for more than the transaction's own amount (a documented
            // error: "Requested Refund Amount is greater than the maximum
            // refund amount permissible"), so trusting order.Total here
            // instead of the amount this specific callback actually
            // reported would risk that exact failure if the two ever
            // disagreed, however unlikely that normally is.
            await RefundIfPaidAsync(order, amountEgpPaid);
            return;
        }

        // The order already has a different, legitimate transaction on
        // file - this is a genuine second, extra real charge, not a
        // duplicate callback (e.g. a resumed checkout that was actually
        // paid a second time in the narrow window before the first
        // payment's webhook had a chance to move the order out of
        // AwaitingPayment - see ResumeCheckoutAsync). The order was only
        // ever meant to be charged once, so this stray charge gets refunded
        // on its own - the order's own recorded transaction/status is left
        // exactly as it was, since that one is the legitimate one.
        await RefundStrayChargeAsync(order.OrderId, transactionId.Value, amountEgpPaid);
    }

    private async Task RefundStrayChargeAsync(int orderId, long transactionId, decimal amountEgp)
    {
        try
        {
            var result = await paymobClient.RefundAsync(transactionId, amountEgp);
            if (result.Success)
                logger.LogWarning(
                    "Refunded a duplicate/stray Paymob charge for OrderId={OrderId}, TransactionId={TransactionId} - this order already had a different transaction on file.",
                    orderId, transactionId);
            else
                logger.LogError(
                    "Paymob rejected refunding a duplicate/stray charge for OrderId={OrderId}, TransactionId={TransactionId} - needs manual review.",
                    orderId, transactionId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Refunding a duplicate/stray Paymob charge threw for OrderId={OrderId}, TransactionId={TransactionId} - needs manual review.",
                orderId, transactionId);
        }
    }

    // cancelledBy: who/what is cancelling - "Staff: {username}" from the
    // WinForms Order Status screen, or one of the two auto-cancel sweep
    // reasons below. Defaults to a plain "Staff" for callers (mostly tests)
    // that don't care to be specific.
    public async Task<bool> CancelAsync(int id, string cancelledBy = "Staff")
    {
        var order = await dataAccess.GetByIdAsync(id);
        if (order is null) return false;

        var cancelled = await dataAccess.CancelAsync(id, cancelledBy);
        if (cancelled)
            await RefundIfPaidAsync(order);

        return cancelled;
    }

    // Best-effort, never blocks or fails the cancellation itself - an order
    // should always actually cancel even if Paymob is unreachable right now.
    // A failed/rejected refund is logged loudly instead, so a human can
    // process it manually via Paymob's own dashboard rather than it silently
    // vanishing. The PaymobTransactionId/RefundedAt guards mean this is a
    // no-op for anything never paid through Paymob (Cashier orders, or a
    // Mobile order that never completed checkout) or already refunded.
    // amountEgpOverride: when refunding an order the normal way (cancelled
    // via CancelAsync/CancelForStudentAsync), order.Total is exactly what
    // was validated against the original payment when it was first marked
    // paid (see the AwaitingPayment branch of MarkOrderPaymentResultAsync),
    // so it's safe to trust here. The late-arrival caller passes the
    // callback's own reported amount instead, since order.Total is only an
    // expectation there, never actually verified against this particular
    // transaction.
    private async Task RefundIfPaidAsync(Order order, decimal? amountEgpOverride = null)
    {
        if (order.PaymobTransactionId is null || order.RefundedAt is not null) return;

        var amountEgp = amountEgpOverride ?? order.Total;

        try
        {
            var result = await paymobClient.RefundAsync(order.PaymobTransactionId.Value, amountEgp);
            if (result is { Success: true, RefundTransactionId: long refundTransactionId })
                await dataAccess.MarkRefundedAsync(order.OrderId, refundTransactionId);
            else
                logger.LogError(
                    "Paymob refund was rejected for OrderId={OrderId}, PaymobTransactionId={TransactionId} - needs manual review.",
                    order.OrderId, order.PaymobTransactionId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Paymob refund threw for OrderId={OrderId}, PaymobTransactionId={TransactionId} - needs manual review.",
                order.OrderId, order.PaymobTransactionId);
        }
    }

    // Key missing entirely (never touched) falls back to DefaultAutoCancelMinutes -
    // same "no seeding needed" reasoning as the other resilience settings.
    public const string MobileOrderAutoCancelMinutesSettingKey = "MobileOrderAutoCancelMinutes";
    private const int DefaultAutoCancelMinutes = 10;

    // The safety net that catches whatever slips past the manual toggle and
    // heartbeat check above: a mobile order that's sat at Placed too long,
    // for any reason (a connectivity blip too brief to trip the heartbeat
    // check, a genuinely busy kitchen, a crashed app, anything). Reuses
    // CancelAsync - the exact same cancellation staff/students already
    // trigger by hand - so if a refund step is ever added there (once
    // Paymob exists), this inherits it automatically with no changes here.
    // Called periodically by MobileOrderAutoCancelService in the API project.
    public async Task<int> CancelStaleMobileOrdersAsync()
    {
        var setting = await settingsBusiness.GetByKeyAsync(MobileOrderAutoCancelMinutesSettingKey);
        var minutes = setting?.Value is not null && int.TryParse(setting.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : DefaultAutoCancelMinutes;

        var cutoffUtc = DateTime.UtcNow.AddMinutes(-minutes);
        // useUpdatedAt: true - the timeout measures how long the kitchen has
        // had a visible, un-accepted order, not how long ago checkout
        // started (that's a completely different window, already covered by
        // CancelAbandonedPaymentsAsync below). See GetStaleMobileOrdersAsync.
        var staleOrders = await dataAccess.GetStaleMobileOrdersAsync(OrderStatus.Placed, cutoffUtc, useUpdatedAt: true);

        var cancelledCount = 0;
        foreach (var order in staleOrders)
        {
            if (await CancelAsync(order.OrderId, "Auto (kitchen didn't accept in time)"))
                cancelledCount++;
        }

        return cancelledCount;
    }

    // A different, narrower safety net from CancelStaleMobileOrdersAsync above -
    // this one is specifically for a checkout that never finished at all (the
    // student closed the app mid-payment, the WebView crashed, they just gave
    // up). Uses the same window as Paymob's own intention expiration
    // (PaymentExpirationSeconds) rather than a separate setting - there's no
    // reason to keep an order around waiting for a payment session that
    // Paymob itself has already expired on its end.
    //
    // Before cancelling any of these, this asks Paymob directly whether it
    // was actually paid - closing the same gap ResumeCheckoutAsync closes,
    // but for the case where nobody's around to trigger a resume. A webhook
    // is a one-shot delivery attempt; if this server was briefly unreachable
    // (a deploy, a crash, a network blip - exactly the kind of thing that
    // happened repeatedly tonight) at the moment Paymob tried to deliver it,
    // the payment can succeed for real while this app never finds out, and
    // this sweep would otherwise cancel an order that was genuinely paid for
    // - real money taken, order lost. Reusing the inquiry here means a paid
    // order always gets reconciled correctly regardless of whether the
    // webhook ever arrives.
    public async Task<int> CancelAbandonedPaymentsAsync()
    {
        var cutoffUtc = DateTime.UtcNow.AddSeconds(-PaymentExpirationSeconds);
        var staleOrders = await dataAccess.GetStaleMobileOrdersAsync(OrderStatus.AwaitingPayment, cutoffUtc);

        var cancelledCount = 0;
        foreach (var order in staleOrders)
        {
            if (await WasActuallyPaidAsync(order))
                continue; // reconciled as paid instead of cancelled - see MarkOrderPaymentResultAsync

            if (await CancelAsync(order.OrderId, "Auto (payment abandoned)"))
                cancelledCount++;
        }

        return cancelledCount;
    }

    // Best-effort: if Paymob's inquiry API is itself unreachable, this falls
    // back to the old behavior (cancel as unpaid) rather than blocking the
    // whole sweep - the resume-checkout path and this same reconciliation on
    // a later sweep run remain as further safety nets for that rarer case.
    private async Task<bool> WasActuallyPaidAsync(Order order)
    {
        if (order.LastPaymobReference is null) return false;

        try
        {
            var inquiry = await paymobClient.InquireByMerchantOrderIdAsync(order.LastPaymobReference);
            if (inquiry is not { Found: true, Success: true, TransactionId: long transactionId })
                return false;

            await MarkOrderPaymentResultAsync(order.OrderId, paymentSucceeded: true, inquiry.AmountEgp ?? order.Total, transactionId);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Paymob inquiry threw while checking OrderId={OrderId} before auto-cancelling it - falling back to cancelling as unpaid.",
                order.OrderId);
            return false;
        }
    }
}
