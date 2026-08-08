using POS_MB.WinformsApp.Api;
using POS_MB.WinformsApp.Dialogs;
using POS_MB.WinformsApp.Models;
using POS_MB.WinformsApp.Session;

namespace POS_MB.WinformsApp.Controls;

// The kitchen's working queue - "what needs to happen to orders placed today",
// as opposed to OrderHistoryControl which is a read-only historical record.
// Placed->Preparing->Ready->Completed is a straight line (Advance always means
// "move to the next status"), plus Cancel for anything gone wrong. This is an
// interim stand-in for the planned chef-tablet web client (see project notes) -
// same underlying api/orders/{id}/status endpoint, just from a WinForms screen
// that exists today.
//
// Mobile orders only - a cashier order is paid and handed over in the same
// moment it's created (starts at Completed, see clsOrderDataAccess.CreateOrderAsync),
// so there's nothing for a working queue to ever do with one.
public class OrderStatusControl : UserControl
{
    private static readonly TimeSpan AutoRefreshInterval = TimeSpan.FromSeconds(15);

    private readonly ApiClient _apiClient = new();
    private readonly System.Windows.Forms.Timer _autoRefreshTimer;

    private readonly CheckBox _chkShowAll;
    private readonly DataGridView _grid;

    private List<OrderDto> _orders = [];

    public OrderStatusControl()
    {
        Font = new Font("Segoe UI", 12F);

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 60, Padding = new Padding(10) };

        // Defaults to just the active queue (Placed/Preparing/Ready) - completed
        // and cancelled orders belong to Order History, not a working queue that's
        // meant to be glanced at repeatedly during a shift.
        _chkShowAll = new CheckBox { Text = "Show Completed/Cancelled Too", AutoSize = true, Margin = new Padding(0, 14, 20, 0) };
        _chkShowAll.CheckedChanged += (_, _) => ApplyFilterAndBind();

        var btnRefresh = new Button { Text = "Refresh", Width = 130, Height = 40, Font = new Font("Segoe UI", 11F, FontStyle.Bold) };
        btnRefresh.Click += async (_, _) => await LoadAsync();

