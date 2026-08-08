using POS_MB.Mobile.Api;
using POS_MB.Mobile.Models;

namespace POS_MB.Mobile;

public partial class OrdersPage : ContentPage
{
    private readonly ApiClient _apiClient = new();

    public OrdersPage()
    {
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        var orders = await _apiClient.GetMyOrdersAsync();
        OrdersView.ItemsSource = orders;
        PlaceholderLabel.IsVisible = orders.Count == 0;
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
}
