using POS_MB.WinformsApp.Api;
using POS_MB.WinformsApp.Dialogs;
using POS_MB.WinformsApp.Models;
using POS_MB.WinformsApp.Session;

namespace POS_MB.WinformsApp.Controls;

public class ItemsControl : UserControl
{
    private readonly ApiClient _apiClient = new();
    private readonly DataGridView _grid;
    private readonly CheckBox _chkShowInactive;
    private readonly ComboBox _cboCategoryFilter;
    private List<ItemDto> _items = [];
    private List<CategoryDto> _categories = [];
    private string? _sortColumn;
    private bool _sortAscending = true;

    public ItemsControl()
    {
        Font = new Font("Segoe UI", 12F);

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 60, Padding = new Padding(10) };

        var btnAdd = new Button { Text = "Add Item", Width = 140, Height = 40, Font = new Font("Segoe UI", 11F, FontStyle.Bold) };
        btnAdd.Click += async (_, _) => await AddItemAsync();

        _cboCategoryFilter = new ComboBox
        {
            Width = 200,
            Height = 40,
            Font = new Font("Segoe UI", 11F),
            DropDownStyle = ComboBoxStyle.DropDownList,
            DisplayMember = nameof(CategoryFilterOption.Name),
            Margin = new Padding(10, 10, 20, 0)
        };
        _cboCategoryFilter.SelectedIndexChanged += CategoryFilter_SelectedIndexChanged;

        _chkShowInactive = new CheckBox { Text = "Show Inactive", AutoSize = true, Margin = new Padding(0, 12, 0, 0) };
        _chkShowInactive.CheckedChanged += async (_, _) => await LoadAsync();

