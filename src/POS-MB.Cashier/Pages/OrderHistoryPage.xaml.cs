using POS_MB.Cashier.Api;
using POS_MB.Cashier.Controls;
using POS_MB.Cashier.Models;
using POS_MB.Cashier.Session;

namespace POS_MB.Cashier.Pages;

// Replaces OrderHistoryControl. Sorting is deliberately not ported (same
// reasoning as Categories/Items/Users).
public partial class OrderHistoryPage : ContentPage
{
    private readonly ApiClient _apiClient = new();

    // _allOrders is the raw last fetch from the server; the grid shows that,
    // filtered (Hide Unpaid Cancellations) - same split as WinForms'
    // _allOrders/_orders, so toggling the filter doesn't need a server
    // round-trip.
    private List<OrderDto> _allOrders = [];

    public OrderHistoryPage()
    {
        InitializeComponent();

        SourcePicker.ItemsSource = new[] { "All Sources", "Cashier", "Mobile" };
        SourcePicker.SelectedIndex = 0;
        SourcePicker.SelectedIndexChanged += async (_, _) => await LoadAsync();

        // Defaults to today only, not all-time - loading every order ever
        // placed on every screen open is unnecessary and gets slower as
        // history grows. Unchecking the filter (or Refresh) still gets the
        // full history on demand.
        StartDatePicker.Date = DateTime.Today;
        EndDatePicker.Date = DateTime.Today;

        OrdersGrid.SetColumns(
            columns:
            [
                new GridColumn("Order #", o => (((OrderDto)o).SerialNumber ?? ((OrderDto)o).OrderId).ToString(), weight: 0.6),
                new GridColumn("Date", o => AppSession.ToLocalDisplay(((OrderDto)o).Date).ToString("yyyy-MM-dd HH:mm"), weight: 1),
                new GridColumn("Source", o => ((OrderDto)o).OrderSource.ToString(), weight: 0.6),
                new GridColumn("Status", o => ((OrderDto)o).Status.ToString(), weight: 0.7),
                new GridColumn("Cancelled By", o => ((OrderDto)o).CancelledBy ?? "", weight: 1),
                new GridColumn("Paid via Paymob", o => ((OrderDto)o).PaymobTransactionId is not null ? "Yes" : "", weight: 0.8),
                new GridColumn("Refunded", o => ((OrderDto)o).RefundedAt is DateTime r ? AppSession.ToLocalDisplay(r).ToString("yyyy-MM-dd HH:mm") : "", weight: 0.9),
                new GridColumn("Total", o => ((OrderDto)o).Total.ToString("0.00"), weight: 0.6),
                new GridColumn("Comp.", o => ((OrderDto)o).IsComplimentary ? "Yes" : "No", weight: 0.5)
            ],
            rowActions: [new GridRowAction("View", async row => await ViewAsync((OrderDto)row))]);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadAsync();
    }

    private void OnUseDateRangeToggled(object? sender, ToggledEventArgs e)
    {
        StartDatePicker.IsEnabled = UseDateRangeSwitch.IsToggled;
        EndDatePicker.IsEnabled = UseDateRangeSwitch.IsToggled;
    }

    private async void OnRefreshClicked(object? sender, EventArgs e) => await LoadAsync();

    private void OnHideUnpaidCancelledToggled(object? sender, ToggledEventArgs e) => ApplyFilterAndBind();

    private async Task LoadAsync()
    {
        DateTime? start = UseDateRangeSwitch.IsToggled ? StartDatePicker.Date : null;
        DateTime? end = UseDateRangeSwitch.IsToggled ? EndDatePicker.Date : null;
        OrderSource? source = SourcePicker.SelectedIndex switch
        {
            1 => OrderSource.Cashier,
            2 => OrderSource.Mobile,
            _ => null
        };

        if (start is not null && end is not null && end < start)
        {
            await UiAlerts.Error(this, "End date cannot be before start date.");
            return;
        }

        _allOrders = await _apiClient.GetOrdersAsync(start, end, source);
        ApplyFilterAndBind();
    }

    // A mobile order cancelled before ever reaching payment - no
    // PaymobTransactionId at all - is what "Hide Unpaid Cancellations"
    // hides. A Cashier order is never paid through Paymob in the first
    // place, so this filter never touches Cashier cancellations.
    private void ApplyFilterAndBind()
    {
        IEnumerable<OrderDto> visible = HideUnpaidCancelledSwitch.IsToggled
            ? _allOrders.Where(o => !(o.Status == OrderStatus.Cancelled && o.OrderSource == OrderSource.Mobile && o.PaymobTransactionId is null))
            : _allOrders;

        OrdersGrid.ItemsSource = visible.Cast<object>();
    }

    private async void OnRowTapped(object? sender, object row) => await ViewAsync((OrderDto)row);

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
}
