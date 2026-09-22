using POS_MB.Cashier.Api;
using POS_MB.Cashier.Controls;
using POS_MB.Cashier.Models;

namespace POS_MB.Cashier.Pages;

// Replaces CategoriesControl. Sorting (click a column header to sort,
// present in the WinForms original) is deliberately not ported - a minor
// UX nicety, not a core CRUD feature, and DataGridView doesn't support
// header-tap sorting yet; can be added later if it's missed.
public partial class CategoriesPage : ContentPage
{
    private readonly ApiClient _apiClient = new();
    private List<CategoryDto> _categories = [];

    public CategoriesPage()
    {
        InitializeComponent();

        CategoriesGrid.SetColumns(
            columns:
            [
                new GridColumn("Id", c => ((CategoryDto)c).CategoryId.ToString(), weight: 0.5),
                new GridColumn("Name", c => ((CategoryDto)c).CategoryName, weight: 2),
                new GridColumn("Active", c => ((CategoryDto)c).IsActive ? "Yes" : "No", weight: 0.6)
            ],
            rowActions:
            [
                new GridRowAction("Edit", async row => await EditAsync((CategoryDto)row)),
                new GridRowAction(
                    row => ((CategoryDto)row).IsActive ? "Deactivate" : "Reactivate",
                    async row => await ToggleActiveAsync((CategoryDto)row))
            ]);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _categories = await _apiClient.GetCategoriesAsync(ShowInactiveSwitch.IsToggled);
        CategoriesGrid.ItemsSource = _categories.Cast<object>();
    }

    private async void OnShowInactiveToggled(object? sender, ToggledEventArgs e) => await LoadAsync();

    private async void OnRowTapped(object? sender, object row) => await EditAsync((CategoryDto)row);

    private async void OnAddClicked(object? sender, EventArgs e)
    {
        var page = new CategoryEditPage("Add Category");
        await Navigation.PushModalAsync(page);
        if (!await page.Completion || !page.IsValid) return;

        var (categoryId, error) = await _apiClient.CreateCategoryAsync(page.CategoryName);
        if (categoryId is null)
        {
            await UiAlerts.Error(this, error ?? "Could not create the category.");
            return;
        }

        if (page.SelectedImageStream is not null)
            await _apiClient.UploadCategoryImageAsync(categoryId.Value, page.SelectedImageStream, page.SelectedImageFileName ?? "photo.jpg");

        await LoadAsync();
    }

    private async Task EditAsync(CategoryDto category)
    {
        var previewUrl = category.ImageUrl is not null ? _apiClient.ResolveImageUrl(category.ImageUrl) : null;
        var page = new CategoryEditPage("Edit Category", category.CategoryName, previewUrl);
        await Navigation.PushModalAsync(page);
        if (!await page.Completion || !page.IsValid) return;

        var (success, error) = await _apiClient.UpdateCategoryAsync(category.CategoryId, page.CategoryName);
        if (!success)
        {
            await UiAlerts.Error(this, error ?? "Could not update the category.");
            return;
        }

        if (page.SelectedImageStream is not null)
            await _apiClient.UploadCategoryImageAsync(category.CategoryId, page.SelectedImageStream, page.SelectedImageFileName ?? "photo.jpg");
        else if (page.RemoveImageRequested)
            await _apiClient.RemoveCategoryImageAsync(category.CategoryId);

        await LoadAsync();
    }

    private async Task ToggleActiveAsync(CategoryDto category)
    {
        if (category.IsActive)
        {
            var confirmed = await UiAlerts.Confirm(this, "Confirm",
                $"Deactivate '{category.CategoryName}'? It will be hidden from the menu but its history is kept.");
            if (!confirmed) return;

            var (success, error) = await _apiClient.DeactivateCategoryAsync(category.CategoryId);
            if (!success) await UiAlerts.Error(this, error ?? "Could not deactivate the category.");
        }
        else
        {
            var (success, error) = await _apiClient.ReactivateCategoryAsync(category.CategoryId);
            if (!success) await UiAlerts.Error(this, error ?? "Could not reactivate the category.");
        }

        await LoadAsync();
    }
}
