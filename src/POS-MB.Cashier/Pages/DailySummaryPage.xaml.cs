using POS_MB.Cashier.Api;
using POS_MB.Cashier.Controls;
using POS_MB.Cashier.Models;
using POS_MB.Cashier.Services;
using POS_MB.Cashier.Session;

namespace POS_MB.Cashier.Pages;

// Replaces DailySummaryControl - deliberately locked to today only (no date
// picker), same reasoning as the WinForms original: this is "what do I
// reconcile the till against right now", not a historical report (that's
// what Reports' own Sales Summary type is for). Reuses the exact same
// api/reports/sales-summary endpoint Reports does.
//
// WinForms bolds and reddens the "Cashier Revenue" row specifically (the
// amount the cashier physically collects) - DataGridView's row template has
// no per-row style hook, so that's dropped here, same treatment as every
// other visual-only nicety trimmed elsewhere in this port. The value itself
// is still there, just not highlighted.
public partial class DailySummaryPage : ContentPage
{
    private readonly ApiClient _apiClient = new();

    public DailySummaryPage()
    {
        InitializeComponent();

        SummaryGrid.SetColumns(
        [
            new GridColumn("Metric", r => ((SummaryRow)r).Metric, weight: 1.6),
            new GridColumn("Value", r => ((SummaryRow)r).Value, weight: 1)
        ]);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadAsync();
    }

    private async void OnRefreshClicked(object? sender, EventArgs e) => await LoadAsync();

    private async Task LoadAsync()
    {
        var today = AppSession.LocalToday;
        DateLabel.Text = $"Today: {today:yyyy-MM-dd}";

        var summary = await _apiClient.GetSalesSummaryAsync(today, today);
        if (summary is null)
        {
            await UiAlerts.Error(this, "Could not load today's summary.");
            return;
        }

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

        SummaryGrid.ItemsSource = rows.Cast<object>();
    }

    private async void OnExportClicked(object? sender, EventArgs e)
    {
        var today = AppSession.LocalToday;

        ExportButton.IsEnabled = false;
        try
        {
            var (bytes, error) = await _apiClient.DownloadReportExcelAsync("sales-summary", today, today);
            if (bytes is null)
            {
                await UiAlerts.Error(this, error ?? "Could not export today's summary.");
                return;
            }

            await ExportFileHandler.SaveAsync(bytes, $"daily-summary-{today:yyyy-MM-dd}.xlsx");
        }
        finally
        {
            ExportButton.IsEnabled = true;
        }
    }

    private record SummaryRow(string Metric, string Value);
}
