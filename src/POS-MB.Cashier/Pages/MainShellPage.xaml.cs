using POS_MB.Cashier.Api;
using POS_MB.Cashier.Models;
using POS_MB.Cashier.Session;

namespace POS_MB.Cashier.Pages;

// Replaces FormMain's button-bar + content-swap area. MAUI's idiom is
// closer to "push a page per screen" than "swap a control into a hosting
// panel" - each nav button here pushes a full Page onto the nav stack
// (Navigation.PushAsync) rather than reimplementing WinForms' Panel-hosting
// pattern. Stays alive for the whole session (user navigates to sub-pages
// and back to this one), so the heartbeat/token-refresh timers are started
// once here and disposed on logout, same lifetime as FormMain's own timers.
public partial class MainShellPage : ContentPage
{
    // Same interval as FormMain's own heartbeat timer - a mobile order's
    // "is the shop watching" check needs a signal that survives navigation
    // between screens, not one tied to a specific screen being active.
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);

    private readonly ApiClient _apiClient = new();
    private TokenRefreshTimer? _refreshTimer;
    private System.Threading.Timer? _heartbeatTimer;
    private bool _timersStarted;
    private bool _loggedOut;

    private Button? _newOrderButton;
    private Button? _orderStatusButton;

    public MainShellPage()
    {
        InitializeComponent();

        ActiveUserLabel.Text = AppSession.CurrentUser?.UserName ?? "";

        _newOrderButton = AddNavButton("New Order", Permission.Orders, () => new OrderTakingPage());
        _orderStatusButton = AddNavButton("Order Status", Permission.Orders, () => new StubPage("Order Status"));
        AddNavButton("Categories", Permission.Categories, () => new CategoriesPage());
        AddNavButton("Items", Permission.Items, () => new ItemsPage());
        AddNavButton("Users", Permission.Users, () => new UsersPage());
        AddNavButton("Order History", Permission.OrderHistory, () => new OrderHistoryPage());
        AddNavButton("Daily Summary", Permission.DailySummary, () => new StubPage("Daily Summary"));
        AddNavButton("Reports", Permission.Reports, () => new StubPage("Reports"));
        AddNavButton("Settings", Permission.Settings, () => new StubPage("Settings"));
        AddNavButton("Logs", Permission.Logs, () => new LogsPage());
    }

    private Button AddNavButton(string text, Permission requiredPermission, Func<Page> createPage)
    {
        var button = new Button
        {
            Text = text,
            BackgroundColor = Color.FromArgb("#343A40"),
            TextColor = Colors.White,
            FontAttributes = FontAttributes.Bold,
            Margin = new Thickness(4),
            IsEnabled = AppSession.HasPermission(requiredPermission)
        };
        button.Clicked += async (_, _) => await Navigation.PushAsync(createPage());
        NavButtonsLayout.Children.Add(button);
        return button;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (_timersStarted) return;
        _timersStarted = true;

        _refreshTimer = new TokenRefreshTimer(RefreshTokenAsync, TimeSpan.FromMinutes(20));

        // Only staff who can actually act on orders represent "the shop is
        // watching" - same reasoning as FormMain's own gate on this timer.
        if (AppSession.HasPermission(Permission.Orders))
        {
            _heartbeatTimer = new System.Threading.Timer(
                async _ => await _apiClient.SendHeartbeatAsync(),
                null, HeartbeatInterval, HeartbeatInterval);
            _ = _apiClient.SendHeartbeatAsync(); // immediately at login, not just on the first tick
        }
    }

    private async Task RefreshTokenAsync()
    {
        if (AppSession.RefreshToken is not { } refreshToken) return;

        var result = await _apiClient.RefreshTokenAsync(refreshToken);
        if (result is null) return;

        // If logout happened while the HTTP call above was in flight,
        // _loggedOut is already true by the time we get here - writing the
        // newly-rotated tokens back now would resurrect a session
        // AppSession.Clear() just tore down. Same race guard as FormMain.
        if (_loggedOut) return;

        AppSession.Token = result.Token;
        AppSession.RefreshToken = result.RefreshToken;
    }

    private async void OnLogoutClicked(object? sender, EventArgs e)
    {
        var confirmed = await DisplayAlert("Log Out", "Are you sure you want to log out?", "Yes", "No");
        if (!confirmed) return;

        await LogoutAsync();
    }

    private async Task LogoutAsync()
    {
        if (_loggedOut) return;
        _loggedOut = true;

        _refreshTimer?.Dispose();
        _heartbeatTimer?.Dispose();

        if (AppSession.LogId is int logId)
            await _apiClient.EndSessionAsync(logId);

        if (AppSession.RefreshToken is string refreshToken)
            await _apiClient.LogoutAsync(refreshToken);

        AppSession.Clear();

        await Navigation.PopToRootAsync();
    }
}
