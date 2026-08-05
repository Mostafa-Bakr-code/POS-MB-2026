using System.Globalization;
using POS_MB.WinformsApp.Api;
using POS_MB.WinformsApp.Session;

namespace POS_MB.WinformsApp;

public class FormLogIn : Form
{
    private readonly ApiClient _apiClient = new();
    private readonly TextBox _txtUserName;
    private readonly TextBox _txtPassword;
    private readonly Label _lblError;
    private readonly Button _btnLogin;

    public FormLogIn()
    {
        Text = "POS-MB - Login";
        ClientSize = new Size(480, 400);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = new Font("Segoe UI", 12F);

        var lblTitle = new Label
        {
            Text = "POS-MB",
            Font = new Font("Segoe UI", 28F, FontStyle.Bold),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(0, 30),
            Size = new Size(480, 60)
        };

        var lblUserName = new Label { Text = "Username", Location = new Point(60, 130), Size = new Size(360, 28) };
        _txtUserName = new TextBox { Location = new Point(60, 162), Size = new Size(360, 36), Font = new Font("Segoe UI", 14F) };

        var lblPassword = new Label { Text = "Password", Location = new Point(60, 208), Size = new Size(360, 28) };
        _txtPassword = new TextBox { Location = new Point(60, 240), Size = new Size(360, 36), Font = new Font("Segoe UI", 14F), UseSystemPasswordChar = true };

        _lblError = new Label
        {
            Text = "",
            ForeColor = Color.Red,
            Location = new Point(60, 284),
            Size = new Size(360, 40),
            TextAlign = ContentAlignment.MiddleCenter
        };

        _btnLogin = new Button
        {
            Text = "Log In",
            Location = new Point(60, 328),
            Size = new Size(360, 50),
            Font = new Font("Segoe UI", 14F, FontStyle.Bold)
        };
        _btnLogin.Click += BtnLogin_Click;

        Controls.Add(lblTitle);
        Controls.Add(lblUserName);
        Controls.Add(_txtUserName);
        Controls.Add(lblPassword);
        Controls.Add(_txtPassword);
        Controls.Add(_lblError);
        Controls.Add(_btnLogin);

        AcceptButton = _btnLogin;
        Shown += (_, _) => _txtUserName.Focus();
    }

    private async void BtnLogin_Click(object? sender, EventArgs e)
    {
        _lblError.Text = "";

        if (string.IsNullOrWhiteSpace(_txtUserName.Text) || string.IsNullOrWhiteSpace(_txtPassword.Text))
        {
            _lblError.Text = "Enter a username and password.";
            return;
        }

        _btnLogin.Enabled = false;
        try
        {
            var login = await _apiClient.VerifyCredentialsAsync(_txtUserName.Text.Trim(), _txtPassword.Text);
            if (login is null)
            {
                _lblError.Text = "Invalid username or password.";
                return;
            }

            AppSession.Token = login.Token;
            AppSession.RefreshToken = login.RefreshToken;

            var logId = await _apiClient.StartSessionAsync();

            AppSession.CurrentUser = login.User;
            AppSession.LogId = logId;

            var offsetValue = await _apiClient.GetSettingValueAsync("TimeZoneOffsetHours");
            AppSession.TimeZoneOffsetHours = offsetValue is not null && decimal.TryParse(offsetValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var offset)
                ? offset
                : 0m;

            var main = new FormMain();
            main.FormClosed += (_, _) => Close();
            main.Show();
            Hide();
        }
        catch (Exception ex)
        {
            _lblError.Text = $"Could not reach the server: {ex.Message}";
        }
        finally
        {
            _btnLogin.Enabled = true;
        }
    }
}
