using POS_MB.Mobile.Api;

namespace POS_MB.Mobile;

public partial class ResetPasswordPage : ContentPage
{
    private readonly ApiClient _apiClient = new();
    private readonly string _email;

    public ResetPasswordPage(string email)
    {
        InitializeComponent();
        _email = email;
        InstructionsLabel.Text = $"Enter the code sent to {email} and choose a new password.";
    }

    private async void OnResetClicked(object? sender, EventArgs e)
    {
        ErrorLabel.IsVisible = false;
        var code = CodeEntry.Text?.Trim() ?? "";
        var newPassword = NewPasswordEntry.Text ?? "";
        var confirmPassword = ConfirmPasswordEntry.Text ?? "";

        if (code.Length != 6 || !code.All(char.IsDigit))
        {
            ShowError("Enter the 6-digit code from your email.");
            return;
        }

        if (string.IsNullOrWhiteSpace(newPassword))
        {
            ShowError("Enter a new password.");
            return;
        }

        if (newPassword != confirmPassword)
        {
            ShowError("Passwords don't match.");
            return;
        }

        SetBusy(true);
        var (success, error) = await _apiClient.ResetPasswordAsync(_email, code, newPassword);
        SetBusy(false);

        if (!success)
        {
            ShowError(error ?? "Something went wrong.");
            return;
        }

        await DisplayAlert("Password Reset", "Your password has been reset. Log in with your new password.", "OK");
        // Same reasoning as logout - lands back at the login form, not
        // still-logged-in anywhere, since a password reset also revokes
        // every existing session (see clsStudentBusiness.ResetPasswordAsync).
        await Navigation.PopToRootAsync();
    }

    private async void OnResendCodeClicked(object? sender, EventArgs e)
    {
        SetBusy(true);
        var (success, error) = await _apiClient.ForgotPasswordAsync(_email);
        SetBusy(false);

        await DisplayAlert("Check Your Email",
            success ? "If that email is registered, a new code is on its way." : (error ?? "Something went wrong."),
            "OK");
    }

    private void SetBusy(bool busy)
    {
        LoadingIndicator.IsRunning = busy;
        LoadingIndicator.IsVisible = busy;
        ResetButton.IsEnabled = !busy;
        ResendCodeButton.IsEnabled = !busy;
    }

    private void ShowError(string message)
    {
        ErrorLabel.Text = message;
        ErrorLabel.IsVisible = true;
    }
}
