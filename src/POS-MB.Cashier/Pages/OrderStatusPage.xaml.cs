using POS_MB.Cashier.Api;
using POS_MB.Cashier.Controls;
using POS_MB.Cashier.Models;
using POS_MB.Cashier.Session;

namespace POS_MB.Cashier.Pages;

// Replaces OrderStatusControl - the kitchen's working queue (what needs to
// happen to orders placed today), as opposed to OrderHistoryPage which is a
// read-only historical record. Mobile orders only - a Cashier order starts
// at Completed already (see clsOrderDataAccess.CreateOrderAsync), so
// there's nothing for a working queue to ever do with one.
//
// The per-second auto-cancel countdown WinForms had (via InvalidateColumn)
// is deliberately not ported - a visual nicety needing a lightweight
// partial-repaint DataGridView doesn't support yet; the "Auto-Cancel In"
// column still shows a real countdown, just refreshed every 15s along with
// everything else rather than ticking every second.
public partial class OrderStatusPage : ContentPage
{
    // Matches clsOrderBusiness.AcceptingOnlineOrdersSettingKey - duplicated
    // deliberately, same reasoning as WinForms' own copy: this app talks to
    // the API over HTTP like any other client, so the two sides share the
    // contract (the key name), not the code.
    private const string AcceptingOnlineOrdersSettingKey = "AcceptingOnlineOrders";

    private const string AutoCancelMinutesSettingKey = "MobileOrderAutoCancelMinutes";
    private const int DefaultAutoCancelMinutes = 10;

    private static readonly TimeSpan AutoRefreshInterval = TimeSpan.FromSeconds(15);

    private readonly ApiClient _apiClient = new();
    private System.Threading.Timer? _autoRefreshTimer;
    private bool _suppressAcceptingOrdersEvent;
    private int _autoCancelMinutes = DefaultAutoCancelMinutes;
    private List<OrderDto> _allOrders = [];

    public OrderStatusPage()
    {
        InitializeComponent();

        OrdersGrid.SetColumns(
            columns:
            [
                new GridColumn("Order #", o => (((OrderDto)o).SerialNumber ?? ((OrderDto)o).OrderId).ToString(), weight: 0.6),
                new GridColumn("Time", o => AppSession.ToLocalDisplay(((OrderDto)o).Date).ToString("HH:mm"), weight: 0.5),
                new GridColumn("Source", o => ((OrderDto)o).OrderSource.ToString(), weight: 0.6),
                new GridColumn("Placed By", o => ((OrderDto)o).CashierName ?? ((OrderDto)o).StudentEmail ?? "", weight: 1),
                new GridColumn("Status", o => ((OrderDto)o).Status.ToString(), weight: 0.7),
                new GridColumn("Auto-Cancel In", o => TimeLeftText((OrderDto)o), weight: 0.8),
                new GridColumn("Total", o => ((OrderDto)o).Total.ToString("0.00"), weight: 0.6)
            ],
            rowActions:
            [
                new GridRowAction("View", async row => await ViewAsync((OrderDto)row)),
                new GridRowAction(row => AdvanceLabel(((OrderDto)row).Status), async row => await AdvanceAsync((OrderDto)row)),
                new GridRowAction("Cancel", async row => await CancelAsync((OrderDto)row))
            ]);
    }

    private static string AdvanceLabel(OrderStatus status) => status switch
    {
        OrderStatus.Placed => "Start Preparing",
        OrderStatus.Preparing => "Mark Ready",
        OrderStatus.Ready => "Complete",
        _ => "" // terminal state - only reachable with "Show Completed/Cancelled" on
    };

