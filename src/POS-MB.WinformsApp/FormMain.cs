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

        _btnLogout = CreateNavButton("Log Out");
        _btnLogout.Click += async (_, _) => await LogoutAsync();

        var navButtonsPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Left,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(10)
        };
        navButtonsPanel.Controls.Add(_btnNewOrder);
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

        _btnNewOrder.Enabled = AppSession.HasPermission(Permission.Orders) || AppSession.HasPermission(Permission.FullAccess);

        ShowOrderTaking();
    }

    private void ShowOrderTaking()
    {
        _contentArea.Controls.Clear();
        var control = new OrderTakingControl { Dock = DockStyle.Fill };
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
