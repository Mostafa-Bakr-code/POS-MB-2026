using POS_MB.Mobile.Api;
using POS_MB.Mobile.Models;
using POS_MB.Mobile.Session;

namespace POS_MB.Mobile;

// A CollectionView group needs its items plus something to bind the header to -
// this is the standard MAUI pattern (a List<T> subclass carrying an extra
// display property) rather than a raw IGrouping, which .NET doesn't expose a
// public constructible implementation of.
public class OrderGroup(string label, IEnumerable<OrderSummaryDto> orders) : List<OrderSummaryDto>(orders)
{
    public string Label { get; } = label;
}

public partial class OrdersPage : ContentPage
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    // Bounds the query to a recent rolling window rather than a student's
    // entire multi-year history - keeps the payload small and the grouped
    // list scannable, while "past orders" for a student realistically means
    // recent weeks, not freshman year.
    private const int HistoryWindowDays = 30;

    private readonly ApiClient _apiClient = new();
    private CancellationTokenSource? _pollCts;
    private List<OrderSummaryDto> _currentOrders = [];

    public OrdersPage()
    {
        InitializeComponent();
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

    // Same reasoning/pattern as OrderDetailPage - a student sitting on the
    // order list should see a status change (e.g. an order moving to Ready)
    // without pulling to refresh manually.
    private void StartPolling()
    {
        StopPolling();
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

            try
            {
                await PollRefreshAsync();
            }
            catch (Exception)
            {
                // Transient failure - keep showing the last known list and
                // try again next tick, same as OrderDetailPage.
            }
        }
    }

    // Reassigning ItemsSource - even to an equal-looking list - resets the
    // CollectionView's scroll position and causes a visible flicker. Almost
    // every 5-second tick finds nothing changed, so skip touching the UI
    // entirely in that case; only rebind when something real (a status/total)
    // actually differs. Explicit loads (first open, pull-to-refresh) always go
    // through LoadAsync instead, since the student expects those to visibly
    // refresh.
    private async Task PollRefreshAsync()
    {
        var orders = await FetchOrdersAsync();
        if (!HasOrdersChanged(_currentOrders, orders)) return;

        BindOrders(orders);
    }

    private async Task LoadAsync()
    {
        var orders = await FetchOrdersAsync();
        BindOrders(orders);
    }

    private Task<List<OrderSummaryDto>> FetchOrdersAsync() =>
        _apiClient.GetMyOrdersAsync(DateTime.Today.AddDays(-(HistoryWindowDays - 1)), DateTime.Today);

    private void BindOrders(List<OrderSummaryDto> orders)
    {
        _currentOrders = orders;

        // Date comes back from the API in UTC - convert to local for display only,
        // same as WinForms does; OrderId (used for navigation) is untouched.
        var displayOrders = orders.Select(o => o with { Date = AppSession.ToLocalDisplay(o.Date) }).ToList();

        // Newest day first, and (per the API's own ordering) newest order
        // first within each day - matches how a student expects to scan a
        // history list, most recent activity at the top.
        var groups = displayOrders
            .GroupBy(o => o.Date.Date)
            .OrderByDescending(g => g.Key)
            .Select(g => new OrderGroup(LabelFor(g.Key), g))
            .ToList();

        MainThread.BeginInvokeOnMainThread(() =>
        {
            OrdersView.ItemsSource = groups;
            PlaceholderLabel.IsVisible = displayOrders.Count == 0;
        });
    }

    private static string LabelFor(DateTime localDate)
    {
        var today = DateTime.Today;
        if (localDate == today) return "Today";
        if (localDate == today.AddDays(-1)) return "Yesterday";
        return localDate.ToString("dddd, MMM d");
    }

    private static bool HasOrdersChanged(List<OrderSummaryDto> previous, List<OrderSummaryDto> next)
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

    private async void OnOrderSelected(object? sender, SelectionChangedEventArgs e)
    {
        OrdersView.SelectedItem = null; // reset so the same row can be tapped again later

        if (e.CurrentSelection.FirstOrDefault() is not OrderSummaryDto order) return;

        await Navigation.PushAsync(new OrderDetailPage(order.OrderId));
    }

    private async void OnRefreshing(object? sender, EventArgs e)
    {
        await LoadAsync();
        OrdersRefreshView.IsRefreshing = false;
    }

    private async void OnHomeClicked(object? sender, EventArgs e) =>
        await NavigationHelper.GoHomeAsync(Navigation);
}
