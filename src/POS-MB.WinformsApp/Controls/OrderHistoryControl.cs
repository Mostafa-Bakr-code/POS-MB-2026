using POS_MB.WinformsApp.Api;
using POS_MB.WinformsApp.Dialogs;
using POS_MB.WinformsApp.Models;
using POS_MB.WinformsApp.Session;

namespace POS_MB.WinformsApp.Controls;

public class OrderHistoryControl : UserControl
{
    private readonly ApiClient _apiClient = new();

    private readonly CheckBox _chkUseDateRange;
    private readonly DateTimePicker _dtpStart;
    private readonly DateTimePicker _dtpEnd;
    private readonly ComboBox _cboSource;
    private readonly CheckBox _chkHideUnpaidCancelled;
    private readonly DataGridView _grid;

    // _allOrders is the raw last fetch from the server; _orders is that,
    // filtered (Hide Unpaid Cancellations) and sorted, which is what's
    // actually bound to the grid - same split as OrderStatusControl's
    // _allOrders/_displayedOrders, so toggling the filter doesn't require a
    // server round-trip and never loses the underlying data.
    private List<OrderDto> _allOrders = [];
    private List<OrderDto> _orders = [];
    private string? _sortColumn;
    private bool _sortAscending = true;

    public OrderHistoryControl()
    {
        Font = new Font("Segoe UI", 12F);

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 70, Padding = new Padding(10), AutoScroll = true };

        // Defaults to today only, not all-time - loading every order ever placed on
        // every screen open is unnecessary and gets slower as history grows. "Refresh"
        // (or unchecking the filter) still gets the full history on demand.
        _chkUseDateRange = new CheckBox { Text = "Filter by Date Range", AutoSize = true, Checked = true, Margin = new Padding(0, 14, 10, 0) };

        _dtpStart = new DateTimePicker { Width = 150, Height = 36, Format = DateTimePickerFormat.Short, Margin = new Padding(0, 6, 10, 6) };
        _dtpEnd = new DateTimePicker { Width = 150, Height = 36, Format = DateTimePickerFormat.Short, Margin = new Padding(0, 6, 20, 6) };

        _chkUseDateRange.CheckedChanged += (_, _) => { _dtpStart.Enabled = _chkUseDateRange.Checked; _dtpEnd.Enabled = _chkUseDateRange.Checked; };

