using POS_MB.WinformsApp.Api;
using POS_MB.WinformsApp.Dialogs;
using POS_MB.WinformsApp.Models;
using POS_MB.WinformsApp.Session;

namespace POS_MB.WinformsApp.Controls;

// The kitchen's working queue - "what needs to happen to orders placed today",
// as opposed to OrderHistoryControl which is a read-only historical record.
// Placed->Preparing->Ready->Completed is a straight line (Advance always means
// "move to the next status"), plus Cancel for anything gone wrong. Now one of
// two clients that can drive this queue - the chef tablet (wwwroot/chef) is
// the other, both hitting the same api/orders/{id}/status endpoint.
//
// Mobile orders only - a cashier order is paid and handed over in the same
// moment it's created (starts at Completed, see clsOrderDataAccess.CreateOrderAsync),
// so there's nothing for a working queue to ever do with one.
//
// Advancing Placed -> Preparing ("Accept") no longer prints inline from here -
// see KitchenTicketPrintService (owned by FormMain, runs regardless of which
// screen/client accepted the order) for why that moved out.
public class OrderStatusControl : UserControl
{
    // Matches clsOrderBusiness.AcceptingOnlineOrdersSettingKey - duplicated
    // deliberately, not shared via a project reference, same reasoning as the
    // Permission enum: WinForms talks to the API over HTTP like any other
    // client, so the two sides share the contract (the key name), not the code.
    private const string AcceptingOnlineOrdersSettingKey = "AcceptingOnlineOrders";

    // Matches clsOrderBusiness.MobileOrderAutoCancelMinutesSettingKey/
    // DefaultAutoCancelMinutes - read-only here (the actual editing UI lives on
    // the Settings screen), just so the countdown column can compute the same
    // deadline the server itself uses.
    private const string AutoCancelMinutesSettingKey = "MobileOrderAutoCancelMinutes";
    private const int DefaultAutoCancelMinutes = 10;

    private static readonly TimeSpan AutoRefreshInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan CountdownTickInterval = TimeSpan.FromSeconds(1);

    private readonly ApiClient _apiClient = new();
    private readonly System.Windows.Forms.Timer _autoRefreshTimer;
    private readonly System.Windows.Forms.Timer _countdownTimer;

    private readonly CheckBox _chkShowAll;
    private readonly CheckBox _chkAcceptingOrders;
    private readonly DataGridView _grid;
    private bool _suppressAcceptingOrdersEvent;
    private int _autoCancelMinutes = DefaultAutoCancelMinutes;

    // _allOrders is the raw last fetch (unfiltered/unsorted) - always the true
    // source for re-deriving the filtered view, so toggling "Show All" works
    // even between fetches. _displayedOrders is exactly what's bound to the
    // grid right now, row-for-row - used both as the "previous" baseline for
    // change detection and for mapping a clicked row back to its order.
    private List<OrderDto> _allOrders = [];
    private List<OrderDto> _displayedOrders = [];

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

        // A quick pause switch for new mobile orders - too busy, closing soon,
        // worried about a connectivity blip, whatever the reason. Lives here
        // (not the separate admin Settings screen) since it needs to be
        // flippable by whoever's actually running this screen, instantly, not
        // buried behind a permission a cashier/chef might not even have.
        _chkAcceptingOrders = new CheckBox
        {
            Text = "Accepting Online Orders",
            AutoSize = true,
            Checked = true,
            Margin = new Padding(20, 14, 20, 0),
            Font = new Font("Segoe UI", 11F, FontStyle.Bold)
        };
        _chkAcceptingOrders.CheckedChanged += async (_, _) => await OnAcceptingOrdersToggledAsync();

