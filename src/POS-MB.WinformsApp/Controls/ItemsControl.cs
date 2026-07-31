using POS_MB.WinformsApp.Api;
using POS_MB.WinformsApp.Dialogs;
using POS_MB.WinformsApp.Models;

namespace POS_MB.WinformsApp.Controls;

public class ItemsControl : UserControl
{
    private readonly ApiClient _apiClient = new();
    private readonly DataGridView _grid;
    private readonly CheckBox _chkShowInactive;
    private readonly ComboBox _cboCategoryFilter;
    private List<ItemDto> _items = [];
    private List<CategoryDto> _categories = [];

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
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            RowTemplate = { Height = 40 },
            Font = new Font("Segoe UI", 11F)
        };
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ItemId", HeaderText = "Id", DataPropertyName = "ItemId", Width = 60 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ItemName", HeaderText = "Name", DataPropertyName = "ItemName", Width = 180 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "CategoryId", HeaderText = "Category", DataPropertyName = "CategoryId", Width = 150 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Price", HeaderText = "Price", DataPropertyName = "Price", Width = 100 });
        _grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "IsActive", HeaderText = "Active", DataPropertyName = "IsActive", Width = 80 });
        _grid.Columns.Add(new DataGridViewButtonColumn { Name = "Edit", HeaderText = "", Text = "Edit", UseColumnTextForButtonValue = true, Width = 100 });
        _grid.Columns.Add(new DataGridViewButtonColumn { Name = "Deactivate", HeaderText = "", Text = "Deactivate", UseColumnTextForButtonValue = true, Width = 120 });
        _grid.CellClick += Grid_CellClick;
        _grid.CellFormatting += Grid_CellFormatting;

        Controls.Add(_grid);
        Controls.Add(toolbar);

        Load += async (_, _) => await LoadAsync();
    }

    private void Grid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (_grid.Columns[e.ColumnIndex].Name != "CategoryId" || e.Value is null) return;

        e.Value = CategoryName(Convert.ToInt32(e.Value));
        e.FormattingApplied = true;
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
        _grid.DataSource = null;
        _grid.DataSource = _items;
    }

    private string CategoryName(int categoryId) =>
        _categories.FirstOrDefault(c => c.CategoryId == categoryId)?.CategoryName ?? "?";

    private async void Grid_CellClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0) return;

        var item = _items[e.RowIndex];
        var columnName = _grid.Columns[e.ColumnIndex].Name;

        if (columnName == "Edit")
        {
            using var dialog = new FormItemEditDialog("Edit Item", _categories, item.ItemName, item.CategoryId, item.Price);
            if (dialog.ShowDialog(this) == DialogResult.OK && dialog.IsValid)
            {
                await _apiClient.UpdateItemAsync(item.ItemId, dialog.ItemName, dialog.CategoryId, dialog.Price);
                await LoadAsync();
            }
        }
        else if (columnName == "Deactivate")
        {
            var confirm = MessageBox.Show(
                $"Deactivate '{item.ItemName}'? It will be hidden from the menu but its history is kept.",
                "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (confirm == DialogResult.Yes)
            {
                await _apiClient.DeactivateItemAsync(item.ItemId);
                await LoadAsync();
            }
        }
    }

    private async Task AddItemAsync()
    {
        if (_categories.Count == 0)
        {
            MessageBox.Show("Add a category first.", "No Categories", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var dialog = new FormItemEditDialog("Add Item", _categories);
        if (dialog.ShowDialog(this) == DialogResult.OK && dialog.IsValid)
        {
            await _apiClient.CreateItemAsync(dialog.ItemName, dialog.CategoryId, dialog.Price);
            await LoadAsync();
        }
    }

    private record CategoryFilterOption(string Name, int? CategoryId);
}
