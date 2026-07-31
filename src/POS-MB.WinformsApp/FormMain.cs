using POS_MB.WinformsApp.Controls;
using POS_MB.WinformsApp.Models;
using POS_MB.WinformsApp.Session;

namespace POS_MB.WinformsApp;

public class FormMain : Form
{
    private readonly Panel _navBar;
    private readonly Panel _contentArea;
    private readonly Label _lblActiveUser;
    private readonly Button _btnNewOrder;
    private readonly Button _btnCategories;
    private readonly Button _btnItems;
    private readonly Button _btnUsers;
    private readonly Button _btnOrderHistory;
    private readonly Button _btnDailySummary;
    private readonly Button _btnReports;
    private readonly Button _btnSettings;
    private readonly Button _btnLogout;

    public FormMain()
    {
        Text = "POS-MB";
        WindowState = FormWindowState.Maximized;
        Font = new Font("Segoe UI", 12F);

        _navBar = new Panel
        {
            Dock = DockStyle.Top,
            Height = 90,
            BackColor = Color.FromArgb(33, 37, 41)
        };

        _lblActiveUser = new Label
        {
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 12F, FontStyle.Bold),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Dock = DockStyle.Right,
            Width = 260
        };

        _btnNewOrder = CreateNavButton("New Order");
        _btnNewOrder.Click += (_, _) => ShowOrderTaking();

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

        _btnLogout = CreateNavButton("Log Out");
        _btnLogout.Click += async (_, _) => await LogoutAsync();

        var navButtonsPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Left,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(10)
        };
        navButtonsPanel.Controls.Add(_btnNewOrder);
        navButtonsPanel.Controls.Add(_btnCategories);
        navButtonsPanel.Controls.Add(_btnItems);
        navButtonsPanel.Controls.Add(_btnUsers);
        navButtonsPanel.Controls.Add(_btnOrderHistory);
        navButtonsPanel.Controls.Add(_btnDailySummary);
        navButtonsPanel.Controls.Add(_btnReports);
        navButtonsPanel.Controls.Add(_btnSettings);
        navButtonsPanel.Controls.Add(_btnLogout);

        _navBar.Controls.Add(navButtonsPanel);
        _navBar.Controls.Add(_lblActiveUser);

        _contentArea = new Panel { Dock = DockStyle.Fill };

        Controls.Add(_contentArea);
        Controls.Add(_navBar);

        Load += FormMain_Load;
        FormClosing += async (_, _) => await LogoutAsync(closingApp: true);
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
        _btnCategories.Enabled = AppSession.HasPermission(Permission.Categories);
        _btnItems.Enabled = AppSession.HasPermission(Permission.Items);
        _btnUsers.Enabled = AppSession.HasPermission(Permission.Users);
        _btnOrderHistory.Enabled = AppSession.HasPermission(Permission.OrderHistory);
        _btnDailySummary.Enabled = AppSession.HasPermission(Permission.DailySummary);
        _btnReports.Enabled = AppSession.HasPermission(Permission.Reports);
        _btnSettings.Enabled = AppSession.HasPermission(Permission.Settings);

        ShowOrderTaking();
    }

    private void ShowOrderTaking() => ShowContent(new OrderTakingControl());

    private void ShowContent(Control control)
    {
        control.Dock = DockStyle.Fill;
        _contentArea.Controls.Clear();
        _contentArea.Controls.Add(control);
    }

    private bool _loggedOut;

    private async Task LogoutAsync(bool closingApp = false)
    {
        if (_loggedOut) return;
        _loggedOut = true;

        if (AppSession.LogId is int logId)
        {
            try { await new POS_MB.WinformsApp.Api.ApiClient().EndSessionAsync(logId); }
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
