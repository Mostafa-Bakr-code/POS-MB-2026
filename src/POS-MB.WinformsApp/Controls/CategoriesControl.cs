using POS_MB.WinformsApp.Api;
using POS_MB.WinformsApp.Dialogs;
using POS_MB.WinformsApp.Models;

namespace POS_MB.WinformsApp.Controls;

public class CategoriesControl : UserControl
{
    private readonly ApiClient _apiClient = new();
    private readonly DataGridView _grid;
    private readonly CheckBox _chkShowInactive;
    private List<CategoryDto> _categories = [];
    private string? _sortColumn;
    private bool _sortAscending = true;

    public CategoriesControl()
    {
        Font = new Font("Segoe UI", 12F);

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 60, Padding = new Padding(10) };

        var btnAdd = new Button { Text = "Add Category", Width = 160, Height = 40, Font = new Font("Segoe UI", 11F, FontStyle.Bold) };
        btnAdd.Click += async (_, _) => await AddCategoryAsync();

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
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "CategoryId", HeaderText = "Id", DataPropertyName = "CategoryId", FillWeight = 40, MinimumWidth = 60 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "CategoryName", HeaderText = "Name", DataPropertyName = "CategoryName", FillWeight = 200, MinimumWidth = 200 });
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
        _categories = await _apiClient.GetCategoriesAsync(_chkShowInactive.Checked);
        ApplySortAndBind();
    }

    // DataGridView doesn't support click-to-sort out of the box when bound to a
    // plain List<T> (only IBindingList sources with SupportsSortingCore do) - sort
    // manually and rebind instead.
    private void Grid_ColumnHeaderMouseClick(object? sender, DataGridViewCellMouseEventArgs e)
    {
        var columnName = _grid.Columns[e.ColumnIndex].Name;
        if (columnName is not ("CategoryId" or "CategoryName" or "IsActive")) return;

        _sortAscending = _sortColumn == columnName && _sortAscending ? false : true;
        _sortColumn = columnName;
        ApplySortAndBind();
    }

    private void ApplySortAndBind()
    {
        IEnumerable<CategoryDto> sorted = _sortColumn switch
        {
            "CategoryId" => _categories.OrderBy(c => c.CategoryId),
            "CategoryName" => _categories.OrderBy(c => c.CategoryName, StringComparer.OrdinalIgnoreCase),
            "IsActive" => _categories.OrderBy(c => c.IsActive),
            _ => _categories
        };
        if (_sortColumn is not null && !_sortAscending) sorted = sorted.Reverse();

        foreach (DataGridViewColumn column in _grid.Columns)
            column.HeaderCell.SortGlyphDirection = column.Name == _sortColumn
                ? (_sortAscending ? SortOrder.Ascending : SortOrder.Descending)
                : SortOrder.None;

        _categories = sorted.ToList();
        _grid.DataSource = null;
        _grid.DataSource = _categories;
    }

    private void Grid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (_grid.Columns[e.ColumnIndex].Name != "ToggleActive" || e.RowIndex >= _categories.Count) return;

        e.Value = _categories[e.RowIndex].IsActive ? "Deactivate" : "Reactivate";
        e.FormattingApplied = true;
    }

    private async void Grid_CellClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0) return;

        var category = _categories[e.RowIndex];
        var columnName = _grid.Columns[e.ColumnIndex].Name;

        if (columnName == "Edit")
        {
            using var dialog = new FormTextInputDialog("Edit Category", "Category Name", category.CategoryName);
            if (dialog.ShowDialog(this) == DialogResult.OK && dialog.Value.Length > 0)
            {
                await _apiClient.UpdateCategoryAsync(category.CategoryId, dialog.Value);
                await LoadAsync();
            }
        }
        else if (columnName == "ToggleActive")
        {
            if (category.IsActive)
            {
                var confirm = MessageBox.Show(
                    $"Deactivate '{category.CategoryName}'? It will be hidden from the menu but its history is kept.",
                    "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (confirm != DialogResult.Yes) return;

                await _apiClient.DeactivateCategoryAsync(category.CategoryId);
            }
            else
            {
                await _apiClient.ReactivateCategoryAsync(category.CategoryId);
            }

            await LoadAsync();
        }
    }

    private async Task AddCategoryAsync()
    {
        using var dialog = new FormTextInputDialog("Add Category", "Category Name");
        if (dialog.ShowDialog(this) == DialogResult.OK && dialog.Value.Length > 0)
        {
            await _apiClient.CreateCategoryAsync(dialog.Value);
            await LoadAsync();
        }
    }
}