        toolbar.Controls.Add(btnAdd);
        toolbar.Controls.Add(_cboCategoryFilter);
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
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ItemId", HeaderText = "Id", DataPropertyName = "ItemId", FillWeight = 40, MinimumWidth = 60 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ItemName", HeaderText = "Name", DataPropertyName = "ItemName", FillWeight = 130, MinimumWidth = 150 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "CategoryId", HeaderText = "Category", DataPropertyName = "CategoryId", FillWeight = 110, MinimumWidth = 130 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Price", HeaderText = "Price", DataPropertyName = "Price", FillWeight = 70, MinimumWidth = 90 });
        _grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "IsActive", HeaderText = "Active", DataPropertyName = "IsActive", FillWeight = 50, MinimumWidth = 70 });
        _grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "IsAvailable", HeaderText = "In Stock", DataPropertyName = "IsAvailable", FillWeight = 55, MinimumWidth = 75 });
        _grid.Columns.Add(new DataGridViewButtonColumn { Name = "Edit", HeaderText = "", Text = "Edit", UseColumnTextForButtonValue = true, FillWeight = 70, MinimumWidth = 90 });
        _grid.Columns.Add(new DataGridViewButtonColumn { Name = "Availability", HeaderText = "", FillWeight = 140, MinimumWidth = 190 });
        _grid.Columns.Add(new DataGridViewButtonColumn { Name = "History", HeaderText = "", Text = "Price History", UseColumnTextForButtonValue = true, FillWeight = 90, MinimumWidth = 120 });
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

    private void Grid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex >= _items.Count) return;
        var item = _items[e.RowIndex];
        var columnName = _grid.Columns[e.ColumnIndex].Name;
        var categoryIsInactive = !(_categories.FirstOrDefault(c => c.CategoryId == item.CategoryId)?.IsActive ?? true);

        // Make out-of-stock items impossible to miss - a checkbox alone is too easy to skim past.
        if (!item.IsAvailable)
        {
            e.CellStyle.BackColor = Color.FromArgb(248, 215, 218);
            e.CellStyle.SelectionBackColor = Color.FromArgb(220, 53, 69);
            e.CellStyle.SelectionForeColor = Color.White;
        }
        // Item shows "Active" but its category is hidden from New Order, so it's
        // silently unorderable - flag it rather than let that go unnoticed. Out-of-
        // stock (above) takes visual priority since it's the more urgent state.
        else if (item.IsActive && categoryIsInactive)
        {
            e.CellStyle.BackColor = Color.FromArgb(255, 229, 199);
            e.CellStyle.SelectionBackColor = Color.FromArgb(253, 126, 20);
            e.CellStyle.SelectionForeColor = Color.White;
        }

        if (columnName == "CategoryId" && e.Value is not null)
        {
            var name = CategoryName(Convert.ToInt32(e.Value));
            e.Value = item.IsActive && categoryIsInactive ? $"{name} (inactive)" : name;
            e.FormattingApplied = true;
        }
        else if (columnName == "Availability")
        {
            e.Value = item.IsAvailable ? "Mark Out of Stock" : "Mark In Stock";
            e.FormattingApplied = true;
        }
        else if (columnName == "ToggleActive")
        {
            e.Value = item.IsActive ? "Deactivate" : "Reactivate";
            e.FormattingApplied = true;
        }
    }

    private async void CategoryFilter_SelectedIndexChanged(object? sender, EventArgs e) => await LoadAsync();

    private async Task LoadAsync()
    {
        _categories = await _apiClient.GetCategoriesAsync(includeInactive: true);

        var previousSelection = (_cboCategoryFilter.SelectedItem as CategoryFilterOption)?.CategoryId;

        _cboCategoryFilter.SelectedIndexChanged -= CategoryFilter_SelectedIndexChanged;
        _cboCategoryFilter.Items.Clear();
        _cboCategoryFilter.Items.Add(new CategoryFilterOption("All Categories", null));
        foreach (var category in _categories)
            _cboCategoryFilter.Items.Add(new CategoryFilterOption(category.CategoryName, category.CategoryId));

        var toReselect = _cboCategoryFilter.Items.Cast<CategoryFilterOption>()
            .FirstOrDefault(o => o.CategoryId == previousSelection);
        _cboCategoryFilter.SelectedItem = toReselect ?? _cboCategoryFilter.Items[0];
        _cboCategoryFilter.SelectedIndexChanged += CategoryFilter_SelectedIndexChanged;

        var selectedCategoryId = (_cboCategoryFilter.SelectedItem as CategoryFilterOption)?.CategoryId;
        _items = await _apiClient.GetItemsAsync(selectedCategoryId, includeInactive: _chkShowInactive.Checked);
        ApplySortAndBind();
    }

    private string CategoryName(int categoryId) =>
        _categories.FirstOrDefault(c => c.CategoryId == categoryId)?.CategoryName ?? "?";

    // DataGridView doesn't support click-to-sort out of the box when bound to a
    // plain List<T> (only IBindingList sources with SupportsSortingCore do) - sort
    // manually and rebind instead.
    private void Grid_ColumnHeaderMouseClick(object? sender, DataGridViewCellMouseEventArgs e)
    {
        var columnName = _grid.Columns[e.ColumnIndex].Name;
        if (columnName is not ("ItemId" or "ItemName" or "CategoryId" or "Price" or "IsActive" or "IsAvailable")) return;

        _sortAscending = _sortColumn == columnName && _sortAscending ? false : true;
        _sortColumn = columnName;
        ApplySortAndBind();
    }

    private void ApplySortAndBind()
    {
        IEnumerable<ItemDto> sorted = _sortColumn switch
        {
            "ItemId" => _items.OrderBy(i => i.ItemId),
            "ItemName" => _items.OrderBy(i => i.ItemName, StringComparer.OrdinalIgnoreCase),
            "CategoryId" => _items.OrderBy(i => CategoryName(i.CategoryId), StringComparer.OrdinalIgnoreCase),
            "Price" => _items.OrderBy(i => i.Price),
            "IsActive" => _items.OrderBy(i => i.IsActive),
            "IsAvailable" => _items.OrderBy(i => i.IsAvailable),
            _ => _items
        };
        if (_sortColumn is not null && !_sortAscending) sorted = sorted.Reverse();

        foreach (DataGridViewColumn column in _grid.Columns)
            column.HeaderCell.SortGlyphDirection = column.Name == _sortColumn
                ? (_sortAscending ? SortOrder.Ascending : SortOrder.Descending)
                : SortOrder.None;

        _items = sorted.ToList();
        _grid.DataSource = null;
        _grid.DataSource = _items;
    }

    private async void Grid_CellClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0) return;

        var item = _items[e.RowIndex];
        var columnName = _grid.Columns[e.ColumnIndex].Name;

        if (columnName == "Edit")
        {
            using var dialog = new FormItemEditDialog("Edit Item", CategoryOptionsFor(item.CategoryId), item.ItemName, item.CategoryId, item.Price);
            if (dialog.ShowDialog(this) == DialogResult.OK && dialog.IsValid)
            {
                await _apiClient.UpdateItemAsync(item.ItemId, dialog.ItemName, dialog.CategoryId, dialog.Price);
                await LoadAsync();
            }
        }
        else if (columnName == "History")
        {
            var history = await _apiClient.GetItemPriceHistoryAsync(item.ItemId);
            using var dialog = new FormItemPriceHistoryDialog(item.ItemName, history);
            dialog.ShowDialog(this);
        }
        else if (columnName == "ToggleActive")
        {
            if (item.IsActive)
            {
                var confirm = MessageBox.Show(
                    $"Deactivate '{item.ItemName}'? It will be hidden from the menu but its history is kept.",
                    "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (confirm != DialogResult.Yes) return;

                await _apiClient.DeactivateItemAsync(item.ItemId);
            }
            else
            {
                await _apiClient.ReactivateItemAsync(item.ItemId);
            }

            await LoadAsync();
        }
        else if (columnName == "Availability")
        {
            await _apiClient.SetItemAvailabilityAsync(item.ItemId, !item.IsAvailable);
            await LoadAsync();
        }
    }

    private async Task AddItemAsync()
    {
        var activeCategories = CategoryOptionsFor();
        if (activeCategories.Count == 0)
        {
            MessageBox.Show("Add an active category first.", "No Categories", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var dialog = new FormItemEditDialog("Add Item", activeCategories);
        if (dialog.ShowDialog(this) == DialogResult.OK && dialog.IsValid)
        {
            await _apiClient.CreateItemAsync(dialog.ItemName, dialog.CategoryId, dialog.Price);
            await LoadAsync();
        }
    }

    // New/changed category assignments can only point at active categories - a
    // deactivated category is a dead end (hidden from New Order), so letting an
    // item get routed into one would silently make it unorderable. When editing an
    // item whose current category has since been deactivated, that one category is
    // still included so the dialog doesn't strand the item with no valid selection -
    // the admin can leave it as-is or move it to an active category.
    private List<CategoryDto> CategoryOptionsFor(int? currentCategoryId = null)
    {
        var options = _categories.Where(c => c.IsActive).ToList();
        if (currentCategoryId is not null && options.All(c => c.CategoryId != currentCategoryId))
        {
            var current = _categories.FirstOrDefault(c => c.CategoryId == currentCategoryId);
            if (current is not null) options.Add(current);
        }
        return options;
    }

    private record CategoryFilterOption(string Name, int? CategoryId);
}
