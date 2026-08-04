using POS_MB.WinformsApp.Models;

namespace POS_MB.WinformsApp.Dialogs;

public class FormUserEditDialog : Form
{
    private readonly bool _isEdit;
    private readonly TextBox _txtUserName;
    private readonly TextBox _txtPassword;
    private readonly CheckBox _chkFullAccess;
    private readonly CheckBox _chkCategories;
    private readonly CheckBox _chkItems;
    private readonly CheckBox _chkOrders;
    private readonly CheckBox _chkUsers;
    private readonly CheckBox _chkReports;
    private readonly CheckBox _chkOrderHistory;
    private readonly CheckBox _chkDailySummary;
    private readonly CheckBox _chkSettings;
    private readonly CheckBox _chkLogs;
    private readonly CheckBox _chkComplimentary;

    public string UserNameValue => _txtUserName.Text.Trim();
    public string? PasswordValue => string.IsNullOrWhiteSpace(_txtPassword.Text) ? null : _txtPassword.Text;

    public int Permissions
    {
        get
        {
            if (_chkFullAccess.Checked) return (int)Permission.FullAccess;

            var permission = Permission.None;
            if (_chkCategories.Checked) permission |= Permission.Categories;
            if (_chkItems.Checked) permission |= Permission.Items;
            if (_chkOrders.Checked) permission |= Permission.Orders;
            if (_chkUsers.Checked) permission |= Permission.Users;
            if (_chkReports.Checked) permission |= Permission.Reports;
            if (_chkOrderHistory.Checked) permission |= Permission.OrderHistory;
            if (_chkDailySummary.Checked) permission |= Permission.DailySummary;
            if (_chkSettings.Checked) permission |= Permission.Settings;
            if (_chkLogs.Checked) permission |= Permission.Logs;
            if (_chkComplimentary.Checked) permission |= Permission.Complimentary;
            return (int)permission;
        }
    }

    public bool IsValid => UserNameValue.Length > 0 && (_isEdit || !string.IsNullOrWhiteSpace(_txtPassword.Text));

    public FormUserEditDialog(string title, bool isEdit, string initialUserName = "", int initialPermissions = 0)
    {
        _isEdit = isEdit;
        Text = title;
        ClientSize = new Size(420, 590);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = new Font("Segoe UI", 12F);

        var lblUserName = new Label { Text = "Username", Location = new Point(20, 20), Size = new Size(360, 28) };
        _txtUserName = new TextBox { Text = initialUserName, Location = new Point(20, 52), Size = new Size(360, 36), Font = new Font("Segoe UI", 14F), MaxLength = 50 };

        var lblPassword = new Label
        {
            Text = isEdit ? "Password (leave blank to keep current)" : "Password",
            Location = new Point(20, 96),
            Size = new Size(360, 28)
        };
        _txtPassword = new TextBox { Location = new Point(20, 128), Size = new Size(360, 36), Font = new Font("Segoe UI", 14F), UseSystemPasswordChar = true };

        var lblPermissions = new Label { Text = "Permissions", Location = new Point(20, 172), Size = new Size(360, 28), Font = new Font("Segoe UI", 12F, FontStyle.Bold) };

        var existing = (Permission)initialPermissions;
        _chkFullAccess = new CheckBox { Text = "Full Access", Location = new Point(20, 204), Size = new Size(360, 28), Checked = existing == Permission.FullAccess };
        _chkCategories = new CheckBox { Text = "Categories", Location = new Point(20, 236), Size = new Size(170, 28), Checked = existing.HasFlag(Permission.Categories) };
        _chkItems = new CheckBox { Text = "Items", Location = new Point(210, 236), Size = new Size(170, 28), Checked = existing.HasFlag(Permission.Items) };
        // Labeled to match the actual nav button ("New Order") it gates, not the
        // underlying Permission.Orders enum name - "Orders" alone reads like it
        // means Order History and is easy to leave unchecked by mistake.
        _chkOrders = new CheckBox { Text = "New Order", Location = new Point(20, 268), Size = new Size(170, 28), Checked = existing.HasFlag(Permission.Orders) };
        _chkUsers = new CheckBox { Text = "Users", Location = new Point(210, 268), Size = new Size(170, 28), Checked = existing.HasFlag(Permission.Users) };
        _chkReports = new CheckBox { Text = "Reports", Location = new Point(20, 300), Size = new Size(170, 28), Checked = existing.HasFlag(Permission.Reports) };
        _chkOrderHistory = new CheckBox { Text = "Order History", Location = new Point(210, 300), Size = new Size(170, 28), Checked = existing.HasFlag(Permission.OrderHistory) };
        _chkDailySummary = new CheckBox { Text = "Daily Summary Only", Location = new Point(20, 332), Size = new Size(200, 28), Checked = existing.HasFlag(Permission.DailySummary) };
        _chkSettings = new CheckBox { Text = "Settings", Location = new Point(230, 332), Size = new Size(150, 28), Checked = existing.HasFlag(Permission.Settings) };
        _chkLogs = new CheckBox { Text = "Logs", Location = new Point(20, 364), Size = new Size(170, 28), Checked = existing.HasFlag(Permission.Logs) };
        _chkComplimentary = new CheckBox { Text = "Complimentary Orders", Location = new Point(20, 396), Size = new Size(280, 28), Checked = existing.HasFlag(Permission.Complimentary) };

        var individualChecks = new[] { _chkCategories, _chkItems, _chkOrders, _chkUsers, _chkReports, _chkOrderHistory, _chkDailySummary, _chkSettings, _chkLogs, _chkComplimentary };
        _chkFullAccess.CheckedChanged += (_, _) =>
        {
            foreach (var chk in individualChecks) chk.Enabled = !_chkFullAccess.Checked;
        };
        foreach (var chk in individualChecks) chk.Enabled = !_chkFullAccess.Checked;

        var btnSave = new Button { Text = "Save", Location = new Point(20, 522), Size = new Size(170, 50), Font = new Font("Segoe UI", 12F, FontStyle.Bold), DialogResult = DialogResult.OK };
        var btnCancel = new Button { Text = "Cancel", Location = new Point(210, 522), Size = new Size(170, 50), Font = new Font("Segoe UI", 12F), DialogResult = DialogResult.Cancel };

        Controls.Add(lblUserName);
        Controls.Add(_txtUserName);
        Controls.Add(lblPassword);
        Controls.Add(_txtPassword);
        Controls.Add(lblPermissions);
        Controls.Add(_chkFullAccess);
        Controls.Add(_chkCategories);
        Controls.Add(_chkItems);
        Controls.Add(_chkOrders);
        Controls.Add(_chkUsers);
        Controls.Add(_chkReports);
        Controls.Add(_chkOrderHistory);
        Controls.Add(_chkDailySummary);
        Controls.Add(_chkSettings);
        Controls.Add(_chkLogs);
        Controls.Add(_chkComplimentary);
        Controls.Add(btnSave);
        Controls.Add(btnCancel);

        AcceptButton = btnSave;
        CancelButton = btnCancel;
    }
}
