using POS_MB.Mobile.Api;

namespace POS_MB.Mobile;

public partial class ForgotPasswordPage : ContentPage
{
    private readonly ApiClient _apiClient = new();

    public ForgotPasswordPage(string? prefillEmail = null)
    {
        InitializeComponent();
        EmailEntry.Text = prefillEmail;
    }

    private async void OnSendCodeClicked(object? sender, EventArgs e)
    {
        ErrorLabel.IsVisible = false;
        var email = EmailEntry.Text?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(email))
        {
            ShowError("Enter your email.");
            return;
        }

        SetBusy(true);
        var (success, error) = await _apiClient.ForgotPasswordAsync(email);
        SetBusy(false);

        if (!success)
        {
            ShowError(error ?? "Something went wrong.");
            return;
        }

        // The server always returns success here regardless of whether the
        // email actually matched an account - never reveal that from the
        // client either, same reasoning as clsStudentBusiness.RequestPasswordResetAsync.
        await DisplayAlert("Check Your Email",
            "If that email is registered, a reset code is on its way. It expires in 15 minutes.", "OK");
        await Navigation.PushAsync(new ResetPasswordPage(email));
    }

    private void SetBusy(bool busy)
    {
        LoadingIndicator.IsRunning = busy;
        LoadingIndicator.IsVisible = busy;
        SendCodeButton.IsEnabled = !busy;
    }

    private void ShowError(string message)
    {
        ErrorLabel.Text = message;
        ErrorLabel.IsVisible = true;
    }
}