        toolbar.Controls.Add(_chkShowAll);
        toolbar.Controls.Add(btnRefresh);
        toolbar.Controls.Add(_chkAcceptingOrders);

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
        // Only meaningful for Placed orders (see Grid_CellFormatting) - once
        // accepted, an order is no longer subject to auto-cancel at all.
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "TimeLeft", HeaderText = "Auto-Cancel In", FillWeight = 80, MinimumWidth = 110 });
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
        //
        // The "is the shop watching" heartbeat (clsOrderBusiness.
        // GetAcceptingOnlineOrdersStatusAsync) is sent from FormMain instead
        // of here - it needs to reflect "a staff member is logged in
        // somewhere", not "this specific screen happens to be the active
        // one", since a cashier switching to New Order to actually take a
        // walk-in order is completely normal and shouldn't make mobile
        // ordering look offline.
        _autoRefreshTimer = new System.Windows.Forms.Timer { Interval = (int)AutoRefreshInterval.TotalMilliseconds };
        _autoRefreshTimer.Tick += async (_, _) => await LoadAsync();

        // A lightweight repaint, not a rebind - InvalidateColumn just makes the
        // grid re-run Grid_CellFormatting for that one column so the countdown
        // text updates every second, without touching DataSource/scroll
        // position/selection the way ApplyFilterAndBind's rebind does.
        _countdownTimer = new System.Windows.Forms.Timer { Interval = (int)CountdownTickInterval.TotalMilliseconds };
        _countdownTimer.Tick += (_, _) =>
        {
            if (_grid.Columns["TimeLeft"] is { } column)
                _grid.InvalidateColumn(column.Index);
        };

        Load += async (_, _) =>
        {
            await LoadAcceptingOrdersToggleAsync();
            await LoadAutoCancelMinutesAsync();
            await LoadAsync();
        };
        HandleCreated += (_, _) =>
        {
            _autoRefreshTimer.Start();
            _countdownTimer.Start();
        };
        HandleDestroyed += (_, _) =>
        {
            _autoRefreshTimer.Stop();
            _countdownTimer.Stop();
        };
    }

    // Read-only here (the editing UI lives on the Settings screen) - fetched
    // once at Load, same treatment as the Accepting Online Orders toggle. If
    // staff changes it on the Settings screen while this one's open, it'll
    // pick up the new value next time this screen is reopened.
    private async Task LoadAutoCancelMinutesAsync()
    {
        var value = await _apiClient.GetSettingValueAsync(AutoCancelMinutesSettingKey);
        _autoCancelMinutes = value is not null && int.TryParse(value, out var minutes) && minutes > 0
            ? minutes
            : DefaultAutoCancelMinutes;
    }

    private async Task LoadAcceptingOrdersToggleAsync()
    {
        var value = await _apiClient.GetSettingValueAsync(AcceptingOnlineOrdersSettingKey);

        _suppressAcceptingOrdersEvent = true;
        _chkAcceptingOrders.Checked = value != "false"; // missing/anything else defaults to accepting
        _suppressAcceptingOrdersEvent = false;
    }

    private async Task OnAcceptingOrdersToggledAsync()
    {
        if (_suppressAcceptingOrdersEvent) return;

        var isAccepting = _chkAcceptingOrders.Checked;
        _chkAcceptingOrders.Enabled = false;
        try
        {
            await _apiClient.SetAcceptingOnlineOrdersAsync(isAccepting);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not update this setting: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            await LoadAcceptingOrdersToggleAsync(); // revert the checkbox to whatever the server actually has
        }
        finally
        {
            _chkAcceptingOrders.Enabled = true;
        }
    }

    private void Grid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        var columnName = _grid.Columns[e.ColumnIndex].Name;
        if (e.RowIndex < 0 || e.RowIndex >= _displayedOrders.Count) return;
        var order = _displayedOrders[e.RowIndex];

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
        else if (columnName == "TimeLeft")
        {
            // Only Placed orders are ever subject to auto-cancel
            // (clsOrderBusiness.CancelStaleMobileOrdersAsync) - once accepted,
            // there's nothing counting down anymore.
            if (order.Status != OrderStatus.Placed)
            {
                e.Value = "—";
                e.FormattingApplied = true;
                return;
            }

            var deadlineUtc = order.Date.AddMinutes(_autoCancelMinutes);
            var remaining = deadlineUtc - DateTime.UtcNow;

            // The actual cancellation is a once-a-minute background sweep
            // server-side (MobileOrderAutoCancelService), not instant - so
            // this can sit at 0:00 for up to ~a minute before the order
            // actually flips to Cancelled. "Cancelling..." is honest about
            // that instead of implying it happens the exact instant it hits zero.
            e.Value = remaining > TimeSpan.Zero
                ? $"{(int)remaining.TotalMinutes}:{remaining.Seconds:D2}"
                : "Cancelling...";
            e.FormattingApplied = true;
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
        _allOrders = await _apiClient.GetOrdersAsync(today, today, orderSource: OrderSource.Mobile);
        ApplyFilterAndBind();
    }

    private void ApplyFilterAndBind()
    {
        var visible = _chkShowAll.Checked
            ? _allOrders
            : _allOrders.Where(o => o.Status is OrderStatus.Placed or OrderStatus.Preparing or OrderStatus.Ready).ToList();

        // Oldest-first, matching a real kitchen queue - the order that's been
        // waiting longest belongs at the top, not buried under newer arrivals.
        var newOrders = visible.OrderBy(o => o.Date).ToList();

        // Rebinding DataGridView.DataSource unconditionally resets scroll
        // position to the top every time - disruptive on a screen that
        // auto-refreshes every 15 seconds, since a tap on Advance/Cancel could
        // land on the wrong row if the view jumps mid-reach. Compares against
        // _displayedOrders (what's actually bound right now), never _allOrders
        // (the raw fetch) - comparing against the raw fetch was the bug that
        // made the grid appear permanently empty, since filtering it and then
        // comparing it to itself looks like "nothing changed" even on the
        // very first load.
        if (!HasOrdersChanged(_displayedOrders, newOrders))
            return;

        var scrollRowIndex = _grid.FirstDisplayedScrollingRowIndex;
        var selectedOrderId = _grid.CurrentRow?.DataBoundItem is OrderDto current ? current.OrderId : (int?)null;

        _displayedOrders = newOrders;
        _grid.DataSource = null;
        _grid.DataSource = _displayedOrders;

        if (_displayedOrders.Count == 0) return;

        if (scrollRowIndex >= 0 && scrollRowIndex < _grid.RowCount)
            _grid.FirstDisplayedScrollingRowIndex = scrollRowIndex;

        var restoredIndex = selectedOrderId is int id ? _displayedOrders.FindIndex(o => o.OrderId == id) : -1;
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
        if (e.RowIndex < 0 || e.RowIndex >= _displayedOrders.Count) return;

        var columnName = _grid.Columns[e.ColumnIndex].Name;
        var order = _displayedOrders[e.RowIndex];

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

            // Placed -> Preparing is "Accept" - printing the kitchen ticket is
            // no longer triggered from here, see KitchenTicketPrintService
            // (owned by FormMain, picks this order up on its next poll
            // regardless of which client/screen accepted it).
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
