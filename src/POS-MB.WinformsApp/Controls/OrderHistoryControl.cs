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
    private readonly DataGridView _grid;

    private List<OrderDto> _orders = [];
    private List<UserDto> _users = [];
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

        toolbar.Controls.Add(_chkUseDateRange);
        toolbar.Controls.Add(_dtpStart);
        toolbar.Controls.Add(_dtpEnd);
        toolbar.Controls.Add(_cboSource);
        toolbar.Controls.Add(btnRefresh);

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

        _orders = await _apiClient.GetOrdersAsync(start, end, source);
        _users = await _apiClient.GetUsersAsync(includeInactive: true);

        ApplySortAndBind();
    }

    // DataGridView doesn't support click-to-sort out of the box when bound to a
    // plain List<T> (only IBindingList sources with SupportsSortingCore do) - sort
    // manually and rebind instead.
    private void Grid_ColumnHeaderMouseClick(object? sender, DataGridViewCellMouseEventArgs e)
    {
        var columnName = _grid.Columns[e.ColumnIndex].Name;
        if (columnName is not ("SerialNumber" or "Date" or "OrderSource" or "Status" or "Total" or "IsComplimentary")) return;

        _sortAscending = _sortColumn == columnName && _sortAscending ? false : true;
        _sortColumn = columnName;
        ApplySortAndBind();
    }

    private void ApplySortAndBind()
    {
        IEnumerable<OrderDto> sorted = _sortColumn switch
        {
            "SerialNumber" => _orders.OrderBy(o => o.SerialNumber),
            "Date" => _orders.OrderBy(o => o.Date),
            "OrderSource" => _orders.OrderBy(o => o.OrderSource),
            "Status" => _orders.OrderBy(o => o.Status),
            "Total" => _orders.OrderBy(o => o.Total),
            "IsComplimentary" => _orders.OrderBy(o => o.IsComplimentary),
            _ => _orders
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

        var cashierName = full.UserId is int userId
            ? _users.FirstOrDefault(u => u.UserId == userId)?.UserName
            : null;

        using var dialog = new FormOrderDetailDialog(full, itemNamesById, cashierName);
        dialog.ShowDialog(this);
    }
}