        _cboSource = new ComboBox { Width = 150, Height = 36, Font = new Font("Segoe UI", 11F), DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 6, 20, 6) };
        _cboSource.Items.AddRange(["All Sources", "Cashier", "Mobile"]);
        _cboSource.SelectedIndex = 0;

        var btnRefresh = new Button { Text = "Refresh", Width = 130, Height = 44, Font = new Font("Segoe UI", 11F, FontStyle.Bold), Margin = new Padding(0, 6, 0, 6) };
        btnRefresh.Click += async (_, _) => await LoadAsync();

        // Off by default - a mobile order cancelled before ever paying (backed
        // out of checkout, or the abandoned-payment safety net caught it) is
        // still real signal (how often do students start an order and not
        // finish?), not just clutter, so it stays visible unless staff
        // deliberately wants a cleaner view. Purely a client-side filter over
        // the already-fetched _allOrders - no server round-trip needed.
        _chkHideUnpaidCancelled = new CheckBox { Text = "Hide Unpaid Cancellations", AutoSize = true, Margin = new Padding(0, 14, 10, 0) };
        _chkHideUnpaidCancelled.CheckedChanged += (_, _) => ApplySortAndBind();

        toolbar.Controls.Add(_chkUseDateRange);
        toolbar.Controls.Add(_dtpStart);
        toolbar.Controls.Add(_dtpEnd);
        toolbar.Controls.Add(_cboSource);
        toolbar.Controls.Add(btnRefresh);
        toolbar.Controls.Add(_chkHideUnpaidCancelled);

        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            RowTemplate = { Height = 40 },
            Font = new Font("Segoe UI", 11F)
        };
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "SerialNumber", HeaderText = "Order #", DataPropertyName = "SerialNumber", FillWeight = 70, MinimumWidth = 90 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Date", HeaderText = "Date", DataPropertyName = "Date", FillWeight = 110, MinimumWidth = 150 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "OrderSource", HeaderText = "Source", DataPropertyName = "OrderSource", FillWeight = 70, MinimumWidth = 90 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "Status", DataPropertyName = "Status", FillWeight = 80, MinimumWidth = 100 });
        // Only meaningful for a Cancelled order - blank for everything else.
        // "Student"/"Staff: {username}" are a real person's decision; the two
        // "Auto (...)" reasons are the automatic sweeps (see
        // clsOrderBusiness.CancelStaleMobileOrdersAsync/CancelAbandonedPaymentsAsync)
        // - telling these apart at a glance is the whole point of this column,
        // since "kitchen never noticed it" and "student changed their mind"
        // call for very different follow-up.
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "CancelledBy", HeaderText = "Cancelled By", DataPropertyName = "CancelledBy", FillWeight = 90, MinimumWidth = 130 });
        // Lets staff tell apart, at a glance, a Cancelled mobile order that
        // was actually charged (backed out/refunded after paying - has a
        // matching transaction on Paymob's own dashboard) from one that
        // never reached payment at all (backed out of checkout, or the
        // abandoned-payment safety net caught it - genuinely nothing on
        // Paymob's side to show, not a discrepancy). A Cashier order is
        // never paid through Paymob at all, so this is always blank there.
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "PaidViaPaymob", HeaderText = "Paid via Paymob", DataPropertyName = "PaymobTransactionId", FillWeight = 80, MinimumWidth = 110 });
        // Only ever meaningful for a Mobile order that was paid through
        // Paymob and later cancelled - see clsOrderBusiness.RefundIfPaidAsync.
        // Blank for everything else (never paid through Paymob, or paid but
        // not cancelled) rather than an explicit "No", since "was this ever
        // even eligible for a refund" isn't the question this answers.
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Refunded", HeaderText = "Refunded", DataPropertyName = "RefundedAt", FillWeight = 70, MinimumWidth = 90 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Total", HeaderText = "Total", DataPropertyName = "Total", FillWeight = 70, MinimumWidth = 90 });
        _grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "IsComplimentary", HeaderText = "Comp.", DataPropertyName = "IsComplimentary", FillWeight = 50, MinimumWidth = 60 });
        _grid.Columns.Add(new DataGridViewButtonColumn { Name = "View", HeaderText = "", Text = "View", UseColumnTextForButtonValue = true, FillWeight = 70, MinimumWidth = 90 });
        // Programmatic (not the default Automatic) so the grid never attempts its own
        // built-in sort-on-click - that path collides with checkbox columns (they try
        // to commit cell edit state mid-sort) and throws. Grid_ColumnHeaderMouseClick
        // handles all sorting manually instead.
        foreach (DataGridViewColumn column in _grid.Columns) column.SortMode = DataGridViewColumnSortMode.Programmatic;
        _grid.CellFormatting += Grid_CellFormatting;
        _grid.CellClick += Grid_CellClick;
        _grid.ColumnHeaderMouseClick += Grid_ColumnHeaderMouseClick;

        Controls.Add(_grid);
        Controls.Add(toolbar);

        Load += async (_, _) => await LoadAsync();
    }

    private void Grid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (_grid.Columns[e.ColumnIndex].Name == "Date" && e.Value is DateTime date)
        {
            e.Value = AppSession.ToLocalDisplay(date).ToString("yyyy-MM-dd HH:mm");
            e.FormattingApplied = true;
        }
        else if (_grid.Columns[e.ColumnIndex].Name == "Total" && e.Value is decimal total)
        {
            e.Value = total.ToString("0.00");
            e.FormattingApplied = true;
        }
        else if (_grid.Columns[e.ColumnIndex].Name == "Refunded")
        {
            e.Value = e.Value is DateTime refundedAt ? AppSession.ToLocalDisplay(refundedAt).ToString("yyyy-MM-dd HH:mm") : "";
            e.FormattingApplied = true;
        }
        else if (_grid.Columns[e.ColumnIndex].Name == "PaidViaPaymob")
        {
            e.Value = e.Value is long ? "Yes" : "";
            e.FormattingApplied = true;
        }
    }

    private async Task LoadAsync()
    {
        DateTime? start = _chkUseDateRange.Checked ? _dtpStart.Value.Date : null;
        DateTime? end = _chkUseDateRange.Checked ? _dtpEnd.Value.Date : null;
        OrderSource? source = _cboSource.SelectedIndex switch
        {
            1 => OrderSource.Cashier,
            2 => OrderSource.Mobile,
            _ => null
        };

        if (start is not null && end is not null && end < start)
        {
            MessageBox.Show("End date cannot be before start date.", "Invalid Date Range", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        _allOrders = await _apiClient.GetOrdersAsync(start, end, source);

        ApplySortAndBind();
    }

    // DataGridView doesn't support click-to-sort out of the box when bound to a
    // plain List<T> (only IBindingList sources with SupportsSortingCore do) - sort
    // manually and rebind instead.
    private void Grid_ColumnHeaderMouseClick(object? sender, DataGridViewCellMouseEventArgs e)
    {
        var columnName = _grid.Columns[e.ColumnIndex].Name;
        if (columnName is not ("SerialNumber" or "Date" or "OrderSource" or "Status" or "Total" or "IsComplimentary" or "Refunded" or "PaidViaPaymob" or "CancelledBy")) return;

        _sortAscending = _sortColumn == columnName && _sortAscending ? false : true;
        _sortColumn = columnName;
        ApplySortAndBind();
    }

    private void ApplySortAndBind()
    {
        // A mobile order cancelled before ever reaching payment - no
        // PaymobTransactionId at all - is what "Hide Unpaid Cancellations"
        // hides. A Cashier order is never paid through Paymob in the first
        // place, so this filter never touches Cashier cancellations - those
        // are real walk-in activity, not checkout abandonment.
        IEnumerable<OrderDto> visible = _chkHideUnpaidCancelled.Checked
            ? _allOrders.Where(o => !(o.Status == OrderStatus.Cancelled && o.OrderSource == OrderSource.Mobile && o.PaymobTransactionId is null))
            : _allOrders;

        IEnumerable<OrderDto> sorted = _sortColumn switch
        {
            "SerialNumber" => visible.OrderBy(o => o.SerialNumber),
            "Date" => visible.OrderBy(o => o.Date),
            "OrderSource" => visible.OrderBy(o => o.OrderSource),
            "Status" => visible.OrderBy(o => o.Status),
            "Total" => visible.OrderBy(o => o.Total),
            "IsComplimentary" => visible.OrderBy(o => o.IsComplimentary),
            "Refunded" => visible.OrderBy(o => o.RefundedAt),
            "PaidViaPaymob" => visible.OrderBy(o => o.PaymobTransactionId),
            "CancelledBy" => visible.OrderBy(o => o.CancelledBy),
            _ => visible
        };
        if (_sortColumn is not null && !_sortAscending) sorted = sorted.Reverse();

        foreach (DataGridViewColumn column in _grid.Columns)
            column.HeaderCell.SortGlyphDirection = column.Name == _sortColumn
                ? (_sortAscending ? SortOrder.Ascending : SortOrder.Descending)
                : SortOrder.None;

        _orders = sorted.ToList();
        _grid.DataSource = null;
        _grid.DataSource = _orders;
    }

    private async void Grid_CellClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || _grid.Columns[e.ColumnIndex].Name != "View") return;

        var order = _orders[e.RowIndex];
        var full = await _apiClient.GetOrderByIdAsync(order.OrderId);
        if (full is null)
        {
            MessageBox.Show("This order could not be loaded.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var allItems = await _apiClient.GetItemsAsync(includeInactive: true);
        var itemNamesById = allItems.ToDictionary(i => i.ItemId, i => i.ItemName);

        using var dialog = new FormOrderDetailDialog(full, itemNamesById);
        dialog.ShowDialog(this);
    }
}
