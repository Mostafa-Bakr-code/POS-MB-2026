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

            foreach (var item in items)
            {
                if (!itemInfo.TryGetValue(item.ItemId, out var info))
                    throw new InvalidOperationException($"Item {item.ItemId} does not exist.");
                if (!info.IsActive)
                    throw new ArgumentException($"{info.ItemName} is no longer available.", nameof(items));
                if (!info.IsAvailable)
                    throw new ArgumentException($"{info.ItemName} is currently out of stock.", nameof(items));
            }

            var total = items.Sum(i => itemInfo[i.ItemId].Price * i.Quantity);

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
                Status = OrderStatus.Placed,
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

    public async Task<bool> CancelForStudentAsync(int id, int studentId)
    {
        using var connection = connectionFactory.CreateConnection();

        const string query = @"
            UPDATE Orders
            SET Status = @Status,
                UpdatedAt = SYSUTCDATETIME()
            WHERE OrderId = @Id AND StudentId = @StudentId";

        var rowsAffected = await connection.ExecuteAsync(
            query, new { Id = id, StudentId = studentId, Status = OrderStatus.Cancelled });

        return rowsAffected > 0;
    }
}
