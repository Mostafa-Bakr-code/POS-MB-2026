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
    private CancellationTokenSource? _pollCts;

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
    //
    // Uses a plain cancellable Task.Delay loop rather than IDispatcherTimer -
    // MAUI's dispatcher timer has known reliability gaps on some platforms
    // (WinUI in particular), and an unhandled exception inside its Tick
    // handler can silently stop it firing forever with no visible error. This
    // pattern doesn't depend on that infrastructure at all.
    private void StartPolling()
    {
        StopPolling(); // in case OnAppearing ever fires twice without a matching OnDisappearing
        _pollCts = new CancellationTokenSource();
        _ = PollLoopAsync(_pollCts.Token);
    }

    private void StopPolling()
    {
        _pollCts?.Cancel();
        _pollCts = null;
    }

    private async Task PollLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(PollInterval, token);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            if (token.IsCancellationRequested) return;
            await RefreshStatusAsync();
        }
    }

    // Deliberately lighter than LoadAsync - only re-fetches the order itself,
    // not the full item catalog (item names/prices on an already-placed order
    // never change, so re-resolving them every 5 seconds would be wasted work).
    private async Task RefreshStatusAsync()
    {
        OrderDetailDto? updated;
        try
        {
            updated = await _apiClient.GetMyOrderAsync(_orderId);
        }
        catch (Exception)
        {
            // A transient failure (brief connectivity blip) should not clear the
            // screen, interrupt the student, or kill the poll loop - just keep
            // showing the last known good state and try again next tick.
            return;
        }

        if (updated is null || _order is null) return;
        if (updated.Status == _order.Status && updated.Total == _order.Total) return;

        _order = updated;
        MainThread.BeginInvokeOnMainThread(ApplyOrderToUi);
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
            $"Status: {DisplayStatus(_order.Status)}\n" +
            $"Total: {_order.Total:0.00}";

        // Self-cancel only while the kitchen hasn't started yet - once it's
        // Preparing, real ingredients/time are already committed, so there's
        // no turning back for a student (staff retain a broader override
        // elsewhere). Matches the same rule enforced server-side in
        // clsOrderBusiness.CancelForStudentAsync, not just hidden here.
        CancelButton.IsVisible = _order.Status is OrderStatus.Placed;
    }

    // Every other status here is a real workflow step worth showing as-is;
    // AwaitingPayment is purely an internal "checkout isn't finished yet"
    // state that shouldn't normally even be visible for long (the student
    // gets redirected straight here right after paying), but showing the
    // raw enum name if they do land on it mid-payment would be confusing.
    private static string DisplayStatus(OrderStatus status) =>
        status == OrderStatus.AwaitingPayment ? "Waiting for payment" : status.ToString();

    private async void OnCancelClicked(object? sender, EventArgs e)
    {
        var confirmed = await DisplayAlert("Cancel Order", "Are you sure you want to cancel this order?", "Yes", "No");
        if (!confirmed) return;

        CancelButton.IsEnabled = false;
        var (success, error) = await _apiClient.CancelOrderAsync(_orderId);
        CancelButton.IsEnabled = true;

        if (!success)
        {
            await DisplayAlert("Error", error ?? "This order could not be cancelled.", "OK");
            await LoadAsync(); // status may have moved on server-side even though cancel was rejected - refresh to reflect it
            return;
        }

        await LoadAsync();
    }
}
