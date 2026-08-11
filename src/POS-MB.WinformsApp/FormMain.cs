using POS_MB.WinformsApp.Api;
using POS_MB.WinformsApp.Controls;
using POS_MB.WinformsApp.Models;
using POS_MB.WinformsApp.Session;

namespace POS_MB.WinformsApp;

public class FormMain : Form
{
    // Independent of the 20-minute token-refresh timer below, and of whichever
    // screen happens to be showing - a mobile order's "is the shop watching"
    // check (clsOrderBusiness.GetAcceptingOnlineOrdersStatusAsync) needs a
    // signal that survives normal navigation between screens, not one tied to
    // a specific control like Order Status being the active view.
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);

    // Same reasoning as the heartbeat above - a kitchen ticket needs to print
    // regardless of which screen (or client entirely - the chef tablet can
    // move an order to Preparing too) is currently active. See
    // KitchenTicketPrintService for why printing had to move out of
    // OrderStatusControl once a browser-only client could also accept orders.
    private static readonly TimeSpan KitchenTicketPollInterval = TimeSpan.FromSeconds(5);

    private readonly ApiClient _apiClient = new();
    private readonly KitchenTicketPrintService _kitchenTicketPrintService;
    private TokenRefreshTimer? _refreshTimer;
    private System.Windows.Forms.Timer? _heartbeatTimer;
    private System.Windows.Forms.Timer? _kitchenTicketTimer;
    private readonly Panel _navBar;
    private readonly Panel _contentArea;
    private readonly Label _lblActiveUser;
    private readonly Label _lblPrintStatus;
    private readonly Button _btnNewOrder;
    private readonly Button _btnOrderStatus;
    private readonly Button _btnCategories;
    private readonly Button _btnItems;
    private readonly Button _btnUsers;
    private readonly Button _btnOrderHistory;
    private readonly Button _btnDailySummary;
    private readonly Button _btnReports;
    private readonly Button _btnSettings;
    private readonly Button _btnLogs;
    private readonly Button _btnLogout;

    public FormMain()
    {
        Text = "POS-MB";
        WindowState = FormWindowState.Maximized;
        Font = new Font("Segoe UI", 12F);

        _kitchenTicketPrintService = new KitchenTicketPrintService(_apiClient);

        _navBar = new Panel
        {
            Dock = DockStyle.Top,
            Height = 170,
            BackColor = Color.FromArgb(33, 37, 41)
        };

        // Username + Log Out live together in their own column on the far right,
        // permanently separated from the nav button flow (and from New Order
        // specifically) so wrapping/resizing can never put Log Out next to a
        // frequently-tapped button by accident.
        var rightPanel = new Panel { Dock = DockStyle.Right, Width = 220 };

        _lblActiveUser = new Label
        {
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 12F, FontStyle.Bold),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Top,
            Height = 85
        };

        // Kitchen-ticket print status now shows here (not on OrderStatusControl)
        // since printing can fire while any screen - or the chef tablet
        // entirely - is what accepted the order, not just this one.
        _lblPrintStatus = new Label
        {
            ForeColor = Color.FromArgb(180, 190, 200),
            Font = new Font("Segoe UI", 8.5F),
            AutoSize = false,
            TextAlign = ContentAlignment.TopCenter,
            Dock = DockStyle.Top,
            Height = 40,
            Padding = new Padding(6, 2, 6, 0)
        };

        _kitchenTicketPrintService.StatusChanged += (text, success) =>
        {
            void Apply()
            {
                _lblPrintStatus.Text = text;
                _lblPrintStatus.ForeColor = success ? Color.FromArgb(40, 200, 130) : Color.FromArgb(255, 140, 140);
            }
            if (InvokeRequired) BeginInvoke(Apply); else Apply();
        };

        _btnNewOrder = CreateNavButton("New Order");
        _btnNewOrder.Click += (_, _) => ShowOrderTaking();

        _btnOrderStatus = CreateNavButton("Order Status");
        _btnOrderStatus.Click += (_, _) => ShowContent(new OrderStatusControl());

        _btnCategories = CreateNavButton("Categories");
        _btnCategories.Click += (_, _) => ShowContent(new CategoriesControl());

        _btnItems = CreateNavButton("Items");
        _btnItems.Click += (_, _) => ShowContent(new ItemsControl());

        _btnUsers = CreateNavButton("Users");
        _btnUsers.Click += (_, _) => ShowContent(new UsersControl());

        _btnOrderHistory = CreateNavButton("Order History");
        _btnOrderHistory.Click += (_, _) => ShowContent(new OrderHistoryControl());

        _btnDailySummary = CreateNavButton("Daily Summary");
        _btnDailySummary.Click += (_, _) => ShowContent(new DailySummaryControl());

        _btnReports = CreateNavButton("Reports");
        _btnReports.Click += (_, _) => ShowContent(new ReportsControl());

        _btnSettings = CreateNavButton("Settings");
        _btnSettings.Click += (_, _) => ShowContent(new SettingsControl());

        _btnLogs = CreateNavButton("Logs");
        _btnLogs.Click += (_, _) => ShowContent(new LogsControl());

        _btnLogout = new Button
        {
            Text = "Log Out",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 12F, FontStyle.Bold),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(176, 42, 55),
            ForeColor = Color.White
        };
        _btnLogout.Click += async (_, _) => await ConfirmAndLogoutAsync();

        var navButtonsPanel = new FlowLayoutPanel
        {
            // Fill (not Left+AutoSize) so this panel stops at the label's reserved
            // width instead of growing over it as more nav buttons get added.
            // WrapContents lets extra buttons flow onto a second row (navBar is
            // tall enough for two rows) instead of overlapping or scrolling.
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Padding = new Padding(10)
        };
        navButtonsPanel.Controls.Add(_btnNewOrder);
        navButtonsPanel.Controls.Add(_btnOrderStatus);
        navButtonsPanel.Controls.Add(_btnCategories);
        navButtonsPanel.Controls.Add(_btnItems);
        navButtonsPanel.Controls.Add(_btnUsers);
        navButtonsPanel.Controls.Add(_btnOrderHistory);
        navButtonsPanel.Controls.Add(_btnDailySummary);
        navButtonsPanel.Controls.Add(_btnReports);
        navButtonsPanel.Controls.Add(_btnSettings);
        navButtonsPanel.Controls.Add(_btnLogs);

        rightPanel.Controls.Add(_btnLogout);
        rightPanel.Controls.Add(_lblPrintStatus);
        rightPanel.Controls.Add(_lblActiveUser);

        _navBar.Controls.Add(navButtonsPanel);
        _navBar.Controls.Add(rightPanel);

        _contentArea = new Panel { Dock = DockStyle.Fill };

        Controls.Add(_contentArea);
        Controls.Add(_navBar);

        Load += FormMain_Load;
        FormClosing += FormMain_FormClosing;
    }

    // FormClosing is fire-and-forget by default with an async handler - the window (and
    // process) can close before the EndSessionAsync call actually completes, silently
    // dropping the logout. Cancel the close, wait for it to finish, then close for real.
    private async void FormMain_FormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_loggedOut) return;

        e.Cancel = true;
        await LogoutAsync(closingApp: true);
        Close();
    }

    private static Button CreateNavButton(string text) => new()
    {
        Text = text,
        Width = 160,
        Height = 70,
        Margin = new Padding(6),
        Font = new Font("Segoe UI", 12F, FontStyle.Bold),
        FlatStyle = FlatStyle.Flat,
        BackColor = Color.FromArgb(52, 58, 64),
        ForeColor = Color.White
    };

    private void FormMain_Load(object? sender, EventArgs e)
    {
        _lblActiveUser.Text = AppSession.CurrentUser?.UserName ?? "";

        _btnNewOrder.Enabled = AppSession.HasPermission(Permission.Orders);
        _btnOrderStatus.Enabled = AppSession.HasPermission(Permission.Orders);
        _btnCategories.Enabled = AppSession.HasPermission(Permission.Categories);
        _btnItems.Enabled = AppSession.HasPermission(Permission.Items);
        _btnUsers.Enabled = AppSession.HasPermission(Permission.Users);
        _btnOrderHistory.Enabled = AppSession.HasPermission(Permission.OrderHistory);
        _btnDailySummary.Enabled = AppSession.HasPermission(Permission.DailySummary);
        _btnReports.Enabled = AppSession.HasPermission(Permission.Reports);
        _btnSettings.Enabled = AppSession.HasPermission(Permission.Settings);
        _btnLogs.Enabled = AppSession.HasPermission(Permission.Logs);

        _refreshTimer = new TokenRefreshTimer(RefreshTokenAsync, TimeSpan.FromMinutes(20));

        // Only staff who can actually act on orders represent "the shop is
        // watching" - a logged-in account without Orders permission (e.g.
        // office-only reporting access) wouldn't be able to advance/print a
        // mobile order anyway, so its presence shouldn't count toward keeping
        // mobile ordering open.
        if (AppSession.HasPermission(Permission.Orders))
        {
            _heartbeatTimer = new System.Windows.Forms.Timer { Interval = (int)HeartbeatInterval.TotalMilliseconds };
            _heartbeatTimer.Tick += async (_, _) => await _apiClient.SendHeartbeatAsync();
            _heartbeatTimer.Start();
            _ = _apiClient.SendHeartbeatAsync(); // immediately at login, not just on the first tick

            _kitchenTicketTimer = new System.Windows.Forms.Timer { Interval = (int)KitchenTicketPollInterval.TotalMilliseconds };
            _kitchenTicketTimer.Tick += async (_, _) => await _kitchenTicketPrintService.PollOnceAsync();
            _kitchenTicketTimer.Start();
            _ = _kitchenTicketPrintService.PollOnceAsync();
        }

        ShowOrderTaking();
    }

    private async Task RefreshTokenAsync()
    {
        if (AppSession.RefreshToken is not { } refreshToken) return;

        var result = await _apiClient.RefreshTokenAsync(refreshToken);
        if (result is null) return;

        // If logout happened while the HTTP call above was in flight, _loggedOut
        // is already true by the time we get here (it's set synchronously as the
        // first line of LogoutAsync) - writing the newly-rotated tokens back now
        // would resurrect a session AppSession.Clear() just tore down, and leave
        // a live refresh token server-side that /logout never got a chance to
        // revoke (it revoked the OLD one, this is a NEW one from the race).
        if (_loggedOut) return;

        AppSession.Token = result.Token;
        AppSession.RefreshToken = result.RefreshToken;
    }

    private void ShowOrderTaking() => ShowContent(new OrderTakingControl());

    private void ShowContent(Control control)
    {
        control.Dock = DockStyle.Fill;
        _contentArea.Controls.Clear();
        _contentArea.Controls.Add(control);
    }

    private async Task ConfirmAndLogoutAsync()
    {
        var result = MessageBox.Show("Are you sure you want to log out?", "Log Out",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
        if (result != DialogResult.Yes) return;

        await LogoutAsync();
    }

    private bool _loggedOut;

    private async Task LogoutAsync(bool closingApp = false)
    {
        if (_loggedOut) return;
        _loggedOut = true;

        _refreshTimer?.Dispose();
        _heartbeatTimer?.Stop();
        _heartbeatTimer?.Dispose();
        _kitchenTicketTimer?.Stop();
        _kitchenTicketTimer?.Dispose();

        if (AppSession.LogId is int logId)
        {
            try { await _apiClient.EndSessionAsync(logId); }
            catch { /* best-effort - don't block logout on a network hiccup */ }
        }

        if (AppSession.RefreshToken is string refreshToken)
        {
            try { await _apiClient.LogoutAsync(refreshToken); }
            catch { /* best-effort - don't block logout on a network hiccup */ }
        }

        AppSession.Clear();

        if (!closingApp)
        {
            var login = new FormLogIn();
            login.Show();
            Close();
        }
    }
}
