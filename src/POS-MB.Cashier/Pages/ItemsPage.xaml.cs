using POS_MB.Cashier.Api;
using POS_MB.Cashier.Controls;
using POS_MB.Cashier.Models;

namespace POS_MB.Cashier.Pages;

// Replaces ItemsControl. Sorting and the out-of-stock/category-inactive row
// highlighting from the WinForms original are deliberately not ported -
// visual niceties DataGridView doesn't support yet, not core CRUD features;
// the "In Stock"/"Active" columns already say the same thing in text.
public partial class ItemsPage : ContentPage
{
    private readonly ApiClient _apiClient = new();
    private List<ItemDto> _items = [];
    private List<CategoryDto> _categories = [];

    public ItemsPage()
    {
        InitializeComponent();

        ItemsGrid.SetColumns(
            columns:
            [
                new GridColumn("Id", i => ((ItemDto)i).ItemId.ToString(), weight: 0.4),
                new GridColumn("Name", i => ((ItemDto)i).ItemName, weight: 1.6),
                new GridColumn("Category", i => CategoryName(((ItemDto)i).CategoryId), weight: 1.2),
                new GridColumn("Price", i => ((ItemDto)i).Price.ToString("0.00"), weight: 0.7),
                new GridColumn("Active", i => ((ItemDto)i).IsActive ? "Yes" : "No", weight: 0.5),
                new GridColumn("In Stock", i => ((ItemDto)i).IsAvailable ? "Yes" : "No", weight: 0.5)
            ],
            rowActions:
            [
                new GridRowAction("Edit", async row => await EditAsync((ItemDto)row)),
                new GridRowAction(
                    row => ((ItemDto)row).IsAvailable ? "Mark Out of Stock" : "Mark In Stock",
                    async row => await ToggleAvailabilityAsync((ItemDto)row)),
                new GridRowAction("Price History", async row => await ShowHistoryAsync((ItemDto)row)),
                new GridRowAction(
                    row => ((ItemDto)row).IsActive ? "Deactivate" : "Reactivate",
                    async row => await ToggleActiveAsync((ItemDto)row))
            ]);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadAsync();
    }

    private string CategoryName(int categoryId) =>
        _categories.FirstOrDefault(c => c.CategoryId == categoryId)?.CategoryName ?? "?";

    private async Task LoadAsync()
    {
        _categories = await _apiClient.GetCategoriesAsync(includeInactive: true);

        var previousSelection = (CategoryFilterPicker.SelectedItem as CategoryFilterOption)?.CategoryId;

        CategoryFilterPicker.SelectedIndexChanged -= OnCategoryFilterChanged;
        var options = new List<CategoryFilterOption> { new("All Categories", null) };
        options.AddRange(_categories.Select(c => new CategoryFilterOption(c.CategoryName, c.CategoryId)));
        CategoryFilterPicker.ItemsSource = options;
        CategoryFilterPicker.ItemDisplayBinding = new Binding(nameof(CategoryFilterOption.Name));
        CategoryFilterPicker.SelectedItem = options.FirstOrDefault(o => o.CategoryId == previousSelection) ?? options[0];
        CategoryFilterPicker.SelectedIndexChanged += OnCategoryFilterChanged;

        var selectedCategoryId = (CategoryFilterPicker.SelectedItem as CategoryFilterOption)?.CategoryId;
        _items = await _apiClient.GetItemsAsync(selectedCategoryId, includeInactive: ShowInactiveSwitch.IsToggled);
        ItemsGrid.ItemsSource = _items.Cast<object>();
    }

    private async void OnCategoryFilterChanged(object? sender, EventArgs e) => await LoadAsync();

    private async void OnShowInactiveToggled(object? sender, ToggledEventArgs e) => await LoadAsync();

    private async void OnRowTapped(object? sender, object row) => await EditAsync((ItemDto)row);

