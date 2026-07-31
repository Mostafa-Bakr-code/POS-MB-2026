using Dapper;
using POS_MB.DataAccess.Models.Reports;

namespace POS_MB.DataAccess;

public class clsReportingDataAccess(ISqlConnectionFactory connectionFactory)
{
    // TotalItemsPrice is tax-inclusive; this pulls out just the tax portion,
    // using each line's own snapshotted TaxRate (not today's Items.TaxRate).
    private const string TaxExpression = "(oi.TotalItemsPrice / (1 + (oi.TaxRate / 100)) * (oi.TaxRate / 100))";

    // utcStart/utcEndExclusive below are already-resolved UTC instants (local-timezone
    // conversion happens in clsReportingBusiness) - every query filters on the raw Date
    // column, not the OrderDate shortcut, since OrderDate reflects the UTC calendar day,
    // not the caller's local day.

    public async Task<SalesSummary> GetSalesSummaryAsync(DateTime? utcStart = null, DateTime? utcEndExclusive = null)
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
        if (utcStart is not null) summaryQuery += " AND Date >= @UtcStart";
        if (utcEndExclusive is not null) summaryQuery += " AND Date < @UtcEndExclusive";

        var parameters = new { UtcStart = utcStart, UtcEndExclusive = utcEndExclusive };

        var summary = await connection.QuerySingleAsync<SalesSummary>(summaryQuery, parameters);

        // Scoped to paid (non-complimentary) orders only, matching TotalRevenue,
        // so RevenueExcludingTax = TotalRevenue - TotalTax is coherent.
        var taxQuery = $@"
            SELECT ISNULL(SUM({TaxExpression}), 0)
            FROM OrderItems oi
            INNER JOIN Orders o ON oi.OrderId = o.OrderId
            WHERE o.Status <> 4
              AND o.IsComplimentary = 0";
        if (utcStart is not null) taxQuery += " AND o.Date >= @UtcStart";
        if (utcEndExclusive is not null) taxQuery += " AND o.Date < @UtcEndExclusive";

        summary.TotalTax = await connection.ExecuteScalarAsync<decimal>(taxQuery, parameters);

