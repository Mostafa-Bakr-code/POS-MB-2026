namespace POS_MB.Cashier.Pages;

// Replaces FormReceiptPreviewDialog - shows what a receipt would look like
// on paper, from the exact same ReceiptBuilder.Preview* content a real
// printer would get. Not pixel-perfect (font/alignment are an
// approximation, same caveat as the WinForms original), just enough to
// check the content/order without needing physical printer hardware.
public partial class ReceiptPreviewPage : ContentPage
{
    public ReceiptPreviewPage(string title, string previewText)
    {
        InitializeComponent();
        Title = title;
        PreviewLabel.Text = previewText;
    }

    private async void OnCloseClicked(object? sender, EventArgs e) => await Navigation.PopModalAsync();
}
