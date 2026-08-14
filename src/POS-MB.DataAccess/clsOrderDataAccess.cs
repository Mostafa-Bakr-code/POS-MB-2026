using Dapper;
using Microsoft.Data.SqlClient;
using POS_MB.DataAccess.Models;

namespace POS_MB.DataAccess;

public class clsOrderDataAccess(ISqlConnectionFactory connectionFactory)
{
    public async Task<int> CreateOrderAsync(OrderSource orderSource, int? userId, int? studentId, bool isComplimentary, IReadOnlyList<NewOrderItem> items)
    {
        if (items.Count == 0)
            throw new ArgumentException("An order must have at least one item.", nameof(items));

        using var connection = (SqlConnection)connectionFactory.CreateConnection();
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();

        try
        {
            var date = DateTime.UtcNow;

            // Locks the (OrderDate) key range for today until commit, so two concurrent
            // orders can never read the same MAX(SerialNumber) and collide.
            const string serialQuery = @"
                SELECT ISNULL(MAX(SerialNumber), 0) + 1
                FROM Orders WITH (UPDLOCK, HOLDLOCK)
                WHERE OrderDate = CAST(@Date AS DATE)";
            var serialNumber = await connection.ExecuteScalarAsync<int>(
                serialQuery, new { Date = date }, transaction);

            // IsActive/IsAvailable are checked here, not just relied on client-side -
            // the menu only ever shows available items, but there's a real gap
            // between "browsed and added to cart" and "actually placed the order"
            // (staff can mark something out of stock in between), and the same
            // path serves both Cashier and Mobile orders, so this applies to both.
            var itemIds = items.Select(i => i.ItemId).Distinct().ToArray();
            const string pricesQuery = "SELECT ItemId, ItemName, Price, TaxRate, IsActive, IsAvailable FROM Items WHERE ItemId IN @ItemIds";
            var itemInfo = (await connection.QueryAsync<(int ItemId, string ItemName, decimal Price, decimal TaxRate, bool IsActive, bool IsAvailable)>(
                pricesQuery, new { ItemIds = itemIds }, transaction))
                .ToDictionary(p => p.ItemId, p => p);

            // Collects every bad item before failing, not just the first one -
            // so the caller (mobile cart in particular) can clean up its whole
            // cart in one round trip instead of one rejection at a time.
            var unavailable = new List<UnavailableItem>();
            foreach (var item in items)
            {
                if (!itemInfo.TryGetValue(item.ItemId, out var info))
                    throw new InvalidOperationException($"Item {item.ItemId} does not exist.");
                if (!info.IsActive)
                    unavailable.Add(new UnavailableItem(info.ItemId, info.ItemName, "no longer available"));
                else if (!info.IsAvailable)
                    unavailable.Add(new UnavailableItem(info.ItemId, info.ItemName, "out of stock"));
            }
            if (unavailable.Count > 0)
                throw new ItemsUnavailableException(unavailable);

            var total = items.Sum(i => itemInfo[i.ItemId].Price * i.Quantity);

            // A cashier order is paid and handed over at the register in the
            // same moment it's created - there's no separate prep-tracking
            // step visible from that side of the counter today, so it starts
            // (and stays, barring a cashier-side Cancel) at Completed rather
            // than sitting at Placed forever with nothing to ever advance it.
            // Mobile orders are the opposite: Placed is the real starting
            // point of a workflow the kitchen needs to work through.
            var initialStatus = orderSource == OrderSource.Cashier ? OrderStatus.Completed : OrderStatus.Placed;

            const string insertOrderQuery = @"
                INSERT INTO Orders (Date, Total, SerialNumber, UserId, StudentId, OrderSource, Status, IsComplimentary)
                OUTPUT INSERTED.OrderId
                VALUES (@Date, @Total, @SerialNumber, @UserId, @StudentId, @OrderSource, @Status, @IsComplimentary);";
            var orderId = await connection.ExecuteScalarAsync<int>(insertOrderQuery, new
            {
                Date = date,
                Total = total,
                SerialNumber = serialNumber,
                UserId = userId,
                StudentId = studentId,
                OrderSource = orderSource,
                Status = initialStatus,
                IsComplimentary = isComplimentary
            }, transaction);

            const string insertItemQuery = @"
                INSERT INTO OrderItems (OrderId, ItemId, Quantity, Price, TotalItemsPrice, TaxRate, Comment)
                VALUES (@OrderId, @ItemId, @Quantity, @Price, @TotalItemsPrice, @TaxRate, @Comment);";
            foreach (var item in items)
            {
                var info = itemInfo[item.ItemId];
                await connection.ExecuteAsync(insertItemQuery, new
                {
                    OrderId = orderId,
                    item.ItemId,
                    item.Quantity,
                    Price = info.Price,
                    TotalItemsPrice = info.Price * item.Quantity,
                    TaxRate = info.TaxRate,
                    item.Comment
                }, transaction);
            }

            transaction.Commit();
            return orderId;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<Order?> GetByIdAsync(int id)
    {
        using var connection = connectionFactory.CreateConnection();

        const string query = @"
            SELECT o.*, u.UserName AS CashierName, s.Email AS StudentEmail
            FROM Orders o
            LEFT JOIN Users u ON u.UserId = o.UserId
            LEFT JOIN Students s ON s.StudentId = o.StudentId
            WHERE o.OrderId = @Id";

        return await connection.QuerySingleOrDefaultAsync<Order>(query, new { Id = id });
    }

    // Resolves a Paymob webhook's order reference (see PaymobOrderReference)
    // back to a real order - OrderDate is the same persisted computed column
    // (CAST(Date AS DATE)) that already enforces SerialNumber's uniqueness
    // per day, so this is exactly as reliable a lookup as OrderId itself.
    public async Task<Order?> GetByDateAndSerialNumberAsync(DateTime orderDateUtc, int serialNumber)
    {
        using var connection = connectionFactory.CreateConnection();

        const string query = "SELECT * FROM Orders WHERE OrderDate = @OrderDate AND SerialNumber = @SerialNumber";

        return await connection.QuerySingleOrDefaultAsync<Order>(
            query, new { OrderDate = orderDateUtc.Date, SerialNumber = serialNumber });
    }

    public async Task<IEnumerable<OrderItem>> GetItemsByOrderIdAsync(int orderId)
    {
        using var connection = connectionFactory.CreateConnection();

        const string query = "SELECT * FROM OrderItems WHERE OrderId = @OrderId";

        return await connection.QueryAsync<OrderItem>(query, new { OrderId = orderId });
    }

    // utcStart/utcEndExclusive are already-resolved UTC instants (local-timezone conversion
    // happens in clsOrderBusiness) - filtering on the raw Date column, not the OrderDate
    // shortcut, since OrderDate reflects the UTC calendar day, not the caller's local day.
    public async Task<IEnumerable<Order>> GetAllAsync(DateTime? utcStart = null, DateTime? utcEndExclusive = null, OrderSource? orderSource = null)
    {
        using var connection = connectionFactory.CreateConnection();

        var query = @"
            SELECT o.*, u.UserName AS CashierName, s.Email AS StudentEmail
            FROM Orders o
            LEFT JOIN Users u ON u.UserId = o.UserId
            LEFT JOIN Students s ON s.StudentId = o.StudentId
            WHERE 1 = 1";
        if (utcStart is not null) query += " AND o.Date >= @UtcStart";
        if (utcEndExclusive is not null) query += " AND o.Date < @UtcEndExclusive";
        if (orderSource is not null) query += " AND o.OrderSource = @OrderSource";
        query += " ORDER BY o.OrderId DESC";

        return await connection.QueryAsync<Order>(
            query, new { UtcStart = utcStart, UtcEndExclusive = utcEndExclusive, OrderSource = orderSource });
    }

    // Feeds the auto-cancel safety net (clsOrderBusiness.CancelStaleMobileOrdersAsync) -
    // a mobile order that's sat too long in a given status, for whatever
    // reason (connectivity blip, a swamped chef, a crashed app, an abandoned
    // Paymob checkout), regardless of why. Shared by both the Layer 3
    // safety net (stale Placed orders nobody accepted) and the abandoned-
    // payment cleanup (stale AwaitingPayment orders nobody ever paid for) -
    // same query shape, different status/threshold.
    //
    // useUpdatedAt distinguishes what "how long has it been sitting here"
    // actually means for each case: for AwaitingPayment, [Date] (order
    // creation/checkout start) is the right anchor - that's genuinely how
    // long the student has been mid-checkout. For Placed, [Date] would be
    // wrong - a mobile order sits at AwaitingPayment first, so anchoring the
    // kitchen's "un-accepted too long" timeout to order creation silently
    // burns however long the student spent entering card details before the
    // kitchen could ever have seen it. UpdatedAt is set the instant the
    // order actually transitions into Placed (MarkOrderPaymentResultAsync)
    // and nothing else touches it while Status stays Placed, so it's the
    // correct "became visible to the kitchen at" anchor.
    public async Task<IEnumerable<Order>> GetStaleMobileOrdersAsync(OrderStatus status, DateTime olderThanUtc, bool useUpdatedAt = false)
    {
        using var connection = connectionFactory.CreateConnection();

        var column = useUpdatedAt ? "UpdatedAt" : "[Date]";
        var query = $@"
            SELECT * FROM Orders
            WHERE OrderSource = @OrderSource AND Status = @Status AND {column} < @OlderThanUtc";

        return await connection.QueryAsync<Order>(
            query, new { OrderSource = OrderSource.Mobile, Status = status, OlderThanUtc = olderThanUtc });
    }

    // Feeds the cashier-PC kitchen-ticket poller (see project notes on the
    // chef tablet's print-trigger design) - a browser can't open a raw socket
    // to the ESC/POS printer, so whichever client (tablet or WinForms) moves
    // an order to Preparing no longer prints it inline. Instead this is what
    // the poller checks every few seconds, decoupled from which screen did
    // the accepting.
    public async Task<IEnumerable<Order>> GetOrdersNeedingKitchenTicketAsync()
    {
        using var connection = connectionFactory.CreateConnection();

        const string query = @"
            SELECT * FROM Orders
            WHERE OrderSource = @OrderSource AND Status = @Status AND KitchenTicketPrintedAt IS NULL
            ORDER BY Date";

        return await connection.QueryAsync<Order>(
            query, new { OrderSource = OrderSource.Mobile, Status = OrderStatus.Preparing });
    }

    // The KitchenTicketPrintedAt IS NULL guard makes this safely idempotent -
    // if more than one cashier PC ever runs the poller, only the first to
    // mark an order actually "claims" it.
    public async Task<bool> MarkKitchenTicketPrintedAsync(int id)
    {
        using var connection = connectionFactory.CreateConnection();

        const string query = @"
            UPDATE Orders
            SET KitchenTicketPrintedAt = SYSUTCDATETIME()
            WHERE OrderId = @Id AND KitchenTicketPrintedAt IS NULL";

        var rowsAffected = await connection.ExecuteAsync(query, new { Id = id });
        return rowsAffected > 0;
    }

    // Records the successful payment's Paymob transaction id alongside moving
    // the order to Placed - needed later to tell Paymob which transaction to
    // refund if this order is ever cancelled (see clsOrderBusiness.RefundIfPaidAsync).
    //
    // The "AND Status = @AwaitingPaymentStatus" guard is not optional - the
    // business layer reads the order's status, decides what to do, then
    // calls this separately, which leaves a real gap for a concurrent
    // cancel to land in between. Without this guard in the WHERE clause
    // itself, this UPDATE would blindly overwrite whatever the row's
    // current status is (even Cancelled), silently resurrecting an order
    // someone just cancelled. Returning false when the guard fails lets
    // clsOrderBusiness detect the race and re-route through the same
    // handling as any other "payment landed after the order moved on" case.
    public async Task<bool> MarkPaidAsync(int id, long transactionId)
    {
        using var connection = connectionFactory.CreateConnection();

        const string query = @"
            UPDATE Orders
            SET Status = @Status, PaymobTransactionId = @TransactionId, UpdatedAt = SYSUTCDATETIME()
            WHERE OrderId = @Id AND Status = @AwaitingPaymentStatus";

        var rowsAffected = await connection.ExecuteAsync(
            query, new { Id = id, Status = OrderStatus.Placed, AwaitingPaymentStatus = OrderStatus.AwaitingPayment, TransactionId = transactionId });
        return rowsAffected > 0;
    }

    // Remembers which special_reference was last sent to Paymob for this
    // order (initial checkout or a resume) - see clsOrderBusiness.ResumeCheckoutAsync,
    // which uses this to ask Paymob directly whether that attempt already
    // succeeded before ever opening a second payment window.
    public async Task SetLastPaymobReferenceAsync(int id, string reference)
    {
        using var connection = connectionFactory.CreateConnection();

        const string query = "UPDATE Orders SET LastPaymobReference = @Reference WHERE OrderId = @Id";

        await connection.ExecuteAsync(query, new { Id = id, Reference = reference });
    }

    // Records a payment's transaction id WITHOUT touching Status - used when
    // a payment succeeds for an order that's already Cancelled (the charge
    // landed just after cancellation, see clsOrderBusiness.MarkOrderPaymentResultAsync)
    // so the order isn't silently resurrected back to Placed, but the charge
    // is still tracked for the automatic refund that follows.
    public async Task<bool> RecordPaymobTransactionIdAsync(int id, long transactionId)
    {
        using var connection = connectionFactory.CreateConnection();

        const string query = @"
            UPDATE Orders
            SET PaymobTransactionId = @TransactionId
            WHERE OrderId = @Id AND PaymobTransactionId IS NULL";

        var rowsAffected = await connection.ExecuteAsync(query, new { Id = id, TransactionId = transactionId });
        return rowsAffected > 0;
    }

    // The RefundedAt IS NULL guard makes this safely idempotent - the same
    // protection as MarkKitchenTicketPrintedAsync, but here it's guarding
    // against ever attempting to refund real money twice.
    public async Task<bool> MarkRefundedAsync(int id, long refundTransactionId)
    {
        using var connection = connectionFactory.CreateConnection();

        const string query = @"
            UPDATE Orders
            SET RefundedAt = SYSUTCDATETIME(), RefundTransactionId = @RefundTransactionId
            WHERE OrderId = @Id AND RefundedAt IS NULL";

        var rowsAffected = await connection.ExecuteAsync(query, new { Id = id, RefundTransactionId = refundTransactionId });
        return rowsAffected > 0;
    }

    public async Task<bool> UpdateStatusAsync(int id, OrderStatus status)
    {
        using var connection = connectionFactory.CreateConnection();

        const string query = @"
            UPDATE Orders
            SET Status = @Status,
                UpdatedAt = SYSUTCDATETIME()
            WHERE OrderId = @Id";

        var rowsAffected = await connection.ExecuteAsync(query, new { Id = id, Status = status });

        return rowsAffected > 0;
    }

    // The StudentId = @StudentId clause is what actually enforces ownership -
    // baked into the query itself rather than checked afterward in C#, so it
    // can't be forgotten by a future caller the way an after-the-fact check
    // could be. Same pattern as clsLogsDataAccess.EndSessionAsync.
    public async Task<IEnumerable<Order>> GetAllForStudentAsync(int studentId, DateTime? utcStart = null, DateTime? utcEndExclusive = null)
    {
        using var connection = connectionFactory.CreateConnection();

        var query = "SELECT * FROM Orders WHERE StudentId = @StudentId";
        if (utcStart is not null) query += " AND Date >= @UtcStart";
        if (utcEndExclusive is not null) query += " AND Date < @UtcEndExclusive";
        query += " ORDER BY OrderId DESC";

        return await connection.QueryAsync<Order>(
            query, new { StudentId = studentId, UtcStart = utcStart, UtcEndExclusive = utcEndExclusive });
    }

    public async Task<Order?> GetByIdForStudentAsync(int id, int studentId)
    {
        using var connection = connectionFactory.CreateConnection();

        const string query = "SELECT * FROM Orders WHERE OrderId = @Id AND StudentId = @StudentId";

        return await connection.QuerySingleOrDefaultAsync<Order>(query, new { Id = id, StudentId = studentId });
    }

    // The Status IN (...) check is a defense-in-depth check, not just the
    // business layer's job - it closes the same race a client could otherwise
    // exploit by firing the cancel request the instant an order moves past
    // the allowed statuses (see clsOrderBusiness.CancelForStudentAsync for
    // the primary, clearer-error-message check).
    public async Task<bool> CancelForStudentAsync(int id, int studentId)
    {
        using var connection = connectionFactory.CreateConnection();

        const string query = @"
            UPDATE Orders
            SET Status = @Status,
                UpdatedAt = SYSUTCDATETIME()
            WHERE OrderId = @Id AND StudentId = @StudentId AND Status IN (@AwaitingPaymentStatus, @PlacedStatus)";

        var rowsAffected = await connection.ExecuteAsync(
            query, new { Id = id, StudentId = studentId, Status = OrderStatus.Cancelled, AwaitingPaymentStatus = OrderStatus.AwaitingPayment, PlacedStatus = OrderStatus.Placed });

        return rowsAffected > 0;
    }
}
