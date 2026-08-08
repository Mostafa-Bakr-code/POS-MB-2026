using POS_MB.Mobile.Api;
using POS_MB.Mobile.Models;
using POS_MB.Mobile.Session;

namespace POS_MB.Mobile;

public partial class OrderDetailPage : ContentPage
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private readonly ApiClient _apiClient = new();
    private readonly int _orderId;
    private OrderDetailDto? _order;
    private IDispatcherTimer? _pollTimer;

    public OrderDetailPage(int orderId)
    {
        InitializeComponent();
        _orderId = orderId;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadAsync();
        StartPolling();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        StopPolling();
    }

    // Same reasoning as the planned chef-tablet poller (see project notes) - a
    // student staring at this screen right after ordering should see the
    // kitchen's progress without having to back out and back in. A few
    // seconds of lag is imperceptible for a food order, so polling instead of
    // real-time push (SignalR/notifications) keeps this simple.
    private void StartPolling()
    {
        _pollTimer ??= Dispatcher.CreateTimer();
        _pollTimer.Interval = PollInterval;
        _pollTimer.Tick -= OnPollTick; // avoid double-subscribing across appear/disappear cycles
        _pollTimer.Tick += OnPollTick;
        _pollTimer.Start();
    }

    private void StopPolling() => _pollTimer?.Stop();

    private async void OnPollTick(object? sender, EventArgs e) => await RefreshStatusAsync();

    // Deliberately lighter than LoadAsync - only re-fetches the order itself,
    // not the full item catalog (item names/prices on an already-placed order
    // never change, so re-resolving them every 5 seconds would be wasted work).
    private async Task RefreshStatusAsync()
    {
        var updated = await _apiClient.GetMyOrderAsync(_orderId);

        // A transient failure (brief connectivity blip) should not clear the
        // screen or interrupt the student - just keep showing the last known
        // good state and try again on the next tick.
        if (updated is null || _order is null) return;

        if (updated.Status == _order.Status && updated.Total == _order.Total) return;

        _order = updated;
        ApplyOrderToUi();
    }

    private async Task LoadAsync()
    {
        _order = await _apiClient.GetMyOrderAsync(_orderId);
        if (_order is null)
        {
            await DisplayAlert("Not found", "This order could not be loaded.", "OK");
            await Navigation.PopAsync();
            return;
        }

        // availableOnly: false AND includeInactive: true - a past order can
        // reference an item that's since been marked unavailable (out of stock,
        // availableOnly excludes it) or fully retired (includeInactive excludes
        // it) - both are independent filters, and either one alone still hides
        // some historical items from name resolution.
        var allItems = await _apiClient.GetItemsAsync(availableOnly: false, includeInactive: true);
        var namesById = allItems.ToDictionary(i => i.ItemId, i => i.ItemName);

        ItemsView.ItemsSource = _order.Items.Select(i => new OrderItemDisplayDto
        {
            ItemName = namesById.GetValueOrDefault(i.ItemId, $"Item #{i.ItemId}"),
            Quantity = i.Quantity,
            TotalItemsPrice = i.TotalItemsPrice,
            Comment = i.Comment
        }).ToList();

        ApplyOrderToUi();
    }

    private void ApplyOrderToUi()
    {
        if (_order is null) return;

        HeaderLabel.Text =
            $"Order #{_order.SerialNumber ?? _order.OrderId}\n" +
            $"Date: {AppSession.ToLocalDisplay(_order.Date):yyyy-MM-dd HH:mm}\n" +
            $"Status: {_order.Status}\n" +
            $"Total: {_order.Total:0.00}";

        // Cancelling something already Completed or already Cancelled makes no
        // sense - only offer it while the order is still in a state the kitchen
        // hasn't finished (or given up on) yet.
        CancelButton.IsVisible = _order.Status is OrderStatus.Placed or OrderStatus.Preparing;
    }

    private async void OnCancelClicked(object? sender, EventArgs e)
    {
        var confirmed = await DisplayAlert("Cancel Order", "Are you sure you want to cancel this order?", "Yes", "No");
        if (!confirmed) return;

        CancelButton.IsEnabled = false;
        var success = await _apiClient.CancelOrderAsync(_orderId);
        CancelButton.IsEnabled = true;

        if (!success)
        {
            await DisplayAlert("Error", "This order could not be cancelled.", "OK");
            return;
        }

        await LoadAsync();
    }
}