        return summary;
    }

    public async Task<IEnumerable<ItemSalesRow>> GetItemSalesAsync(DateTime? utcStart = null, DateTime? utcEndExclusive = null, bool groupByDay = false, bool groupByPrice = false, decimal timeZoneOffsetHours = 0)
    {
        using var connection = connectionFactory.CreateConnection();

        // Bucketed by the caller's local day, not the UTC day o.Date happens to fall on -
        // otherwise the day label itself would still be wrong even with the range fixed.
        var dateColumn = groupByDay ? "CAST(DATEADD(HOUR, @OffsetHours, o.[Date]) AS DATE)," : string.Empty;
        var dateSelect = groupByDay ? $"{dateColumn.TrimEnd(',')} AS OrderDate," : string.Empty;

        // Uses oi.Price - each line's own historical snapshot - not today's Items.Price,
        // so this reflects exactly what was charged at the time, no matter how many times
        // the catalog price has changed since.
        var priceColumn = groupByPrice ? "oi.Price," : string.Empty;
        var priceSelect = groupByPrice ? "oi.Price AS SoldAtPrice," : string.Empty;

        var orderParts = new List<string> { "c.CategoryName", "i.ItemName" };
        if (groupByDay) orderParts.Add("OrderDate");
        if (groupByPrice) orderParts.Add("SoldAtPrice");
        var orderBy = string.Join(", ", orderParts);

        var query = $@"
            SELECT
                {dateSelect}
                {priceSelect}
                c.CategoryName,
                i.ItemName,
                SUM(oi.Quantity) AS Quantity,
                ISNULL(SUM(CASE WHEN o.IsComplimentary = 0 THEN oi.TotalItemsPrice ELSE 0 END), 0) AS Revenue,
                ISNULL(SUM(CASE WHEN o.IsComplimentary = 1 THEN oi.TotalItemsPrice ELSE 0 END), 0) AS ComplimentaryValue,
                ISNULL(SUM(CASE WHEN o.IsComplimentary = 0 THEN {TaxExpression} ELSE 0 END), 0) AS Tax
            FROM OrderItems oi
            INNER JOIN Items i ON oi.ItemId = i.ItemId
            INNER JOIN Categories c ON i.CategoryId = c.CategoryId
            INNER JOIN Orders o ON oi.OrderId = o.OrderId
            WHERE o.Status <> 4";
        if (utcStart is not null) query += " AND o.Date >= @UtcStart";
        if (utcEndExclusive is not null) query += " AND o.Date < @UtcEndExclusive";
        query += $@"
            GROUP BY {dateColumn} {priceColumn} i.ItemId, c.CategoryName, i.ItemName
            ORDER BY {orderBy}";

        return await connection.QueryAsync<ItemSalesRow>(
            query, new { UtcStart = utcStart, UtcEndExclusive = utcEndExclusive, OffsetHours = timeZoneOffsetHours });
    }

    public async Task<IEnumerable<ItemSalesRow>> GetTopSellersAsync(DateTime? utcStart = null, DateTime? utcEndExclusive = null, TopSellersSortBy sortBy = TopSellersSortBy.Quantity, int take = 10)
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
                ISNULL(SUM(CASE WHEN o.IsComplimentary = 1 THEN oi.TotalItemsPrice ELSE 0 END), 0) AS ComplimentaryValue,
                ISNULL(SUM(CASE WHEN o.IsComplimentary = 0 THEN {TaxExpression} ELSE 0 END), 0) AS Tax
            FROM OrderItems oi
            INNER JOIN Items i ON oi.ItemId = i.ItemId
            INNER JOIN Categories c ON i.CategoryId = c.CategoryId
            INNER JOIN Orders o ON oi.OrderId = o.OrderId
            WHERE o.Status <> 4";
        if (utcStart is not null) query += " AND o.Date >= @UtcStart";
        if (utcEndExclusive is not null) query += " AND o.Date < @UtcEndExclusive";
        query += $@"
            GROUP BY i.ItemId, c.CategoryName, i.ItemName
            ORDER BY {sortColumn} DESC";

        return await connection.QueryAsync<ItemSalesRow>(
            query, new { UtcStart = utcStart, UtcEndExclusive = utcEndExclusive, Take = take });
    }

    public async Task<IEnumerable<StaffPerformanceRow>> GetStaffPerformanceAsync(DateTime? utcStart = null, DateTime? utcEndExclusive = null)
    {
        using var connection = connectionFactory.CreateConnection();

        // Joins through OrderItems (rather than summing Orders.Total directly) so Tax
        // can be computed from each line's own snapshotted TaxRate.
        var query = $@"
            SELECT
                u.UserId,
                u.UserName,
                COUNT(DISTINCT o.OrderId) AS OrderCount,
                ISNULL(SUM(CASE WHEN o.IsComplimentary = 0 THEN oi.TotalItemsPrice ELSE 0 END), 0) AS Revenue,
                ISNULL(SUM(CASE WHEN o.IsComplimentary = 0 THEN {TaxExpression} ELSE 0 END), 0) AS Tax
            FROM Orders o
            INNER JOIN Users u ON o.UserId = u.UserId
            INNER JOIN OrderItems oi ON oi.OrderId = o.OrderId
            WHERE o.Status <> 4
              AND o.OrderSource = 0";
        if (utcStart is not null) query += " AND o.Date >= @UtcStart";
        if (utcEndExclusive is not null) query += " AND o.Date < @UtcEndExclusive";
        query += @"
            GROUP BY u.UserId, u.UserName
            ORDER BY Revenue DESC";

        return await connection.QueryAsync<StaffPerformanceRow>(
            query, new { UtcStart = utcStart, UtcEndExclusive = utcEndExclusive });
    }
}
