using POS_MB.WinformsApp.Api;
using POS_MB.WinformsApp.Dialogs;
using POS_MB.WinformsApp.Models;

namespace POS_MB.WinformsApp.Controls;

public class UsersControl : UserControl
{
    private readonly ApiClient _apiClient = new();
    private readonly DataGridView _grid;
    private readonly CheckBox _chkShowInactive;
    private List<UserDto> _users = [];
    private string? _sortColumn;
    private bool _sortAscending = true;

    public UsersControl()
    {
        Font = new Font("Segoe UI", 12F);

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 60, Padding = new Padding(10) };

        var btnAdd = new Button { Text = "Add User", Width = 140, Height = 40, Font = new Font("Segoe UI", 11F, FontStyle.Bold) };
        btnAdd.Click += async (_, _) => await AddUserAsync();

        _chkShowInactive = new CheckBox { Text = "Show Inactive", AutoSize = true, Margin = new Padding(20, 12, 0, 0) };
        _chkShowInactive.CheckedChanged += async (_, _) => await LoadAsync();

        toolbar.Controls.Add(btnAdd);
        toolbar.Controls.Add(_chkShowInactive);

        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            RowTemplate = { Height = 40 },
            Font = new Font("Segoe UI", 11F)
        };
        // FillWeight (not Width) drives sizing in Fill mode - proportional shares of
        // whatever width is available, instead of fixed pixel columns leaving dead
        // space on a wide window.
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "UserId", HeaderText = "Id", DataPropertyName = "UserId", FillWeight = 40, MinimumWidth = 60 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "UserName", HeaderText = "Username", DataPropertyName = "UserName", FillWeight = 150, MinimumWidth = 160 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Permissions", HeaderText = "Permissions", DataPropertyName = "Permissions", FillWeight = 130, MinimumWidth = 160 });
        _grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "IsActive", HeaderText = "Active", DataPropertyName = "IsActive", FillWeight = 60, MinimumWidth = 70 });
        _grid.Columns.Add(new DataGridViewButtonColumn { Name = "Edit", HeaderText = "", Text = "Edit", UseColumnTextForButtonValue = true, FillWeight = 70, MinimumWidth = 90 });
        _grid.Columns.Add(new DataGridViewButtonColumn { Name = "ToggleActive", HeaderText = "", FillWeight = 80, MinimumWidth = 110 });
        // Programmatic (not the default Automatic) so the grid never attempts its own
        // built-in sort-on-click - that path collides with checkbox columns (they try
        // to commit cell edit state mid-sort) and throws. Grid_ColumnHeaderMouseClick
        // handles all sorting manually instead.
        foreach (DataGridViewColumn column in _grid.Columns) column.SortMode = DataGridViewColumnSortMode.Programmatic;
        _grid.CellClick += Grid_CellClick;
        _grid.CellFormatting += Grid_CellFormatting;
        _grid.ColumnHeaderMouseClick += Grid_ColumnHeaderMouseClick;

        Controls.Add(_grid);
        Controls.Add(toolbar);

        Load += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _users = await _apiClient.GetUsersAsync(_chkShowInactive.Checked);
        ApplySortAndBind();
    }

    // DataGridView doesn't support click-to-sort out of the box when bound to a
    // plain List<T> (only IBindingList sources with SupportsSortingCore do) - sort
    // manually and rebind instead.
    private void Grid_ColumnHeaderMouseClick(object? sender, DataGridViewCellMouseEventArgs e)
    {
        var columnName = _grid.Columns[e.ColumnIndex].Name;
        if (columnName is not ("UserId" or "UserName" or "Permissions" or "IsActive")) return;

        _sortAscending = _sortColumn == columnName && _sortAscending ? false : true;
        _sortColumn = columnName;
        ApplySortAndBind();
    }

    private void ApplySortAndBind()
    {
        IEnumerable<UserDto> sorted = _sortColumn switch
        {
            "UserId" => _users.OrderBy(u => u.UserId),
            "UserName" => _users.OrderBy(u => u.UserName, StringComparer.OrdinalIgnoreCase),
            "Permissions" => _users.OrderBy(u => u.Permissions),
            "IsActive" => _users.OrderBy(u => u.IsActive),
            _ => _users
        };
        if (_sortColumn is not null && !_sortAscending) sorted = sorted.Reverse();

        foreach (DataGridViewColumn column in _grid.Columns)
            column.HeaderCell.SortGlyphDirection = column.Name == _sortColumn
                ? (_sortAscending ? SortOrder.Ascending : SortOrder.Descending)
                : SortOrder.None;

        _users = sorted.ToList();
        _grid.DataSource = null;
        _grid.DataSource = _users;
    }

    private void Grid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        var columnName = _grid.Columns[e.ColumnIndex].Name;

        if (columnName == "Permissions" && e.Value is not null)
        {
            var permission = (Permission)Convert.ToInt32(e.Value);
            e.Value = permission == Permission.FullAccess ? "Full Access" : permission.ToString();
            e.FormattingApplied = true;
        }
        else if (columnName == "ToggleActive" && e.RowIndex < _users.Count)
        {
            e.Value = _users[e.RowIndex].IsActive ? "Deactivate" : "Reactivate";
            e.FormattingApplied = true;
        }
    }

    private async void Grid_CellClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0) return;

        var user = _users[e.RowIndex];
        var columnName = _grid.Columns[e.ColumnIndex].Name;

        if (columnName == "Edit")
        {
            using var dialog = new FormUserEditDialog("Edit User", isEdit: true, user.UserName, user.Permissions);
            if (dialog.ShowDialog(this) == DialogResult.OK && dialog.IsValid)
            {
                await _apiClient.UpdateUserAsync(user.UserId, dialog.UserNameValue, dialog.PasswordValue, dialog.Permissions);
                await LoadAsync();
            }
        }
        else if (columnName == "ToggleActive")
        {
            if (user.IsActive)
            {
                var confirm = MessageBox.Show(
                    $"Deactivate '{user.UserName}'? They will no longer be able to log in.",
                    "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (confirm != DialogResult.Yes) return;

                await _apiClient.DeactivateUserAsync(user.UserId);
            }
            else
            {
                await _apiClient.ReactivateUserAsync(user.UserId);
            }

            await LoadAsync();
        }
    }

    private async Task AddUserAsync()
    {
        using var dialog = new FormUserEditDialog("Add User", isEdit: false);
        if (dialog.ShowDialog(this) == DialogResult.OK && dialog.IsValid)
        {
            await _apiClient.CreateUserAsync(dialog.UserNameValue, dialog.PasswordValue!, dialog.Permissions);
            await LoadAsync();
        }
    }
}
