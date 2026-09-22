using POS_MB.Cashier.Api;
using POS_MB.Cashier.Controls;
using POS_MB.Cashier.Models;
using POS_MB.Cashier.Services;

namespace POS_MB.Cashier.Pages;

// Replaces ReportsControl - one screen, four report types, driven entirely
// by which columns DataGridView.SetColumns is handed for the type currently
// selected (that's exactly what the component was built in Phase 3 to
// support). Excel export is fully server-generated (see
// ApiClient.DownloadReportExcelAsync) - this page only ever hands the
// returned bytes to ExportFileHandler, never builds a workbook itself.
public partial class ReportsPage : ContentPage
{
    private readonly ApiClient _apiClient = new();
    private List<object> _lastRows = [];

    public ReportsPage()
    {
        InitializeComponent();

        ReportTypePicker.SelectedIndex = 0;
        SortByPicker.SelectedIndex = 0;
        StartDatePicker.Date = DateTime.Today;
        EndDatePicker.Date = DateTime.Today;

        UpdateVisibleOptions();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await RunReportAsync();
    }

    private ReportType SelectedReportType => (ReportType)ReportTypePicker.SelectedIndex;

    private void OnReportTypeChanged(object? sender, EventArgs e) => UpdateVisibleOptions();

    private void UpdateVisibleOptions()
    {
        var isItemSales = SelectedReportType == ReportType.ItemSales;
        var isTopSellers = SelectedReportType == ReportType.TopSellers;

        GroupByDayRow.IsVisible = isItemSales;
        GroupByPriceRow.IsVisible = isItemSales;
        GroupBySourceRow.IsVisible = isItemSales;

        SortByPicker.IsVisible = isTopSellers;
        TakeRow.IsVisible = isTopSellers;
    }

    private void OnUseDateRangeToggled(object? sender, ToggledEventArgs e)
    {
        StartDatePicker.IsEnabled = UseDateRangeSwitch.IsToggled;
        EndDatePicker.IsEnabled = UseDateRangeSwitch.IsToggled;
    }

    private (DateTime? Start, DateTime? End)? GetDateRange()
    {
        if (!UseDateRangeSwitch.IsToggled) return (null, null);

        var start = StartDatePicker.Date;
        var end = EndDatePicker.Date;
        return (start, end);
    }

    private async void OnRunClicked(object? sender, EventArgs e) => await RunReportAsync();

    private async Task RunReportAsync()
    {
        var range = GetDateRange();
        if (range is null) return;
        var (start, end) = range.Value;

        if (start is not null && end is not null && end < start)
        {
            await UiAlerts.Error(this, "End date cannot be before start date.");
            return;
        }

        switch (SelectedReportType)
        {
            case ReportType.SalesSummary:
                var summary = await _apiClient.GetSalesSummaryAsync(start, end);
                if (summary is null)
                {
                    await UiAlerts.Error(this, "Could not load this report.");
                    return;
                }
                BindSummary(summary);
                break;

            case ReportType.ItemSales:
                var groupByDay = GroupByDayCheck.IsChecked;
                var groupByPrice = GroupByPriceCheck.IsChecked;
                var groupBySource = GroupBySourceCheck.IsChecked;
                var itemRows = await _apiClient.GetItemSalesAsync(start, end, groupByDay, groupByPrice, groupBySource);
                BindItemRows(itemRows, groupByDay, groupByPrice, groupBySource);
                break;

            case ReportType.TopSellers:
                var sortBy = (TopSellersSortBy)SortByPicker.SelectedIndex;
                var take = (int)TakeStepper.Value;
                var topRows = await _apiClient.GetTopSellersAsync(start, end, sortBy, take);
                BindItemRows(topRows, groupByDay: false, groupByPrice: false, groupBySource: false);
                break;

            case ReportType.StaffPerformance:
                var staffRows = await _apiClient.GetStaffPerformanceAsync(start, end);
                BindStaffRows(staffRows);
                break;
        }
    }

    private void BindSummary(SalesSummaryDto summary)
    {
        var rows = new List<SummaryRow>
        {
            new("Total Orders", summary.TotalOrders.ToString()),
            new("Cashier Orders", summary.CashierOrders.ToString()),
            new("Mobile Orders", summary.MobileOrders.ToString()),
            new("Complimentary Orders", summary.ComplimentaryOrders.ToString()),
            new("Total Revenue (incl. tax)", summary.TotalRevenue.ToString("0.00")),
            new("Cashier Revenue (incl. tax)", summary.CashierRevenue.ToString("0.00")),
            new("Mobile Revenue (incl. tax)", summary.MobileRevenue.ToString("0.00")),
            new("Total Revenue (excl. tax)", summary.RevenueExcludingTax.ToString("0.00")),
            new("Total Tax", summary.TotalTax.ToString("0.00")),
            new("Complimentary Value", summary.ComplimentaryValue.ToString("0.00")),
            new("Average Order Value", summary.AverageOrderValue.ToString("0.00"))
        };

        ReportGrid.SetColumns(
        [
            new GridColumn("Metric", r => ((SummaryRow)r).Metric, weight: 1.6),
            new GridColumn("Value", r => ((SummaryRow)r).Value, weight: 1)
        ]);
        _lastRows = rows.Cast<object>().ToList();
        ReportGrid.ItemsSource = _lastRows;
    }

