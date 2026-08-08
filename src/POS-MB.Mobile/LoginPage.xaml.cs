using System.Globalization;
using POS_MB.Mobile.Api;
using POS_MB.Mobile.Session;

namespace POS_MB.Mobile;

public partial class LoginPage : ContentPage
{
    private readonly ApiClient _apiClient = new();
    private bool _isSignUpMode;

    public LoginPage()
    {
        InitializeComponent();
    }

    private void OnToggleModeClicked(object? sender, EventArgs e)
    {
        _isSignUpMode = !_isSignUpMode;
        ModeLabel.Text = _isSignUpMode ? "Create a new account" : "Log in to your account";
        PrimaryButton.Text = _isSignUpMode ? "Sign Up" : "Log In";
        ToggleModeButton.Text = _isSignUpMode ? "Already have an account? Log in" : "Don't have an account? Sign up";
        ErrorLabel.IsVisible = false;
    }

    private async void OnPrimaryButtonClicked(object? sender, EventArgs e)
    {
        ErrorLabel.IsVisible = false;

        var email = EmailEntry.Text?.Trim() ?? "";
        var password = PasswordEntry.Text ?? "";

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            ShowError("Enter an email and password.");
            return;
        }

        SetBusy(true);
        try
        {
            var (result, error) = _isSignUpMode
                ? await _apiClient.SignUpAsync(email, password)
                : await _apiClient.LoginAsync(email, password);

            if (result is null)
            {
                ShowError(error ?? "Something went wrong.");
                return;
            }

            AppSession.Token = result.Token;
            AppSession.RefreshToken = result.RefreshToken;
            AppSession.CurrentStudent = result.Student;

            // Same source of truth WinForms reads at login - without this, order
            // times would show the server's raw UTC clock instead of local time.
            var offsetValue = await _apiClient.GetSettingValueAsync("TimeZoneOffsetHours");
            AppSession.TimeZoneOffsetHours = offsetValue is not null
                && decimal.TryParse(offsetValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var offset)
                    ? offset
                    : 0m;

            await Navigation.PushAsync(new MenuPage());
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        LoadingIndicator.IsRunning = busy;
        LoadingIndicator.IsVisible = busy;
        PrimaryButton.IsEnabled = !busy;
        ToggleModeButton.IsEnabled = !busy;
    }

    private void ShowError(string message)
    {
        ErrorLabel.Text = message;
        ErrorLabel.IsVisible = true;
    }
}
