using POS_MB.WinformsApp.Models;

namespace POS_MB.WinformsApp.Dialogs;

public class FormItemEditDialog : Form
{
    private readonly TextBox _txtName;
    private readonly ComboBox _cboCategory;
    private readonly NumericUpDown _numPrice;

    public string ItemName => _txtName.Text.Trim();
    public int CategoryId => ((CategoryDto)_cboCategory.SelectedItem!).CategoryId;
    public decimal Price => _numPrice.Value;

    public FormItemEditDialog(string title, List<CategoryDto> categories, string initialName = "", int? initialCategoryId = null, decimal initialPrice = 0)
    {
        Text = title;
        Size = new Size(420, 320);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = new Font("Segoe UI", 12F);

        var lblName = new Label { Text = "Item Name", Location = new Point(20, 20), Size = new Size(360, 28) };
        _txtName = new TextBox { Text = initialName, Location = new Point(20, 52), Size = new Size(360, 36), Font = new Font("Segoe UI", 14F) };

        var lblCategory = new Label { Text = "Category", Location = new Point(20, 96), Size = new Size(360, 28) };
        _cboCategory = new ComboBox
        {
            Location = new Point(20, 128),
            Size = new Size(360, 36),
            Font = new Font("Segoe UI", 14F),
            DropDownStyle = ComboBoxStyle.DropDownList,
            DisplayMember = nameof(CategoryDto.CategoryName)
        };
        _cboCategory.Items.AddRange([.. categories]);
        if (initialCategoryId is not null)
            _cboCategory.SelectedItem = categories.FirstOrDefault(c => c.CategoryId == initialCategoryId);
        else if (categories.Count > 0)
            _cboCategory.SelectedIndex = 0;

        var lblPrice = new Label { Text = "Price", Location = new Point(20, 172), Size = new Size(360, 28) };
        _numPrice = new NumericUpDown
        {
            Location = new Point(20, 204),
            Size = new Size(360, 36),
            Font = new Font("Segoe UI", 14F),
            DecimalPlaces = 2,
            Maximum = 100000,
            Value = initialPrice
        };

        var btnSave = new Button { Text = "Save", Location = new Point(20, 252), Size = new Size(170, 50), Font = new Font("Segoe UI", 12F, FontStyle.Bold), DialogResult = DialogResult.OK };
        var btnCancel = new Button { Text = "Cancel", Location = new Point(210, 252), Size = new Size(170, 50), Font = new Font("Segoe UI", 12F), DialogResult = DialogResult.Cancel };

        Controls.Add(lblName);
        Controls.Add(_txtName);
        Controls.Add(lblCategory);
        Controls.Add(_cboCategory);
        Controls.Add(lblPrice);
        Controls.Add(_numPrice);
        Controls.Add(btnSave);
        Controls.Add(btnCancel);

        AcceptButton = btnSave;
        CancelButton = btnCancel;
    }

    public bool IsValid => ItemName.Length > 0 && _cboCategory.SelectedItem is not null;
}