    private void BindItemRows(List<ItemSalesRowDto> rows, bool groupByDay, bool groupByPrice, bool groupBySource)
    {
        List<GridColumn> columns =
        [
            new GridColumn("Category", r => ((ItemSalesRowDto)r).CategoryName, weight: 1.2),
            new GridColumn("Item", r => ((ItemSalesRowDto)r).ItemName, weight: 1.4)
        ];

        if (groupByDay)
            columns.Add(new GridColumn("Date", r => ((ItemSalesRowDto)r).OrderDate?.ToString("yyyy-MM-dd") ?? "", weight: 1));
        if (groupByPrice)
            columns.Add(new GridColumn("Sold At Price", r => ((ItemSalesRowDto)r).SoldAtPrice?.ToString("0.00") ?? "", weight: 1));
        if (groupBySource)
            columns.Add(new GridColumn("Source", r => ((ItemSalesRowDto)r).Source?.ToString() ?? "", weight: 0.9));

        columns.Add(new GridColumn("Quantity", r => ((ItemSalesRowDto)r).Quantity.ToString(), weight: 0.8));
        columns.Add(new GridColumn("Revenue (incl. tax)", r => ((ItemSalesRowDto)r).Revenue.ToString("0.00"), weight: 1));
        columns.Add(new GridColumn("Revenue (excl. tax)", r => ((ItemSalesRowDto)r).RevenueExcludingTax.ToString("0.00"), weight: 1));
        columns.Add(new GridColumn("Tax", r => ((ItemSalesRowDto)r).Tax.ToString("0.00"), weight: 0.8));
        columns.Add(new GridColumn("Complimentary", r => ((ItemSalesRowDto)r).ComplimentaryValue.ToString("0.00"), weight: 0.9));
        columns.Add(new GridColumn("Avg Price", r => ((ItemSalesRowDto)r).AveragePrice.ToString("0.00"), weight: 0.9));

        ReportGrid.SetColumns(columns);
        _lastRows = rows.Cast<object>().ToList();
        ReportGrid.ItemsSource = _lastRows;
    }

    private void BindStaffRows(List<StaffPerformanceRowDto> rows)
    {
        ReportGrid.SetColumns(
        [
            new GridColumn("User", r => ((StaffPerformanceRowDto)r).UserName, weight: 1.4),
            new GridColumn("Order Count", r => ((StaffPerformanceRowDto)r).OrderCount.ToString(), weight: 0.9),
            new GridColumn("Revenue (incl. tax)", r => ((StaffPerformanceRowDto)r).Revenue.ToString("0.00"), weight: 1),
            new GridColumn("Revenue (excl. tax)", r => ((StaffPerformanceRowDto)r).RevenueExcludingTax.ToString("0.00"), weight: 1),
            new GridColumn("Tax", r => ((StaffPerformanceRowDto)r).Tax.ToString("0.00"), weight: 0.8)
        ]);
        _lastRows = rows.Cast<object>().ToList();
        ReportGrid.ItemsSource = _lastRows;
    }

    private async void OnExportClicked(object? sender, EventArgs e)
    {
        var range = GetDateRange();
        if (range is null) return;
        var (start, end) = range.Value;

        if (start is not null && end is not null && end < start)
        {
            await UiAlerts.Error(this, "End date cannot be before start date.");
            return;
        }

        // Reflects whatever is currently selected on screen, same as
        // WinForms' own ExportToExcelAsync - not necessarily whatever the
        // grid last happened to show, if the cashier changed a filter
        // without pressing Run again first.
        var (reportPath, extraParams, defaultFileName) = SelectedReportType switch
        {
            ReportType.SalesSummary => ("sales-summary", "", "sales-summary.xlsx"),
            ReportType.ItemSales => ("item-sales", $"&groupByDay={GroupByDayCheck.IsChecked}&groupByPrice={GroupByPriceCheck.IsChecked}&groupBySource={GroupBySourceCheck.IsChecked}", "item-sales.xlsx"),
            ReportType.TopSellers => ("top-sellers", $"&sortBy={(TopSellersSortBy)SortByPicker.SelectedIndex}&take={(int)TakeStepper.Value}", "top-sellers.xlsx"),
            ReportType.StaffPerformance => ("staff-performance", "", "staff-performance.xlsx"),
            _ => throw new InvalidOperationException()
        };

        ExportButton.IsEnabled = false;
        try
        {
            var (bytes, error) = await _apiClient.DownloadReportExcelAsync(reportPath, start, end, extraParams);
            if (bytes is null)
            {
                await UiAlerts.Error(this, error ?? "Could not export this report.");
                return;
            }

            await ExportFileHandler.SaveAsync(bytes, defaultFileName);
        }
        finally
        {
            ExportButton.IsEnabled = true;
        }
    }

    private record SummaryRow(string Metric, string Value);
}
