using System.Globalization;
using POS_MB.Mobile.Api;
using POS_MB.Mobile.Session;

namespace POS_MB.Mobile;

public partial class LoginPage : ContentPage
{
    private readonly ApiClient _apiClient = new();
    private bool _isSignUpMode;
    private bool _autoLoginAttempted;

    public LoginPage()
    {
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // Only attempted once per app launch - after a logout (which clears the
        // persisted token before popping back here) this will simply find
        // nothing saved and fall through to the normal login form.
        if (_autoLoginAttempted) return;
        _autoLoginAttempted = true;

        var savedRefreshToken = await AppSession.GetPersistedRefreshTokenAsync();
        if (savedRefreshToken is null) return;

        SetBusy(true, "Signing you in...");
        try
        {
            var result = await _apiClient.RefreshTokenAsync(savedRefreshToken);
            if (result is null)
            {
                // Saved token is gone/expired/revoked server-side - clear it so
                // this doesn't keep failing silently on every future launch.
                AppSession.ClearPersisted();
                return;
            }

            await ApplySessionAsync(result.Token, result.RefreshToken, result.Student);
            await Navigation.PushAsync(new MenuPage());
        }
        finally
        {
            SetBusy(false, "");
        }
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

        SetBusy(true, "");
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

            await ApplySessionAsync(result.Token, result.RefreshToken, result.Student);
            await Navigation.PushAsync(new MenuPage());
        }
        finally
        {
            SetBusy(false, "");
        }
    }

    private async Task ApplySessionAsync(string token, string refreshToken, Models.StudentDto student)
    {
        AppSession.Token = token;
        AppSession.RefreshToken = refreshToken;
        AppSession.CurrentStudent = student;
        await AppSession.PersistRefreshTokenAsync();

        // Same source of truth WinForms reads at login - without this, order
        // times would show the server's raw UTC clock instead of local time.
        var offsetValue = await _apiClient.GetSettingValueAsync("TimeZoneOffsetHours");
        AppSession.TimeZoneOffsetHours = offsetValue is not null
            && decimal.TryParse(offsetValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var offset)
                ? offset
                : 0m;
    }

    private void SetBusy(bool busy, string statusText)
    {
        LoadingIndicator.IsRunning = busy;
        LoadingIndicator.IsVisible = busy;
        PrimaryButton.IsEnabled = !busy;
        ToggleModeButton.IsEnabled = !busy;

        // Auto-login runs before the form is even relevant - hide it entirely
        // while that's in flight so the student isn't shown an empty login
        // form for a split second before being whisked away to the menu.
        FormLayout.IsVisible = !busy || string.IsNullOrEmpty(statusText);
        StatusLabel.Text = statusText;
        StatusLabel.IsVisible = busy && !string.IsNullOrEmpty(statusText);
    }

    private void ShowError(string message)
    {
        ErrorLabel.Text = message;
        ErrorLabel.IsVisible = true;
    }
}
