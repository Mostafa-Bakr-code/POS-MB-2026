using POS_MB.WinformsApp.Api;
using POS_MB.WinformsApp.Models;

namespace POS_MB.WinformsApp.Controls;

public class ReportsControl : UserControl
{
    private readonly ApiClient _apiClient = new();

    private readonly ComboBox _cboReportType;
    private readonly CheckBox _chkUseDateRange;
    private readonly DateTimePicker _dtpStart;
    private readonly DateTimePicker _dtpEnd;
    private readonly CheckBox _chkGroupByDay;
    private readonly CheckBox _chkGroupByPrice;
    private readonly CheckBox _chkGroupBySource;
    private readonly ComboBox _cboSortBy;
    private readonly NumericUpDown _numTake;
    private readonly Button _btnRun;
    private readonly Button _btnExport;
    private readonly DataGridView _grid;

    public ReportsControl()
    {
        Font = new Font("Segoe UI", 12F);

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 110, Padding = new Padding(10), AutoScroll = true };

        _cboReportType = new ComboBox { Width = 200, Height = 36, Font = new Font("Segoe UI", 11F), DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 6, 20, 6) };
        _cboReportType.Items.AddRange(["Sales Summary", "Item Sales", "Top Sellers", "Staff Performance"]);
        _cboReportType.SelectedIndex = 0;
        _cboReportType.SelectedIndexChanged += (_, _) => UpdateVisibleOptions();

        _chkUseDateRange = new CheckBox { Text = "Filter by Date Range", AutoSize = true, Margin = new Padding(0, 14, 10, 0) };

        _dtpStart = new DateTimePicker { Width = 150, Height = 36, Format = DateTimePickerFormat.Short, Enabled = false, Margin = new Padding(0, 6, 10, 6) };
        _dtpEnd = new DateTimePicker { Width = 150, Height = 36, Format = DateTimePickerFormat.Short, Enabled = false, Margin = new Padding(0, 6, 20, 6) };

        _chkUseDateRange.CheckedChanged += (_, _) => { _dtpStart.Enabled = _chkUseDateRange.Checked; _dtpEnd.Enabled = _chkUseDateRange.Checked; };

        _chkGroupByDay = new CheckBox { Text = "Group by Day", AutoSize = true, Margin = new Padding(0, 14, 10, 0) };
        _chkGroupByPrice = new CheckBox { Text = "Break Down by Price", AutoSize = true, Margin = new Padding(0, 14, 10, 0) };
        _chkGroupBySource = new CheckBox { Text = "Break Down by Source", AutoSize = true, Margin = new Padding(0, 14, 20, 0) };

        _cboSortBy = new ComboBox { Width = 140, Height = 36, Font = new Font("Segoe UI", 11F), DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 6, 10, 6) };
        _cboSortBy.Items.AddRange(["Quantity", "Revenue"]);
        _cboSortBy.SelectedIndex = 0;

        _numTake = new NumericUpDown { Width = 80, Height = 36, Minimum = 1, Maximum = 100, Value = 10, Margin = new Padding(0, 6, 20, 6) };

        _btnRun = new Button { Text = "Run Report", Width = 150, Height = 44, Font = new Font("Segoe UI", 11F, FontStyle.Bold), Margin = new Padding(0, 6, 10, 6) };
        _btnRun.Click += async (_, _) => await RunReportAsync();

        _btnExport = new Button { Text = "Export to Excel", Width = 160, Height = 44, Font = new Font("Segoe UI", 11F, FontStyle.Bold), Margin = new Padding(0, 6, 0, 6) };
        _btnExport.Click += async (_, _) => await ExportToExcelAsync();

        toolbar.Controls.Add(_cboReportType);
        toolbar.Controls.Add(_chkUseDateRange);
        toolbar.Controls.Add(_dtpStart);
        toolbar.Controls.Add(_dtpEnd);
        toolbar.Controls.Add(_chkGroupByDay);
        toolbar.Controls.Add(_chkGroupByPrice);
        toolbar.Controls.Add(_chkGroupBySource);
        toolbar.Controls.Add(_cboSortBy);
        toolbar.Controls.Add(_numTake);
        toolbar.Controls.Add(_btnRun);
        toolbar.Controls.Add(_btnExport);

        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            RowTemplate = { Height = 36 },
            Font = new Font("Segoe UI", 11F)
        };

        Controls.Add(_grid);
        Controls.Add(toolbar);

        UpdateVisibleOptions();
        Load += async (_, _) => await RunReportAsync();
    }

    private ReportType SelectedReportType => (ReportType)_cboReportType.SelectedIndex;

    private void UpdateVisibleOptions()
    {
        _chkGroupByDay.Visible = SelectedReportType == ReportType.ItemSales;
        _chkGroupByPrice.Visible = SelectedReportType == ReportType.ItemSales;
        _chkGroupBySource.Visible = SelectedReportType == ReportType.ItemSales;
        _cboSortBy.Visible = SelectedReportType == ReportType.TopSellers;
        _numTake.Visible = SelectedReportType == ReportType.TopSellers;
    }

    private (DateTime? start, DateTime? end) GetDateRange() =>
        _chkUseDateRange.Checked ? (_dtpStart.Value.Date, _dtpEnd.Value.Date) : (null, null);

    private async Task RunReportAsync()
    {
        var (start, end) = GetDateRange();
        if (start is not null && end is not null && end < start)
        {
            MessageBox.Show("End date cannot be before start date.", "Invalid Date Range", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        _btnRun.Enabled = false;
        try
        {
            switch (SelectedReportType)
            {
                case ReportType.SalesSummary:
                    var summary = await _apiClient.GetSalesSummaryAsync(start, end);
                    BindGrid(SummaryToRows(summary), ["Metric", "Value"]);
                    break;

                case ReportType.ItemSales:
                    var itemSales = await _apiClient.GetItemSalesAsync(start, end, _chkGroupByDay.Checked, _chkGroupByPrice.Checked, _chkGroupBySource.Checked);
                    BindItemRows(itemSales, _chkGroupByDay.Checked, _chkGroupByPrice.Checked, _chkGroupBySource.Checked);
                    break;

                case ReportType.TopSellers:
                    var sortBy = (TopSellersSortBy)_cboSortBy.SelectedIndex;
                    var topSellers = await _apiClient.GetTopSellersAsync(start, end, sortBy, (int)_numTake.Value);
                    BindItemRows(topSellers, groupedByDay: false, groupedByPrice: false, groupedBySource: false);
                    break;

                case ReportType.StaffPerformance:
                    var staff = await _apiClient.GetStaffPerformanceAsync(start, end);
                    BindStaffRows(staff);
                    break;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not load the report: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _btnRun.Enabled = true;
        }
    }

    private static List<string[]> SummaryToRows(SalesSummaryDto s) =>
    [
        ["Total Orders", s.TotalOrders.ToString()],
        ["Cashier Orders", s.CashierOrders.ToString()],
        ["Mobile Orders", s.MobileOrders.ToString()],
        ["Complimentary Orders", s.ComplimentaryOrders.ToString()],
        ["Total Revenue (incl. tax)", s.TotalRevenue.ToString("0.00")],
        ["Total Revenue (excl. tax)", s.RevenueExcludingTax.ToString("0.00")],
        ["Total Tax", s.TotalTax.ToString("0.00")],
        ["Complimentary Value", s.ComplimentaryValue.ToString("0.00")],
        ["Average Order Value", s.AverageOrderValue.ToString("0.00")],
    ];

    private void BindGrid(List<string[]> rows, string[] headers)
    {
        _grid.DataSource = null;
        _grid.Columns.Clear();
        _grid.AutoGenerateColumns = false;
        foreach (var header in headers)
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = header, MinimumWidth = 120 });

        _grid.Rows.Clear();
        foreach (var row in rows)
            _grid.Rows.Add(row);
    }

    private void BindItemRows(List<ItemSalesRowDto> rows, bool groupedByDay, bool groupedByPrice, bool groupedBySource)
    {
        var headers = new List<string> { "Category", "Item" };
        if (groupedByDay) headers.Add("Date");
        if (groupedByPrice) headers.Add("Sold At Price");
        if (groupedBySource) headers.Add("Source");
        headers.AddRange(["Quantity", "Revenue (incl. tax)", "Revenue (excl. tax)", "Tax", "Complimentary", "Avg Price"]);

        var data = rows.Select(r =>
        {
            var row = new List<string> { r.CategoryName, r.ItemName };
            if (groupedByDay) row.Add(r.OrderDate?.ToString("yyyy-MM-dd") ?? "");
            if (groupedByPrice) row.Add(r.SoldAtPrice?.ToString("0.00") ?? "");
            if (groupedBySource) row.Add(r.Source?.ToString() ?? "");
            row.AddRange([r.Quantity.ToString(), r.Revenue.ToString("0.00"), r.RevenueExcludingTax.ToString("0.00"), r.Tax.ToString("0.00"), r.ComplimentaryValue.ToString("0.00"), r.AveragePrice.ToString("0.00")]);
            return row.ToArray();
        }).ToList();

        BindGrid(data, headers.ToArray());
    }

    private void BindStaffRows(List<StaffPerformanceRowDto> rows)
    {
        var headers = new[] { "User", "Order Count", "Revenue (incl. tax)", "Revenue (excl. tax)", "Tax" };
        var data = rows.Select(r => new[] { r.UserName, r.OrderCount.ToString(), r.Revenue.ToString("0.00"), r.RevenueExcludingTax.ToString("0.00"), r.Tax.ToString("0.00") }).ToList();
        BindGrid(data, headers);
    }

    private async Task ExportToExcelAsync()
    {
        var (start, end) = GetDateRange();

        var (reportPath, extraParams, defaultFileName) = SelectedReportType switch
        {
            ReportType.SalesSummary => ("sales-summary", "", "sales-summary.xlsx"),
            ReportType.ItemSales => ("item-sales", $"&groupByDay={_chkGroupByDay.Checked}&groupByPrice={_chkGroupByPrice.Checked}&groupBySource={_chkGroupBySource.Checked}", "item-sales.xlsx"),
            ReportType.TopSellers => ("top-sellers", $"&sortBy={(TopSellersSortBy)_cboSortBy.SelectedIndex}&take={(int)_numTake.Value}", "top-sellers.xlsx"),
            ReportType.StaffPerformance => ("staff-performance", "", "staff-performance.xlsx"),
            _ => throw new InvalidOperationException()
        };

        _btnExport.Enabled = false;
        try
        {
            var bytes = await _apiClient.DownloadReportExcelAsync(reportPath, start, end, extraParams);

            using var dialog = new SaveFileDialog { FileName = defaultFileName, Filter = "Excel Workbook|*.xlsx" };
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
        finally
        {
            _btnExport.Enabled = true;
        }
    }
}
