using POS_MB.WinformsApp.Api;
using POS_MB.WinformsApp.Models;
using POS_MB.WinformsApp.Session;

namespace POS_MB.WinformsApp.Controls;

// Shift/session history - who logged in, when, and for how long. Admin-only view,
// not present in the old app (which recorded Logs rows but never surfaced them anywhere).
public class LogsControl : UserControl
{
    private readonly ApiClient _apiClient = new();

    private readonly CheckBox _chkUseDateRange;
    private readonly DateTimePicker _dtpStart;
    private readonly DateTimePicker _dtpEnd;
    private readonly DataGridView _grid;

    private List<LogRow> _rows = [];
    private string? _sortColumn;
    private bool _sortAscending = true;

    public LogsControl()
    {
        Font = new Font("Segoe UI", 12F);

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 70, Padding = new Padding(10), AutoScroll = true };

        // Defaults to today only, not all-time - loading every session ever recorded
        // on every screen open is unnecessary and gets slower as history grows.
        // Unchecking the filter still gets the full history on demand.
        _chkUseDateRange = new CheckBox { Text = "Filter by Date Range", AutoSize = true, Checked = true, Margin = new Padding(0, 14, 10, 0) };

        _dtpStart = new DateTimePicker { Width = 150, Height = 36, Format = DateTimePickerFormat.Short, Margin = new Padding(0, 6, 10, 6) };
        _dtpEnd = new DateTimePicker { Width = 150, Height = 36, Format = DateTimePickerFormat.Short, Margin = new Padding(0, 6, 20, 6) };

        _chkUseDateRange.CheckedChanged += (_, _) => { _dtpStart.Enabled = _chkUseDateRange.Checked; _dtpEnd.Enabled = _chkUseDateRange.Checked; };

        var btnRefresh = new Button { Text = "Refresh", Width = 130, Height = 44, Font = new Font("Segoe UI", 11F, FontStyle.Bold), Margin = new Padding(0, 6, 0, 6) };
        btnRefresh.Click += async (_, _) => await LoadAsync();

        toolbar.Controls.Add(_chkUseDateRange);
        toolbar.Controls.Add(_dtpStart);
        toolbar.Controls.Add(_dtpEnd);
        toolbar.Controls.Add(btnRefresh);

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
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "UserName", HeaderText = "User", DataPropertyName = "UserName", FillWeight = 130, MinimumWidth = 180 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "LogIn", HeaderText = "Log In", DataPropertyName = "LogIn", FillWeight = 120, MinimumWidth = 160 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "LogOut", HeaderText = "Log Out", DataPropertyName = "LogOut", FillWeight = 120, MinimumWidth = 160 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Duration", HeaderText = "Duration", DataPropertyName = "Duration", FillWeight = 90, MinimumWidth = 120 });
        // Programmatic (not the default Automatic) so the grid never attempts its own
        // built-in sort-on-click, letting Grid_ColumnHeaderMouseClick handle sorting
        // manually and consistently with the other grids.
        foreach (DataGridViewColumn column in _grid.Columns) column.SortMode = DataGridViewColumnSortMode.Programmatic;
        _grid.CellFormatting += Grid_CellFormatting;
        _grid.ColumnHeaderMouseClick += Grid_ColumnHeaderMouseClick;

        Controls.Add(_grid);
        Controls.Add(toolbar);

        Load += async (_, _) => await LoadAsync();
    }

    private void Grid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        var columnName = _grid.Columns[e.ColumnIndex].Name;
        if ((columnName == "LogIn" || columnName == "LogOut") && e.Value is DateTime date)
        {
            e.Value = AppSession.ToLocalDisplay(date).ToString("yyyy-MM-dd HH:mm");
            e.FormattingApplied = true;
        }
    }

    private async Task LoadAsync()
    {
        DateTime? start = _chkUseDateRange.Checked ? _dtpStart.Value.Date : null;
        DateTime? end = _chkUseDateRange.Checked ? _dtpEnd.Value.Date : null;

        if (start is not null && end is not null && end < start)
        {
            MessageBox.Show("End date cannot be before start date.", "Invalid Date Range", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var logs = await _apiClient.GetLogsAsync(start, end);
        var users = await _apiClient.GetUsersAsync(includeInactive: true);
        var userNamesById = users.ToDictionary(u => u.UserId, u => u.UserName);

        _rows = logs.Select(log => new LogRow
        {
            UserName = userNamesById.GetValueOrDefault(log.UserId, "(unknown)"),
            LogIn = log.LogIn,
            LogOut = log.LogOut,
            DurationSpan = log.LogOut is DateTime logOut ? logOut - log.LogIn : null,
            Duration = log.LogOut is DateTime logOut2 ? FormatDuration(logOut2 - log.LogIn) : "(active)"
        }).ToList();

        ApplySortAndBind();
    }

    // DataGridView doesn't support click-to-sort out of the box when bound to a
    // plain List<T> (only IBindingList sources with SupportsSortingCore do) - sort
    // manually and rebind instead.
    private void Grid_ColumnHeaderMouseClick(object? sender, DataGridViewCellMouseEventArgs e)
    {
        var columnName = _grid.Columns[e.ColumnIndex].Name;
        if (columnName is not ("UserName" or "LogIn" or "LogOut" or "Duration")) return;

        _sortAscending = _sortColumn == columnName && _sortAscending ? false : true;
        _sortColumn = columnName;
        ApplySortAndBind();
    }

    private void ApplySortAndBind()
    {
        // Still-active sessions have no LogOut/Duration - sort them last regardless
        // of direction, rather than letting nulls jump to the front on descending.
        IEnumerable<LogRow> sorted = _sortColumn switch
        {
            "UserName" => _rows.OrderBy(r => r.UserName, StringComparer.OrdinalIgnoreCase),
            "LogIn" => _rows.OrderBy(r => r.LogIn),
            "LogOut" => _rows.OrderBy(r => r.LogOut ?? DateTime.MaxValue),
            "Duration" => _rows.OrderBy(r => r.DurationSpan ?? TimeSpan.MaxValue),
            _ => _rows
        };
        if (_sortColumn is not null && !_sortAscending) sorted = sorted.Reverse();

        foreach (DataGridViewColumn column in _grid.Columns)
            column.HeaderCell.SortGlyphDirection = column.Name == _sortColumn
                ? (_sortAscending ? SortOrder.Ascending : SortOrder.Descending)
                : SortOrder.None;

        _rows = sorted.ToList();
        _grid.DataSource = null;
        _grid.DataSource = _rows;
    }

    private static string FormatDuration(TimeSpan span) =>
        span.TotalHours >= 1
            ? $"{(int)span.TotalHours}h {span.Minutes}m"
            : $"{span.Minutes}m";

    private class LogRow
    {
        public string UserName { get; set; } = string.Empty;
        public DateTime LogIn { get; set; }
        public DateTime? LogOut { get; set; }
        public TimeSpan? DurationSpan { get; set; }
        public string Duration { get; set; } = string.Empty;
    }
}
