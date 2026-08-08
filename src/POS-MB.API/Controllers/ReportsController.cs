using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using POS_MB.API.Auth;
using POS_MB.Business;
using POS_MB.DataAccess.Models.Reports;

namespace POS_MB.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ReportsController(clsReportingBusiness reportingBusiness) : ControllerBase
{
    // Shared by both the Reports screen and the Daily Summary screen (including
    // each one's own Excel export), which are gated behind two different
    // permissions - either one is enough to call this.
    [HttpGet("sales-summary")]
    [RequirePermission(Permission.Reports | Permission.DailySummary)]
    public async Task<IActionResult> GetSalesSummary(
        [FromQuery] DateTime? startDate = null, [FromQuery] DateTime? endDate = null, [FromQuery] string format = "json")
    {
        var summary = await reportingBusiness.GetSalesSummaryAsync(startDate, endDate);

        if (!IsExcel(format)) return Ok(summary);

        var rows = new object[][]
        {
            new object[] { "Total Orders", summary.TotalOrders },
            new object[] { "Cashier Orders", summary.CashierOrders },
            new object[] { "Mobile Orders", summary.MobileOrders },
            new object[] { "Complimentary Orders", summary.ComplimentaryOrders },
            new object[] { "Total Revenue (incl. tax)", summary.TotalRevenue },
            new object[] { "Total Revenue (excl. tax)", summary.RevenueExcludingTax },
            new object[] { "Total Tax", summary.TotalTax },
            new object[] { "Complimentary Value", summary.ComplimentaryValue },
            new object[] { "Average Order Value", summary.AverageOrderValue },
        };
        return ExportToExcel("Sales Summary", ["Metric", "Value"], rows, "sales-summary.xlsx");
    }

    [HttpGet("item-sales")]
    [RequirePermission(Permission.Reports)]
    public async Task<IActionResult> GetItemSales(
        [FromQuery] DateTime? startDate = null, [FromQuery] DateTime? endDate = null,
        [FromQuery] bool groupByDay = false, [FromQuery] bool groupByPrice = false, [FromQuery] bool groupBySource = false,
        [FromQuery] string format = "json")
    {
        var items = (await reportingBusiness.GetItemSalesAsync(startDate, endDate, groupByDay, groupByPrice, groupBySource)).ToList();

        if (!IsExcel(format)) return Ok(items);

        var headers = new List<string> { "Category", "Item" };
        if (groupByDay) headers.Add("Date");
        if (groupByPrice) headers.Add("Sold At Price");
        if (groupBySource) headers.Add("Source");
        headers.AddRange(["Quantity", "Revenue (incl. tax)", "Revenue (excl. tax)", "Tax", "Complimentary Value", "Average Price"]);

        var rows = items.Select(i =>
        {
            var row = new List<object> { i.CategoryName, i.ItemName };
            if (groupByDay) row.Add(i.OrderDate?.ToString("yyyy-MM-dd") ?? "");
            if (groupByPrice) row.Add(i.SoldAtPrice?.ToString("0.00") ?? "");
            if (groupBySource) row.Add(i.Source?.ToString() ?? "");
            row.AddRange([i.Quantity, i.Revenue, i.RevenueExcludingTax, i.Tax, i.ComplimentaryValue, i.AveragePrice]);
            return row.ToArray();
        });

        return ExportToExcel("Item Sales", headers.ToArray(), rows, "item-sales.xlsx");
    }

    [HttpGet("top-sellers")]
    [RequirePermission(Permission.Reports)]
    public async Task<IActionResult> GetTopSellers(
        [FromQuery] DateTime? startDate = null, [FromQuery] DateTime? endDate = null,
        [FromQuery] TopSellersSortBy sortBy = TopSellersSortBy.Quantity, [FromQuery] int take = 10,
        [FromQuery] string format = "json")
    {
        var items = (await reportingBusiness.GetTopSellersAsync(startDate, endDate, sortBy, take)).ToList();

        if (!IsExcel(format)) return Ok(items);

        var headers = new[] { "Category", "Item", "Quantity", "Revenue (incl. tax)", "Revenue (excl. tax)", "Tax", "Complimentary Value", "Average Price" };
        var rows = items.Select(i => new object[] { i.CategoryName, i.ItemName, i.Quantity, i.Revenue, i.RevenueExcludingTax, i.Tax, i.ComplimentaryValue, i.AveragePrice });

        return ExportToExcel("Top Sellers", headers, rows, "top-sellers.xlsx");
    }

    [HttpGet("staff-performance")]
    [RequirePermission(Permission.Reports)]
    public async Task<IActionResult> GetStaffPerformance(
        [FromQuery] DateTime? startDate = null, [FromQuery] DateTime? endDate = null, [FromQuery] string format = "json")
    {
        var staff = (await reportingBusiness.GetStaffPerformanceAsync(startDate, endDate)).ToList();

        if (!IsExcel(format)) return Ok(staff);

        var headers = new[] { "User", "Order Count", "Revenue (incl. tax)", "Revenue (excl. tax)", "Tax" };
        var rows = staff.Select(s => new object[] { s.UserName, s.OrderCount, s.Revenue, s.RevenueExcludingTax, s.Tax });

        return ExportToExcel("Staff Performance", headers, rows, "staff-performance.xlsx");
    }

    private static bool IsExcel(string format) =>
        format.Equals("excel", StringComparison.OrdinalIgnoreCase);

    private FileContentResult ExportToExcel(string sheetName, string[] headers, IEnumerable<object[]> rows, string fileName)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add(sheetName);

        for (var col = 0; col < headers.Length; col++)
            worksheet.Cell(1, col + 1).Value = headers[col];
        worksheet.Row(1).Style.Font.Bold = true;

        var rowIndex = 2;
        foreach (var row in rows)
        {
            for (var col = 0; col < row.Length; col++)
                worksheet.Cell(rowIndex, col + 1).Value = XLCellValue.FromObject(row[col]);
            rowIndex++;
        }

        worksheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        return File(stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }
}
