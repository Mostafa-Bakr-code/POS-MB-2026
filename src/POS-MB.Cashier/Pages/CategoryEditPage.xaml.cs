using POS_MB.Cashier.Controls;

namespace POS_MB.Cashier.Pages;

// Replaces FormCategoryEditDialog - pushed as a modal page
// (Navigation.PushModalAsync) rather than a WinForms dialog. Callers await
// Completion instead of checking a DialogResult:
//
//   var page = new CategoryEditPage("Edit Category", category.CategoryName, previewUrl);
//   await Navigation.PushModalAsync(page);
//   if (await page.Completion) { /* saved - read page.CategoryName etc. */ }
public partial class CategoryEditPage : ContentPage
{
    private readonly bool _hadInitialImage;
    private readonly TaskCompletionSource<bool> _completion = new();

    public string CategoryName => NameEntry.Text?.Trim() ?? "";

    // Set only when the user picks a new photo this session - null means
    // "leave the existing photo (or lack of one) alone". Read fully into
    // memory at pick time (see OnChooseImageClicked) so it survives
    // regardless of how the platform's own file handle behaves afterward.
    public Stream? SelectedImageStream { get; private set; }
    public string? SelectedImageFileName { get; private set; }

    // True only when an existing photo was explicitly removed via the
    // button - distinct from SelectedImageStream being null (no change).
    public bool RemoveImageRequested { get; private set; }

    public bool IsValid => CategoryName.Length > 0;

    // Resolves to true if Save was pressed (and IsValid), false on Cancel.
    public Task<bool> Completion => _completion.Task;

    public CategoryEditPage(string title, string initialName = "", string? initialImagePreviewUrl = null)
    {
        InitializeComponent();
        Title = title;
        NameEntry.Text = initialName;

        _hadInitialImage = initialImagePreviewUrl is not null;
        RemoveImageButton.IsEnabled = _hadInitialImage;
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
            return; // picker cancelled/unavailable - leave the existing selection alone
        }

        if (result is null) return;

        // Read fully into memory now, while the platform's own file handle
        // (a content:// stream on Android) is definitely still valid -
        // outliving this handler isn't guaranteed otherwise.
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
            await UiAlerts.Error(this, "Enter a category name.");
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