    private async void OnAddClicked(object? sender, EventArgs e)
    {
        var activeCategories = CategoryOptionsFor();
        if (activeCategories.Count == 0)
        {
            await UiAlerts.Info(this, "No Categories", "Add an active category first.");
            return;
        }

        var page = new ItemEditPage("Add Item", activeCategories);
        await Navigation.PushModalAsync(page);
        if (!await page.Completion || !page.IsValid) return;

        var (itemId, error) = await _apiClient.CreateItemAsync(page.ItemName, page.CategoryId!.Value, page.Price, page.Description);
        if (itemId is null)
        {
            await UiAlerts.Error(this, error ?? "Could not create the item.");
            return;
        }

        if (page.SelectedImageStream is not null)
            await _apiClient.UploadItemImageAsync(itemId.Value, page.SelectedImageStream, page.SelectedImageFileName ?? "photo.jpg");

        await LoadAsync();
    }

    private async Task EditAsync(ItemDto item)
    {
        var previewUrl = item.ImageUrl is not null ? _apiClient.ResolveImageUrl(item.ImageUrl) : null;
        var page = new ItemEditPage("Edit Item", CategoryOptionsFor(item.CategoryId), item.ItemName, item.CategoryId, item.Price, previewUrl, item.Description);
        await Navigation.PushModalAsync(page);
        if (!await page.Completion || !page.IsValid) return;

        var (success, error) = await _apiClient.UpdateItemAsync(item.ItemId, page.ItemName, page.CategoryId!.Value, page.Price, page.Description);
        if (!success)
        {
            await UiAlerts.Error(this, error ?? "Could not update the item.");
            return;
        }

        if (page.SelectedImageStream is not null)
            await _apiClient.UploadItemImageAsync(item.ItemId, page.SelectedImageStream, page.SelectedImageFileName ?? "photo.jpg");
        else if (page.RemoveImageRequested)
            await _apiClient.RemoveItemImageAsync(item.ItemId);

        await LoadAsync();
    }

    private async Task ShowHistoryAsync(ItemDto item)
    {
        var history = await _apiClient.GetItemPriceHistoryAsync(item.ItemId);
        await Navigation.PushAsync(new ItemPriceHistoryPage(item.ItemName, history));
    }

    private async Task ToggleActiveAsync(ItemDto item)
    {
        if (item.IsActive)
        {
            var confirmed = await UiAlerts.Confirm(this, "Confirm",
                $"Deactivate '{item.ItemName}'? It will be hidden from the menu but its history is kept.");
            if (!confirmed) return;

            var (success, error) = await _apiClient.DeactivateItemAsync(item.ItemId);
            if (!success) await UiAlerts.Error(this, error ?? "Could not deactivate the item.");
        }
        else
        {
            var (success, error) = await _apiClient.ReactivateItemAsync(item.ItemId);
            if (!success) await UiAlerts.Error(this, error ?? "Could not reactivate the item.");
        }

        await LoadAsync();
    }

    private async Task ToggleAvailabilityAsync(ItemDto item)
    {
        var (success, error) = await _apiClient.SetItemAvailabilityAsync(item.ItemId, !item.IsAvailable);
        if (!success) await UiAlerts.Error(this, error ?? "Could not update availability.");

        await LoadAsync();
    }

    // New/changed category assignments can only point at active categories -
    // a deactivated category is a dead end (hidden from New Order). When
    // editing an item whose current category has since been deactivated,
    // that one category is still included so the dialog doesn't strand the
    // item with no valid selection.
    private List<CategoryDto> CategoryOptionsFor(int? currentCategoryId = null)
    {
        var options = _categories.Where(c => c.IsActive).ToList();
        if (currentCategoryId is not null && options.All(c => c.CategoryId != currentCategoryId))
        {
            var current = _categories.FirstOrDefault(c => c.CategoryId == currentCategoryId);
            if (current is not null) options.Add(current);
        }
        return options;
    }

    private record CategoryFilterOption(string Name, int? CategoryId);
}
