namespace POS_MB.Cashier.Pages;

// Replaces FormTextInputDialog - small reusable modal for "enter a name"
// style edits (used by OrderTaking's per-cart-line comment field).
public partial class TextInputPage : ContentPage
{
    private readonly TaskCompletionSource<bool> _completion = new();

    public string Value => ValueEntry.Text?.Trim() ?? "";

    public Task<bool> Completion => _completion.Task;

    // maxLength should match the target database column's size - without
    // it, nothing stops the user from typing more than the database can
    // store.
    public TextInputPage(string title, string label, string initialValue = "", int maxLength = 100)
    {
        InitializeComponent();
        Title = title;
        PromptLabel.Text = label;
        ValueEntry.Text = initialValue;
        ValueEntry.MaxLength = maxLength;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ValueEntry.Focus();
    }

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        _completion.TrySetResult(true);
        await Navigation.PopModalAsync();
    }

    private async void OnCancelClicked(object? sender, EventArgs e)
    {
        _completion.TrySetResult(false);
        await Navigation.PopModalAsync();
    }
}
