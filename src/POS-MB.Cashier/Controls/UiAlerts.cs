namespace POS_MB.Cashier.Controls;

// One obvious, consistent replacement for every WinForms MessageBox.Show
// call site being ported - callers pass their own page (any Page, since
// DisplayAlert is a Page/VisualElement method), so this stays a plain
// static helper rather than needing DI or a page-base-class dependency.
public static class UiAlerts
{
    public static Task<bool> Confirm(Page page, string title, string message) =>
        page.DisplayAlert(title, message, "Yes", "No");

    public static Task Info(Page page, string title, string message) =>
        page.DisplayAlert(title, message, "OK");

    public static Task Error(Page page, string message) =>
        page.DisplayAlert("Error", message, "OK");
}