        toolbar.Controls.Add(_chkShowAll);
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
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "SerialNumber", HeaderText = "Order #", DataPropertyName = "SerialNumber", FillWeight = 60, MinimumWidth = 80 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Date", HeaderText = "Time", DataPropertyName = "Date", FillWeight = 100, MinimumWidth = 130 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "OrderSource", HeaderText = "Source", DataPropertyName = "OrderSource", FillWeight = 70, MinimumWidth = 90 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "PlacedBy", HeaderText = "Placed By", FillWeight = 110, MinimumWidth = 140 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "Status", DataPropertyName = "Status", FillWeight = 80, MinimumWidth = 100 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Total", HeaderText = "Total", DataPropertyName = "Total", FillWeight = 70, MinimumWidth = 90 });
        _grid.Columns.Add(new DataGridViewButtonColumn { Name = "View", HeaderText = "", Text = "View", UseColumnTextForButtonValue = true, FillWeight = 70, MinimumWidth = 90 });
        _grid.Columns.Add(new DataGridViewButtonColumn { Name = "Advance", HeaderText = "", Text = "Advance", UseColumnTextForButtonValue = true, FillWeight = 90, MinimumWidth = 110 });
        _grid.Columns.Add(new DataGridViewButtonColumn { Name = "Cancel", HeaderText = "", Text = "Cancel", UseColumnTextForButtonValue = true, FillWeight = 80, MinimumWidth = 100 });
        _grid.CellFormatting += Grid_CellFormatting;
        _grid.CellClick += Grid_CellClick;

        Controls.Add(_grid);
        Controls.Add(toolbar);

        // A kitchen screen is meant to sit on-screen and stay current without
        // someone remembering to tap Refresh every time an order comes in -
        // same reasoning as the planned chef-tablet poller, just implemented
        // here first since the tablet client doesn't exist yet.
        _autoRefreshTimer = new System.Windows.Forms.Timer { Interval = (int)AutoRefreshInterval.TotalMilliseconds };
        _autoRefreshTimer.Tick += async (_, _) => await LoadAsync();

        Load += async (_, _) => await LoadAsync();
        HandleCreated += (_, _) => _autoRefreshTimer.Start();
        HandleDestroyed += (_, _) => _autoRefreshTimer.Stop();
    }

    private void Grid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        var columnName = _grid.Columns[e.ColumnIndex].Name;
        if (e.RowIndex < 0 || e.RowIndex >= _orders.Count) return;
        var order = _orders[e.RowIndex];

        if (columnName == "Date" && e.Value is DateTime date)
        {
            e.Value = AppSession.ToLocalDisplay(date).ToString("HH:mm");
            e.FormattingApplied = true;
        }
        else if (columnName == "Total" && e.Value is decimal total)
        {
            e.Value = total.ToString("0.00");
            e.FormattingApplied = true;
        }
        else if (columnName == "PlacedBy")
        {
            e.Value = order.CashierName ?? order.StudentEmail ?? "";
            e.FormattingApplied = true;
        }
        else if (columnName == "Advance")
        {
            e.Value = order.Status switch
            {
                OrderStatus.Placed => "Start Preparing",
                OrderStatus.Preparing => "Mark Ready",
                OrderStatus.Ready => "Complete",
                _ => "" // terminal state - only reachable with "Show Completed/Cancelled" checked
            };
        }
    }

    private async Task LoadAsync()
    {
        // Cashier orders are paid and handed over at the register in the same
        // moment they're created (see clsOrderDataAccess.CreateOrderAsync) -
        // they start at Completed already, so they'd never meaningfully
        // appear in a working queue anyway. Filtering by source here mainly
        // matters for cashier orders that existed before this change (still
        // sitting at Placed from back then).
        var today = DateTime.Today;
        _orders = await _apiClient.GetOrdersAsync(today, today, orderSource: OrderSource.Mobile);
        ApplyFilterAndBind();
    }

    private void ApplyFilterAndBind()
    {
        var visible = _chkShowAll.Checked
            ? _orders
            : _orders.Where(o => o.Status is OrderStatus.Placed or OrderStatus.Preparing or OrderStatus.Ready).ToList();

        // Oldest-first, matching a real kitchen queue - the order that's been
        // waiting longest belongs at the top, not buried under newer arrivals.
        var newOrders = visible.OrderBy(o => o.Date).ToList();

        // Rebinding DataGridView.DataSource unconditionally resets scroll
        // position to the top every time - disruptive on a screen that
        // auto-refreshes every 15 seconds, since a tap on Advance/Cancel could
        // land on the wrong row if the view jumps mid-reach. Most ticks find
        // nothing actually changed, so skip the rebind entirely in that case;
        // when something genuinely did change, still preserve where the
        // cashier was looking instead of snapping back to row 0.
        if (!HasOrdersChanged(_orders, newOrders))
        {
            _orders = newOrders;
            return;
        }

        var scrollRowIndex = _grid.FirstDisplayedScrollingRowIndex;
        var selectedOrderId = _grid.CurrentRow?.DataBoundItem is OrderDto current ? current.OrderId : (int?)null;

        _orders = newOrders;
        _grid.DataSource = null;
        _grid.DataSource = _orders;

        if (_orders.Count == 0) return;

        if (scrollRowIndex >= 0 && scrollRowIndex < _grid.RowCount)
            _grid.FirstDisplayedScrollingRowIndex = scrollRowIndex;

        var restoredIndex = selectedOrderId is int id ? _orders.FindIndex(o => o.OrderId == id) : -1;
        if (restoredIndex >= 0)
            _grid.CurrentCell = _grid.Rows[restoredIndex].Cells[0];
    }

    // Order rows carry no version/timestamp of their own, so equality is
    // whatever the grid actually displays: identity, status and total, in the
    // same order. Anything else changing server-side (e.g. items on an order
    // still Placed) wouldn't show up on this screen anyway.
    private static bool HasOrdersChanged(List<OrderDto> previous, List<OrderDto> next)
    {
        if (previous.Count != next.Count) return true;

        for (var i = 0; i < previous.Count; i++)
        {
            var a = previous[i];
            var b = next[i];
            if (a.OrderId != b.OrderId || a.Status != b.Status || a.Total != b.Total)
                return true;
        }

        return false;
    }

    private async void Grid_CellClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _orders.Count) return;

        var columnName = _grid.Columns[e.ColumnIndex].Name;
        var order = _orders[e.RowIndex];

        if (columnName == "View")
        {
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
        else if (columnName == "Advance")
        {
            var next = order.Status switch
            {
                OrderStatus.Placed => OrderStatus.Preparing,
                OrderStatus.Preparing => OrderStatus.Ready,
                OrderStatus.Ready => OrderStatus.Completed,
                _ => (OrderStatus?)null // already terminal - nothing to advance to
            };
            if (next is null) return;

            var success = await _apiClient.UpdateOrderStatusAsync(order.OrderId, next.Value);
            if (!success)
            {
                MessageBox.Show("Could not update this order's status.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            await LoadAsync();
        }
        else if (columnName == "Cancel")
        {
            if (order.Status is OrderStatus.Completed or OrderStatus.Cancelled) return;

            var confirmed = MessageBox.Show($"Cancel order #{order.SerialNumber ?? order.OrderId}?", "Cancel Order",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (confirmed != DialogResult.Yes) return;

            var success = await _apiClient.CancelOrderAsync(order.OrderId);
            if (!success)
            {
                MessageBox.Show("Could not cancel this order.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            await LoadAsync();
        }
    }
}
