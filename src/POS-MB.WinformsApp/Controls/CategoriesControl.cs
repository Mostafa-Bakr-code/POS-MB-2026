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
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            RowTemplate = { Height = 40 },
            Font = new Font("Segoe UI", 11F)
        };
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "CategoryId", HeaderText = "Id", DataPropertyName = "CategoryId", Width = 60 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "CategoryName", HeaderText = "Name", DataPropertyName = "CategoryName", Width = 250 });
        _grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "IsActive", HeaderText = "Active", DataPropertyName = "IsActive", Width = 80 });
        _grid.Columns.Add(new DataGridViewButtonColumn { Name = "Edit", HeaderText = "", Text = "Edit", UseColumnTextForButtonValue = true, Width = 100 });
        _grid.Columns.Add(new DataGridViewButtonColumn { Name = "Deactivate", HeaderText = "", Text = "Deactivate", UseColumnTextForButtonValue = true, Width = 120 });
        _grid.CellClick += Grid_CellClick;

        Controls.Add(_grid);
        Controls.Add(toolbar);

        Load += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _categories = await _apiClient.GetCategoriesAsync(_chkShowInactive.Checked);
        _grid.DataSource = null;
        _grid.DataSource = _categories;
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
        else if (columnName == "Deactivate")
        {
            var confirm = MessageBox.Show(
                $"Deactivate '{category.CategoryName}'? It will be hidden from the menu but its history is kept.",
                "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (confirm == DialogResult.Yes)
            {
                await _apiClient.DeactivateCategoryAsync(category.CategoryId);
                await LoadAsync();
            }
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
