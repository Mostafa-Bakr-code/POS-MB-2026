using POS_MB.Mobile.Api;
using POS_MB.Mobile.Models;
using POS_MB.Mobile.Session;

namespace POS_MB.Mobile;

public partial class ProfilePage : ContentPage
{
    private readonly ApiClient _apiClient = new();

    public ProfilePage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        EmailLabel.Text = AppSession.CurrentStudent?.Email;
        Refresh();
    }

    private void Refresh()
    {
        var maskedPan = AppSession.CurrentStudent?.SavedCardMaskedPan;
        CardLayout.IsVisible = maskedPan is not null;
        NoCardLabel.IsVisible = maskedPan is null;

        if (maskedPan is not null)
        {
            var subtype = AppSession.CurrentStudent?.SavedCardSubtype;
            CardLabel.Text = subtype is null ? maskedPan : $"{subtype} {maskedPan}";
        }
    }

    private async void OnRemoveCardClicked(object? sender, EventArgs e)
    {
        var confirmed = await DisplayAlert("Remove Card", "Remove your saved card? You'll need to enter card details again next time.", "Remove", "Cancel");
        if (!confirmed) return;

        ErrorLabel.IsVisible = false;
        RemoveCardButton.IsEnabled = false;
        LoadingIndicator.IsRunning = true;
        LoadingIndicator.IsVisible = true;

        var (success, error) = await _apiClient.RemoveSavedCardAsync();

        LoadingIndicator.IsRunning = false;
        LoadingIndicator.IsVisible = false;
        RemoveCardButton.IsEnabled = true;

        if (!success)
        {
            ErrorLabel.Text = error ?? "Something went wrong.";
            ErrorLabel.IsVisible = true;
            return;
        }

        // Updates the in-memory session immediately so CartPage's "pay with
        // saved card" option disappears right away too, without needing a
        // fresh login - the server-side removal already happened, this just
        // keeps the client's cached copy from lying about it in the meantime.
        var student = AppSession.CurrentStudent;
        if (student is not null)
            AppSession.CurrentStudent = student with { SavedCardMaskedPan = null, SavedCardSubtype = null };

        Refresh();
    }

    private async void OnChangePasswordClicked(object? sender, EventArgs e)
    {
        PasswordErrorLabel.IsVisible = false;
        var currentPassword = CurrentPasswordEntry.Text ?? "";
        var newPassword = NewPasswordEntry.Text ?? "";
        var confirmPassword = ConfirmPasswordEntry.Text ?? "";

        if (string.IsNullOrWhiteSpace(currentPassword) || string.IsNullOrWhiteSpace(newPassword))
        {
            ShowPasswordError("Enter your current password and a new one.");
            return;
        }

        if (newPassword != confirmPassword)
        {
            ShowPasswordError("New passwords don't match.");
            return;
        }

        ChangePasswordButton.IsEnabled = false;
        PasswordLoadingIndicator.IsRunning = true;
        PasswordLoadingIndicator.IsVisible = true;

        var (success, error) = await _apiClient.ChangePasswordAsync(currentPassword, newPassword);

        PasswordLoadingIndicator.IsRunning = false;
        PasswordLoadingIndicator.IsVisible = false;
        ChangePasswordButton.IsEnabled = true;

        if (!success)
        {
            ShowPasswordError(error ?? "Something went wrong.");
            return;
        }

        // A successful change revokes every session, including this one
        // (see clsStudentBusiness.ChangePasswordAsync) - the app must not
        // keep acting as if it's still logged in, so this signs the
        // student out locally too, same as the normal logout button.
        await DisplayAlert("Password Changed", "Your password has been changed. Please log in again.", "OK");

        if (AppSession.RefreshToken is not null)
            await _apiClient.LogoutAsync(AppSession.RefreshToken);

        AppSession.Clear();
        AppSession.ClearPersisted();
        Cart.Clear();

        await Navigation.PopToRootAsync();
    }

    private void ShowPasswordError(string message)
    {
        PasswordErrorLabel.Text = message;
        PasswordErrorLabel.IsVisible = true;
    }

    private async void OnHomeClicked(object? sender, EventArgs e) =>
        await NavigationHelper.GoHomeAsync(Navigation);

    // Hosted as plain static pages on the API (wwwroot/policies), same
    // pattern as the chef tablet page - a single live source of truth
    // means editing the policy text just needs republishing the API, never
    // an app store update. Opened in the system browser rather than an
    // in-app WebView - simpler, works identically on Android and iOS, and
    // these are read-once legal pages, not something worth a custom screen.
    private async void OnPrivacyPolicyClicked(object? sender, EventArgs e) =>
        await Browser.OpenAsync($"{ApiConfig.BaseUrl}policies/privacy.html");

    private async void OnRefundPolicyClicked(object? sender, EventArgs e) =>
        await Browser.OpenAsync($"{ApiConfig.BaseUrl}policies/refund-exchange.html");

    private async void OnShippingPolicyClicked(object? sender, EventArgs e) =>
        await Browser.OpenAsync($"{ApiConfig.BaseUrl}policies/shipping.html");
}
