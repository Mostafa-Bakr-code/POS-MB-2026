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
}
