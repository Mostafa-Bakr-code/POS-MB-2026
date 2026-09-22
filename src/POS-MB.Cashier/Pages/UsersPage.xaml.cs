using POS_MB.Cashier.Api;
using POS_MB.Cashier.Controls;
using POS_MB.Cashier.Models;

namespace POS_MB.Cashier.Pages;

// Replaces UsersControl.
public partial class UsersPage : ContentPage
{
    private readonly ApiClient _apiClient = new();
    private List<UserDto> _users = [];

    public UsersPage()
    {
        InitializeComponent();

        UsersGrid.SetColumns(
            columns:
            [
                new GridColumn("Id", u => ((UserDto)u).UserId.ToString(), weight: 0.4),
                new GridColumn("Username", u => ((UserDto)u).UserName, weight: 1.5),
                new GridColumn("Permissions", u => PermissionsText(((UserDto)u).Permissions), weight: 1.8),
                new GridColumn("Active", u => ((UserDto)u).IsActive ? "Yes" : "No", weight: 0.5)
            ],
            rowActions:
            [
                new GridRowAction("Edit", async row => await EditAsync((UserDto)row)),
                new GridRowAction(
                    row => ((UserDto)row).IsActive ? "Deactivate" : "Reactivate",
                    async row => await ToggleActiveAsync((UserDto)row))
            ]);
    }

    private static string PermissionsText(int permissions)
    {
        var permission = (Permission)permissions;
        return permission == Permission.FullAccess ? "Full Access" : permission.ToString();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _users = await _apiClient.GetUsersAsync(ShowInactiveSwitch.IsToggled);
        UsersGrid.ItemsSource = _users.Cast<object>();
    }

    private async void OnShowInactiveToggled(object? sender, ToggledEventArgs e) => await LoadAsync();

    private async void OnRowTapped(object? sender, object row) => await EditAsync((UserDto)row);

    private async void OnAddClicked(object? sender, EventArgs e)
    {
        var page = new UserEditPage("Add User", isEdit: false);
        await Navigation.PushModalAsync(page);
        if (!await page.Completion || !page.IsValid) return;

        var (success, error) = await _apiClient.CreateUserAsync(page.UserNameValue, page.PasswordValue!, page.Permissions);
        if (!success) await UiAlerts.Error(this, error ?? "Could not create the user.");

        await LoadAsync();
    }

    private async Task EditAsync(UserDto user)
    {
        var page = new UserEditPage("Edit User", isEdit: true, user.UserName, user.Permissions);
        await Navigation.PushModalAsync(page);
        if (!await page.Completion || !page.IsValid) return;

        var (success, error) = await _apiClient.UpdateUserAsync(user.UserId, page.UserNameValue, page.PasswordValue, page.Permissions);
        if (!success) await UiAlerts.Error(this, error ?? "Could not update the user.");

        await LoadAsync();
    }

    private async Task ToggleActiveAsync(UserDto user)
    {
        if (user.IsActive)
        {
            var confirmed = await UiAlerts.Confirm(this, "Confirm",
                $"Deactivate '{user.UserName}'? They will no longer be able to log in.");
            if (!confirmed) return;

            var (success, error) = await _apiClient.DeactivateUserAsync(user.UserId);
            if (!success) await UiAlerts.Error(this, error ?? "Could not deactivate the user.");
        }
        else
        {
            var (success, error) = await _apiClient.ReactivateUserAsync(user.UserId);
            if (!success) await UiAlerts.Error(this, error ?? "Could not reactivate the user.");
        }

        await LoadAsync();
    }
}
