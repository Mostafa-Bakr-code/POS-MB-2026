using System.Globalization;
using POS_MB.DataAccess;
using POS_MB.DataAccess.Models;

namespace POS_MB.Business;

public class clsOrderBusiness(clsOrderDataAccess dataAccess, clsSettingsBusiness settingsBusiness)
{
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
    public Task<int> CreateStudentOrderAsync(int studentId, IReadOnlyList<NewOrderItem> items) =>
        CreateOrderAsync(OrderSource.Mobile, userId: null, studentId, isComplimentary: false, items);

    // Defaults to today only (see StudentOrdersController) so a student isn't
    // shown their entire order history every time they open the app - same
    // timezone-resolution logic as the staff-facing GetAllAsync, so "today"
    // means the student's actual local day, not the server's UTC day.
    public async Task<IEnumerable<Order>> GetAllForStudentAsync(int studentId, DateTime? startDate = null, DateTime? endDate = null)
    {
        var (utcStart, utcEndExclusive) = await TimeZoneHelper.ResolveUtcRangeAsync(settingsBusiness, startDate, endDate);

        return await dataAccess.GetAllForStudentAsync(studentId, utcStart, utcEndExclusive);
    }

    public Task<Order?> GetByIdForStudentAsync(int orderId, int studentId) =>
        dataAccess.GetByIdForStudentAsync(orderId, studentId);

    // A student can only self-cancel while the kitchen hasn't started on the
    // order yet (Status still Placed) - once it's Preparing, real resources
    // (ingredients, the chef's time) are already committed, so cancelling at
    // that point wastes them for nothing. Staff retain a broader override via
    // clsOrderBusiness.CancelAsync (used from the WinForms Order Status
    // screen) - that's a deliberate difference in privilege, not an oversight.
    public async Task<bool> CancelForStudentAsync(int orderId, int studentId)
    {
        var order = await dataAccess.GetByIdForStudentAsync(orderId, studentId);
        if (order is null) return false; // not found / not theirs - controller returns 404

        if (order.Status != OrderStatus.Placed)
            throw new ArgumentException("This order can no longer be cancelled - the kitchen has already started preparing it.", nameof(orderId));

        return await dataAccess.CancelForStudentAsync(orderId, studentId);
    }

    public Task<Order?> GetByIdAsync(int id) =>
        dataAccess.GetByIdAsync(id);

    public Task<IEnumerable<OrderItem>> GetItemsByOrderIdAsync(int orderId) =>
        dataAccess.GetItemsByOrderIdAsync(orderId);

    public async Task<IEnumerable<Order>> GetAllAsync(DateTime? startDate = null, DateTime? endDate = null, OrderSource? orderSource = null)
    {
        var (utcStart, utcEndExclusive) = await TimeZoneHelper.ResolveUtcRangeAsync(settingsBusiness, startDate, endDate);

        return await dataAccess.GetAllAsync(utcStart, utcEndExclusive, orderSource);
    }

    public Task<bool> UpdateStatusAsync(int id, OrderStatus status) =>
        dataAccess.UpdateStatusAsync(id, status);

    public Task<bool> CancelAsync(int id) =>
        dataAccess.UpdateStatusAsync(id, OrderStatus.Cancelled);

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
        var staleOrders = await dataAccess.GetStaleMobileOrdersAsync(cutoffUtc);

        var cancelledCount = 0;
        foreach (var order in staleOrders)
        {
            if (await CancelAsync(order.OrderId))
                cancelledCount++;
        }

        return cancelledCount;
    }
}
