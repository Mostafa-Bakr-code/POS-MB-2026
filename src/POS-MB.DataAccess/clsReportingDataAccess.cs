using Dapper;
using POS_MB.DataAccess.Models.Reports;

namespace POS_MB.DataAccess;

public class clsReportingDataAccess(ISqlConnectionFactory connectionFactory)
{
    public async Task<SalesSummary> GetSalesSummaryAsync(DateTime? startDate = null, DateTime? endDate = null)
    {
        using var connection = connectionFactory.CreateConnection();

        var summaryQuery = @"
            SELECT
                COUNT(*) AS TotalOrders,
                ISNULL(SUM(CASE WHEN OrderSource = 0 THEN 1 ELSE 0 END), 0) AS CashierOrders,
                ISNULL(SUM(CASE WHEN OrderSource = 1 THEN 1 ELSE 0 END), 0) AS MobileOrders,
                ISNULL(SUM(CASE WHEN IsComplimentary = 1 THEN 1 ELSE 0 END), 0) AS ComplimentaryOrders,
                ISNULL(SUM(CASE WHEN IsComplimentary = 0 THEN Total ELSE 0 END), 0) AS TotalRevenue,
                ISNULL(SUM(CASE WHEN IsComplimentary = 1 THEN Total ELSE 0 END), 0) AS ComplimentaryValue
            FROM Orders
            WHERE Status <> 4";
        if (startDate is not null) summaryQuery += " AND OrderDate >= @StartDate";
        if (endDate is not null) summaryQuery += " AND OrderDate <= @EndDate";

        var parameters = new { StartDate = startDate?.Date, EndDate = endDate?.Date };

        var summary = await connection.QuerySingleAsync<SalesSummary>(summaryQuery, parameters);

        var taxQuery = @"
            SELECT ISNULL(SUM(oi.TotalItemsPrice / (1 + (oi.TaxRate / 100)) * (oi.TaxRate / 100)), 0)
            FROM OrderItems oi
            INNER JOIN Orders o ON oi.OrderId = o.OrderId
            WHERE o.Status <> 4";
        if (startDate is not null) taxQuery += " AND o.OrderDate >= @StartDate";
        if (endDate is not null) taxQuery += " AND o.OrderDate <= @EndDate";

        summary.TotalTax = await connection.ExecuteScalarAsync<decimal>(taxQuery, parameters);

        return summary;
    }

    public async Task<IEnumerable<ItemSalesRow>> GetItemSalesAsync(DateTime? startDate = null, DateTime? endDate = null, bool groupByDay = false)
    {
        using var connection = connectionFactory.CreateConnection();

        var dateColumn = groupByDay ? "CAST(o.[Date] AS DATE)," : string.Empty;
        var dateSelect = groupByDay ? $"{dateColumn.TrimEnd(',')} AS OrderDate," : string.Empty;
        var dateOrder = groupByDay ? "OrderDate," : string.Empty;

        var query = $@"
            SELECT
                {dateSelect}
                c.CategoryName,
                i.ItemName,
                SUM(oi.Quantity) AS Quantity,
                ISNULL(SUM(CASE WHEN o.IsComplimentary = 0 THEN oi.TotalItemsPrice ELSE 0 END), 0) AS Revenue,
                ISNULL(SUM(CASE WHEN o.IsComplimentary = 1 THEN oi.TotalItemsPrice ELSE 0 END), 0) AS ComplimentaryValue
            FROM OrderItems oi
            INNER JOIN Items i ON oi.ItemId = i.ItemId
            INNER JOIN Categories c ON i.CategoryId = c.CategoryId
            INNER JOIN Orders o ON oi.OrderId = o.OrderId
            WHERE o.Status <> 4";
        if (startDate is not null) query += " AND o.OrderDate >= @StartDate";
        if (endDate is not null) query += " AND o.OrderDate <= @EndDate";
        query += $@"
            GROUP BY {dateColumn} c.CategoryName, i.ItemName
            ORDER BY {dateOrder} c.CategoryName, i.ItemName";

        return await connection.QueryAsync<ItemSalesRow>(
            query, new { StartDate = startDate?.Date, EndDate = endDate?.Date });
    }

    public async Task<IEnumerable<ItemSalesRow>> GetTopSellersAsync(DateTime? startDate = null, DateTime? endDate = null, TopSellersSortBy sortBy = TopSellersSortBy.Quantity, int take = 10)
    {
        using var connection = connectionFactory.CreateConnection();

        var sortColumn = sortBy == TopSellersSortBy.Quantity
            ? "SUM(oi.Quantity)"
            : "SUM(CASE WHEN o.IsComplimentary = 0 THEN oi.TotalItemsPrice ELSE 0 END)";

        var query = $@"
            SELECT TOP (@Take)
                c.CategoryName,
                i.ItemName,
                SUM(oi.Quantity) AS Quantity,
                ISNULL(SUM(CASE WHEN o.IsComplimentary = 0 THEN oi.TotalItemsPrice ELSE 0 END), 0) AS Revenue,
                ISNULL(SUM(CASE WHEN o.IsComplimentary = 1 THEN oi.TotalItemsPrice ELSE 0 END), 0) AS ComplimentaryValue
            FROM OrderItems oi
            INNER JOIN Items i ON oi.ItemId = i.ItemId
            INNER JOIN Categories c ON i.CategoryId = c.CategoryId
            INNER JOIN Orders o ON oi.OrderId = o.OrderId
            WHERE o.Status <> 4";
        if (startDate is not null) query += " AND o.OrderDate >= @StartDate";
        if (endDate is not null) query += " AND o.OrderDate <= @EndDate";
        query += $@"
            GROUP BY c.CategoryName, i.ItemName
            ORDER BY {sortColumn} DESC";

        return await connection.QueryAsync<ItemSalesRow>(
            query, new { StartDate = startDate?.Date, EndDate = endDate?.Date, Take = take });
    }

    public async Task<IEnumerable<StaffPerformanceRow>> GetStaffPerformanceAsync(DateTime? startDate = null, DateTime? endDate = null)
    {
        using var connection = connectionFactory.CreateConnection();

        var query = @"
            SELECT
                u.UserId,
                u.UserName,
                COUNT(*) AS OrderCount,
                ISNULL(SUM(CASE WHEN o.IsComplimentary = 0 THEN o.Total ELSE 0 END), 0) AS Revenue
            FROM Orders o
            INNER JOIN Users u ON o.UserId = u.UserId
            WHERE o.Status <> 4
              AND o.OrderSource = 0";
        if (startDate is not null) query += " AND o.OrderDate >= @StartDate";
        if (endDate is not null) query += " AND o.OrderDate <= @EndDate";
        query += @"
            GROUP BY u.UserId, u.UserName
            ORDER BY Revenue DESC";

        return await connection.QueryAsync<StaffPerformanceRow>(
            query, new { StartDate = startDate?.Date, EndDate = endDate?.Date });
    }
}