    private string TimeLeftText(OrderDto order)
    {
        // Only Placed orders are ever subject to auto-cancel - once
        // accepted, there's nothing counting down anymore.
        if (order.Status != OrderStatus.Placed) return "—";

        // UpdatedAt, not Date - a mobile order sits at AwaitingPayment
        // before Placed, so anchoring to order creation would silently burn
        // however long checkout took before the kitchen could ever have
        // seen it.
        var deadlineUtc = order.UpdatedAt.AddMinutes(_autoCancelMinutes);
        var remaining = deadlineUtc - DateTime.UtcNow;

        // The actual cancellation is a once-a-minute background sweep
        // server-side, not instant - "Cancelling..." is honest about that
        // instead of implying it happens the exact instant it hits zero.
        return remaining > TimeSpan.Zero
            ? $"{(int)remaining.TotalMinutes}:{remaining.Seconds:D2}"
            : "Cancelling...";
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        await LoadAcceptingOrdersToggleAsync();
        await LoadAutoCancelMinutesAsync();
        await LoadAsync();

        _autoRefreshTimer = new System.Threading.Timer(
            _ => MainThread.BeginInvokeOnMainThread(async () => await LoadAsync()),
            null, AutoRefreshInterval, AutoRefreshInterval);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _autoRefreshTimer?.Dispose();
        _autoRefreshTimer = null;
    }

    // Read-only here (the editing UI lives on Settings) - fetched once on
    // appearing, same treatment as the Accepting Online Orders toggle.
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
        AcceptingOrdersSwitch.IsToggled = value != "false"; // missing/anything else defaults to accepting
        _suppressAcceptingOrdersEvent = false;
    }

    private async void OnAcceptingOrdersToggled(object? sender, ToggledEventArgs e)
    {
        if (_suppressAcceptingOrdersEvent) return;

        var isAccepting = AcceptingOrdersSwitch.IsToggled;
        AcceptingOrdersSwitch.IsEnabled = false;
        try
        {
            var (success, error) = await _apiClient.SetAcceptingOnlineOrdersAsync(isAccepting);
            if (!success)
            {
                await UiAlerts.Error(this, error ?? "Could not update this setting.");
                await LoadAcceptingOrdersToggleAsync(); // revert to whatever the server actually has
            }
        }
        finally
        {
            AcceptingOrdersSwitch.IsEnabled = true;
        }
    }

    private async void OnRefreshClicked(object? sender, EventArgs e) => await LoadAsync();

    private void OnShowAllToggled(object? sender, ToggledEventArgs e) => ApplyFilterAndBind();

    private async Task LoadAsync()
    {
        // Cashier orders are paid and handed over at the register in the
        // same moment they're created - they start at Completed already,
        // so they'd never meaningfully appear in a working queue anyway.
        var today = DateTime.Today;
        _allOrders = await _apiClient.GetOrdersAsync(today, today, orderSource: OrderSource.Mobile);
        ApplyFilterAndBind();
    }

    private void ApplyFilterAndBind()
    {
        var visible = ShowAllSwitch.IsToggled
            ? _allOrders
            : _allOrders.Where(o => o.Status is OrderStatus.Placed or OrderStatus.Preparing or OrderStatus.Ready).ToList();

        // Oldest-first, matching a real kitchen queue - the order that's
        // been waiting longest belongs at the top.
        OrdersGrid.ItemsSource = visible.OrderBy(o => o.Date).Cast<object>();
    }

    private async Task ViewAsync(OrderDto order)
    {
        var full = await _apiClient.GetOrderByIdAsync(order.OrderId);
        if (full is null)
        {
            await UiAlerts.Error(this, "This order could not be loaded.");
            return;
        }

        var allItems = await _apiClient.GetItemsAsync(includeInactive: true);
        var itemNamesById = allItems.ToDictionary(i => i.ItemId, i => i.ItemName);

        await Navigation.PushAsync(new OrderDetailPage(full, itemNamesById));
    }

    private async Task AdvanceAsync(OrderDto order)
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
            await UiAlerts.Error(this, "Could not update this order's status.");
            return;
        }

        // Placed -> Preparing is "Accept" - printing the kitchen ticket
        // isn't triggered from here at all; MainShellPage's own
        // KitchenTicketPrintService (running regardless of which page is
        // open) picks this order up on its next poll tick.
        await LoadAsync();
    }

    private async Task CancelAsync(OrderDto order)
    {
        if (order.Status is OrderStatus.Completed or OrderStatus.Cancelled) return;

        var confirmed = await UiAlerts.Confirm(this, "Cancel Order", $"Cancel order #{order.SerialNumber ?? order.OrderId}?");
        if (!confirmed) return;

        var success = await _apiClient.CancelOrderAsync(order.OrderId);
        if (!success)
        {
            await UiAlerts.Error(this, "Could not cancel this order.");
            return;
        }

        await LoadAsync();
    }
}
