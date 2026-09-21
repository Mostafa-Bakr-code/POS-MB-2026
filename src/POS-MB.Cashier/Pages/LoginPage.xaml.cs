using System.Globalization;
using POS_MB.Cashier.Api;
using POS_MB.Cashier.Session;

namespace POS_MB.Cashier.Pages;

public partial class LoginPage : ContentPage
{
    private readonly ApiClient _apiClient = new();

    public LoginPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        UserNameEntry.Focus();
    }

    private async void OnLoginClicked(object? sender, EventArgs e)
    {
        ErrorLabel.IsVisible = false;

        if (string.IsNullOrWhiteSpace(UserNameEntry.Text) || string.IsNullOrWhiteSpace(PasswordEntry.Text))
        {
            ShowError("Enter a username and password.");
            return;
        }

        LoginButton.IsEnabled = false;
        LoadingIndicator.IsVisible = true;
        LoadingIndicator.IsRunning = true;
        try
        {
            var (login, error) = await _apiClient.VerifyCredentialsAsync(UserNameEntry.Text.Trim(), PasswordEntry.Text);
            if (login is null)
            {
                ShowError(error ?? "Invalid username or password.");
                return;
            }

            AppSession.Token = login.Token;
            AppSession.RefreshToken = login.RefreshToken;

            var logId = await _apiClient.StartSessionAsync();

            AppSession.CurrentUser = login.User;
            AppSession.LogId = logId;

            var offsetValue = await _apiClient.GetSettingValueAsync("TimeZoneOffsetHours");
            AppSession.TimeZoneOffsetHours = offsetValue is not null &&
                decimal.TryParse(offsetValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var offset)
                ? offset
                : 0m;

            await Navigation.PushAsync(new MainShellPage());
            PasswordEntry.Text = "";
        }
        finally
        {
            LoginButton.IsEnabled = true;
            LoadingIndicator.IsVisible = false;
            LoadingIndicator.IsRunning = false;
        }
    }

    private void ShowError(string text)
    {
        ErrorLabel.Text = text;
        ErrorLabel.IsVisible = true;
    }
}
