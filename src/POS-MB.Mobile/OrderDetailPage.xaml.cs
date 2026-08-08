using POS_MB.Mobile.Api;
using POS_MB.Mobile.Models;

namespace POS_MB.Mobile;

public partial class OrderDetailPage : ContentPage
{
    private readonly ApiClient _apiClient = new();
    private readonly int _orderId;
    private OrderDetailDto? _order;

    public OrderDetailPage(int orderId)
    {
        InitializeComponent();
        _orderId = orderId;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadAsync();
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

        HeaderLabel.Text =
            $"Order #{_order.SerialNumber ?? _order.OrderId}\n" +
            $"Date: {_order.Date:yyyy-MM-dd HH:mm}\n" +
            $"Status: {_order.Status}\n" +
            $"Total: {_order.Total:0.00}";

        // includeInactive: true - a past order can reference an item that's since
        // been made unavailable or retired, and it still needs to show its real
        // name here, not just an id.
        var allItems = await _apiClient.GetItemsAsync(includeInactive: true);
        var namesById = allItems.ToDictionary(i => i.ItemId, i => i.ItemName);

        ItemsView.ItemsSource = _order.Items.Select(i => new OrderItemDisplayDto
        {
            ItemName = namesById.GetValueOrDefault(i.ItemId, $"Item #{i.ItemId}"),
            Quantity = i.Quantity,
            TotalItemsPrice = i.TotalItemsPrice,
            Comment = i.Comment
        }).ToList();

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
