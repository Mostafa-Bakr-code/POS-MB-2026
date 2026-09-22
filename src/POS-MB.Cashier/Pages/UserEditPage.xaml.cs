using POS_MB.Cashier.Controls;
using POS_MB.Cashier.Models;

namespace POS_MB.Cashier.Pages;

// Replaces FormUserEditDialog - same modal-page/Completion pattern as
// CategoryEditPage. Permission checkboxes replace WinForms' CheckBoxes
// with plain MAUI CheckBoxes in a 2-column grid, same layout shape.
public partial class UserEditPage : ContentPage
{
    private readonly bool _isEdit;
    private readonly TaskCompletionSource<bool> _completion = new();

    private readonly CheckBox[] _individualChecks;

    public string UserNameValue => UserNameEntry.Text?.Trim() ?? "";
    public string? PasswordValue => string.IsNullOrWhiteSpace(PasswordEntry.Text) ? null : PasswordEntry.Text;

    public int Permissions
    {
        get
        {
            if (FullAccessCheck.IsChecked) return (int)Permission.FullAccess;

            var permission = Permission.None;
            if (CategoriesCheck.IsChecked) permission |= Permission.Categories;
            if (ItemsCheck.IsChecked) permission |= Permission.Items;
            if (OrdersCheck.IsChecked) permission |= Permission.Orders;
            if (UsersCheck.IsChecked) permission |= Permission.Users;
            if (ReportsCheck.IsChecked) permission |= Permission.Reports;
            if (OrderHistoryCheck.IsChecked) permission |= Permission.OrderHistory;
            if (DailySummaryCheck.IsChecked) permission |= Permission.DailySummary;
            if (SettingsCheck.IsChecked) permission |= Permission.Settings;
            if (LogsCheck.IsChecked) permission |= Permission.Logs;
            if (ComplimentaryCheck.IsChecked) permission |= Permission.Complimentary;
            return (int)permission;
        }
    }

    public bool IsValid => UserNameValue.Length > 0 && (_isEdit || !string.IsNullOrWhiteSpace(PasswordEntry.Text));

    public Task<bool> Completion => _completion.Task;

    public UserEditPage(string title, bool isEdit, string initialUserName = "", int initialPermissions = 0)
    {
        InitializeComponent();
        Title = title;
        _isEdit = isEdit;

        UserNameEntry.Text = initialUserName;
        PasswordLabel.Text = isEdit ? "Password (leave blank to keep current)" : "Password";

        var existing = (Permission)initialPermissions;
        FullAccessCheck.IsChecked = existing == Permission.FullAccess;
        CategoriesCheck.IsChecked = existing.HasFlag(Permission.Categories);
        ItemsCheck.IsChecked = existing.HasFlag(Permission.Items);
        OrdersCheck.IsChecked = existing.HasFlag(Permission.Orders);
        UsersCheck.IsChecked = existing.HasFlag(Permission.Users);
        ReportsCheck.IsChecked = existing.HasFlag(Permission.Reports);
        OrderHistoryCheck.IsChecked = existing.HasFlag(Permission.OrderHistory);
        DailySummaryCheck.IsChecked = existing.HasFlag(Permission.DailySummary);
        SettingsCheck.IsChecked = existing.HasFlag(Permission.Settings);
        LogsCheck.IsChecked = existing.HasFlag(Permission.Logs);
        ComplimentaryCheck.IsChecked = existing.HasFlag(Permission.Complimentary);

        _individualChecks =
        [
            CategoriesCheck, ItemsCheck, OrdersCheck, UsersCheck, ReportsCheck,
            OrderHistoryCheck, DailySummaryCheck, SettingsCheck, LogsCheck, ComplimentaryCheck
        ];
        foreach (var check in _individualChecks) check.IsEnabled = !FullAccessCheck.IsChecked;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        UserNameEntry.Focus();
    }

    private void OnFullAccessChanged(object? sender, CheckedChangedEventArgs e)
    {
        foreach (var check in _individualChecks) check.IsEnabled = !e.Value;
    }

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        if (!IsValid)
        {
            await UiAlerts.Error(this, _isEdit
                ? "Enter a username."
                : "Enter a username and password.");
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
