using POS_MB.WinformsApp.Api;

namespace POS_MB.WinformsApp.Controls;

// Deliberately locked to today only - no date picker, no other report types.
// Meant for a cashier who should be able to see/send today's numbers without
// browsing historical data or item/staff-level breakdowns (see Reports/OrderHistory
// for that, gated by separate permissions).
public class DailySummaryControl : UserControl
{
    private readonly ApiClient _apiClient = new();
    private readonly DataGridView _grid;
    private readonly Label _lblDate;

    public DailySummaryControl()
    {
        Font = new Font("Segoe UI", 12F);

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 60, Padding = new Padding(10) };

        _lblDate = new Label
        {
            Text = $"Today: {DateTime.Today:yyyy-MM-dd}",
            AutoSize = true,
            Font = new Font("Segoe UI", 12F, FontStyle.Bold),
            Margin = new Padding(0, 10, 20, 0)
        };

        var btnRefresh = new Button { Text = "Refresh", Width = 130, Height = 40, Font = new Font("Segoe UI", 11F, FontStyle.Bold) };
        btnRefresh.Click += async (_, _) => await LoadAsync();

        var btnExport = new Button { Text = "Export to Excel", Width = 160, Height = 40, Font = new Font("Segoe UI", 11F, FontStyle.Bold) };
        btnExport.Click += async (_, _) => await ExportAsync();

        toolbar.Controls.Add(_lblDate);
        toolbar.Controls.Add(btnRefresh);
        toolbar.Controls.Add(btnExport);

        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AutoGenerateColumns = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            RowTemplate = { Height = 40 },
            Font = new Font("Segoe UI", 11F)
        };
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Metric", FillWeight = 130, MinimumWidth = 260 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Value", FillWeight = 100, MinimumWidth = 200 });

        Controls.Add(_grid);
        Controls.Add(toolbar);

        Load += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        var today = DateTime.Today;
        var summary = await _apiClient.GetSalesSummaryAsync(today, today);

        _grid.Rows.Clear();
        _grid.Rows.Add("Total Orders", summary.TotalOrders.ToString());
        _grid.Rows.Add("Cashier Orders", summary.CashierOrders.ToString());
        _grid.Rows.Add("Mobile Orders", summary.MobileOrders.ToString());
        _grid.Rows.Add("Complimentary Orders", summary.ComplimentaryOrders.ToString());

        var revenueRowIndex = _grid.Rows.Add("Total Revenue (incl. tax)", summary.TotalRevenue.ToString("0.00"));

        _grid.Rows.Add("Total Revenue (excl. tax)", summary.RevenueExcludingTax.ToString("0.00"));
        _grid.Rows.Add("Total Tax", summary.TotalTax.ToString("0.00"));
        _grid.Rows.Add("Complimentary Value", summary.ComplimentaryValue.ToString("0.00"));
        _grid.Rows.Add("Average Order Value", summary.AverageOrderValue.ToString("0.00"));

        // This is the one figure the cashier reads out - make it impossible to miss.
        var revenueRow = _grid.Rows[revenueRowIndex];
        revenueRow.DefaultCellStyle.ForeColor = Color.Red;
        revenueRow.DefaultCellStyle.Font = new Font(_grid.Font, FontStyle.Bold);
    }

    private async Task ExportAsync()
    {
        var today = DateTime.Today;
        try
        {
            var bytes = await _apiClient.DownloadReportExcelAsync("sales-summary", today, today);

            using var dialog = new SaveFileDialog { FileName = $"daily-summary-{today:yyyy-MM-dd}.xlsx", Filter = "Excel Workbook|*.xlsx" };
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                await File.WriteAllBytesAsync(dialog.FileName, bytes);
                MessageBox.Show("Report exported successfully.", "Done", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not export the report: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
