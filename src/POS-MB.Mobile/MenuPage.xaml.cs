using POS_MB.Mobile.Api;
using POS_MB.Mobile.Models;
using POS_MB.Mobile.Session;

namespace POS_MB.Mobile;

public partial class MenuPage : ContentPage
{
    private readonly ApiClient _apiClient = new();

    public MenuPage()
    {
        InitializeComponent();
        WelcomeLabel.Text = $"Hi, {AppSession.CurrentStudent?.Email}";
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        var categories = await _apiClient.GetCategoriesAsync();
        CategoriesView.ItemsSource = categories;

        await LoadItemsAsync(categoryId: null);
    }

    private async Task LoadItemsAsync(int? categoryId)
    {
        var items = await _apiClient.GetItemsAsync(categoryId);
        ItemsView.ItemsSource = items;
    }

    private async void OnCategorySelected(object? sender, SelectionChangedEventArgs e)
    {
        var selected = e.CurrentSelection.FirstOrDefault() as CategoryDto;
        await LoadItemsAsync(selected?.CategoryId);
    }

    private async void OnRefreshing(object? sender, EventArgs e)
    {
        await LoadAsync();
        ItemsRefreshView.IsRefreshing = false;
    }
}
