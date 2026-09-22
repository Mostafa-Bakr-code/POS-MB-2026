using POS_MB.Cashier.Controls;
using POS_MB.Cashier.Models;

namespace POS_MB.Cashier.Pages;

// Replaces FormItemEditDialog - same modal-page/Completion pattern as
// CategoryEditPage (see its own comment for why).
public partial class ItemEditPage : ContentPage
{
    private readonly List<CategoryDto> _categories;
    private readonly TaskCompletionSource<bool> _completion = new();

    public string ItemName => NameEntry.Text?.Trim() ?? "";
    public int? CategoryId => CategoryPicker.SelectedItem is CategoryDto category ? category.CategoryId : null;
    public decimal Price => PriceStepper.Value;

    // Empty input maps to null, not "" - a blank description and "never
    // set" should mean the same thing everywhere this is read.
    public string? Description => DescriptionEditor.Text?.Trim() is { Length: > 0 } trimmed ? trimmed : null;

    public Stream? SelectedImageStream { get; private set; }
    public string? SelectedImageFileName { get; private set; }
    public bool RemoveImageRequested { get; private set; }

    public bool IsValid => ItemName.Length > 0 && CategoryId is not null;

    public Task<bool> Completion => _completion.Task;

    public ItemEditPage(
        string title,
        List<CategoryDto> categories,
        string initialName = "",
        int? initialCategoryId = null,
        decimal initialPrice = 0,
        string? initialImagePreviewUrl = null,
        string? initialDescription = null)
    {
        InitializeComponent();
        Title = title;
        _categories = categories;

        NameEntry.Text = initialName;
        CategoryPicker.ItemsSource = categories;
        CategoryPicker.SelectedItem = initialCategoryId is not null
            ? categories.FirstOrDefault(c => c.CategoryId == initialCategoryId)
            : categories.FirstOrDefault();
        PriceStepper.Value = initialPrice;
        DescriptionEditor.Text = initialDescription ?? "";

        RemoveImageButton.IsEnabled = initialImagePreviewUrl is not null;
        if (initialImagePreviewUrl is not null)
            PreviewImage.Source = ImageSource.FromUri(new Uri(initialImagePreviewUrl));
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        NameEntry.Focus();
    }

    private async void OnChooseImageClicked(object? sender, EventArgs e)
    {
        FileResult? result;
        try
        {
            result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Choose Image",
                FileTypes = FilePickerFileType.Images
            });
        }
        catch (Exception)
        {
            return;
        }

        if (result is null) return;

        using var sourceStream = await result.OpenReadAsync();
        var buffer = new MemoryStream();
        await sourceStream.CopyToAsync(buffer);
        buffer.Position = 0;

        SelectedImageStream = buffer;
        SelectedImageFileName = result.FileName;
        RemoveImageRequested = false;
        PreviewImage.Source = ImageSource.FromStream(() => new MemoryStream(buffer.ToArray()));
        RemoveImageButton.IsEnabled = true;
    }

    private void OnRemoveImageClicked(object? sender, EventArgs e)
    {
        SelectedImageStream = null;
        SelectedImageFileName = null;
        RemoveImageRequested = true;
        PreviewImage.Source = null;
        RemoveImageButton.IsEnabled = false;
    }

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        if (!IsValid)
        {
            await UiAlerts.Error(this, _categories.Count == 0
                ? "Add an active category first."
                : "Enter an item name and choose a category.");
            return;
        }

        _completion.TrySetResult(true);
        await Navigation.PopModalAsync();
    }

    private async void OnCancelClicked(object? sender, EventArgs e)
    {
        _completion.TrySetResult(false);
        await Navigation.PopModalAsync();
    }
}
