using Dapper;
using Microsoft.Data.SqlClient;
using POS_MB.DataAccess.Models;

namespace POS_MB.DataAccess;

public class clsOrderDataAccess(ISqlConnectionFactory connectionFactory)
{
    public async Task<int> CreateOrderAsync(int userId, bool isComplimentary, IReadOnlyList<NewOrderItem> items)
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

            var itemIds = items.Select(i => i.ItemId).Distinct().ToArray();
            const string pricesQuery = "SELECT ItemId, Price FROM Items WHERE ItemId IN @ItemIds";
            var prices = (await connection.QueryAsync<(int ItemId, decimal Price)>(
                pricesQuery, new { ItemIds = itemIds }, transaction))
                .ToDictionary(p => p.ItemId, p => p.Price);

            foreach (var item in items)
            {
                if (!prices.ContainsKey(item.ItemId))
                    throw new InvalidOperationException($"Item {item.ItemId} does not exist.");
            }

            var total = items.Sum(i => prices[i.ItemId] * i.Quantity);

            const string insertOrderQuery = @"
                INSERT INTO Orders (Date, Total, SerialNumber, UserId, Status, IsComplimentary)
                OUTPUT INSERTED.OrderId
                VALUES (@Date, @Total, @SerialNumber, @UserId, @Status, @IsComplimentary);";
            var orderId = await connection.ExecuteScalarAsync<int>(insertOrderQuery, new
            {
                Date = date,
                Total = total,
                SerialNumber = serialNumber,
                UserId = userId,
                Status = OrderStatus.Placed,
                IsComplimentary = isComplimentary
            }, transaction);

            const string insertItemQuery = @"
                INSERT INTO OrderItems (OrderId, ItemId, Quantity, Price, TotalItemsPrice, Comment)
                VALUES (@OrderId, @ItemId, @Quantity, @Price, @TotalItemsPrice, @Comment);";
            foreach (var item in items)
            {
                var price = prices[item.ItemId];
                await connection.ExecuteAsync(insertItemQuery, new
                {
                    OrderId = orderId,
                    item.ItemId,
                    item.Quantity,
                    Price = price,
                    TotalItemsPrice = price * item.Quantity,
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

        const string query = "SELECT * FROM Orders WHERE OrderId = @Id";

        return await connection.QuerySingleOrDefaultAsync<Order>(query, new { Id = id });
    }

    public async Task<IEnumerable<OrderItem>> GetItemsByOrderIdAsync(int orderId)
    {
        using var connection = connectionFactory.CreateConnection();

        const string query = "SELECT * FROM OrderItems WHERE OrderId = @OrderId";

        return await connection.QueryAsync<OrderItem>(query, new { OrderId = orderId });
    }

    public async Task<IEnumerable<Order>> GetAllAsync(DateTime? startDate = null, DateTime? endDate = null)
    {
        using var connection = connectionFactory.CreateConnection();

        var query = "SELECT * FROM Orders WHERE 1 = 1";
        if (startDate is not null) query += " AND OrderDate >= @StartDate";
        if (endDate is not null) query += " AND OrderDate <= @EndDate";
        query += " ORDER BY OrderId DESC";

        return await connection.QueryAsync<Order>(
            query, new { StartDate = startDate?.Date, EndDate = endDate?.Date });
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
}
