using POS_MB.WinformsApp.Models;

namespace POS_MB.WinformsApp.Dialogs;

public class FormItemEditDialog : Form
{
    private readonly TextBox _txtName;
    private readonly ComboBox _cboCategory;
    private readonly NumericUpDown _numPrice;
    private readonly TextBox _txtDescription;
    private readonly PictureBox _picPreview;
    private readonly Button _btnRemoveImage;
    private readonly bool _hadInitialImage;

    public string ItemName => _txtName.Text.Trim();
    public int CategoryId => ((CategoryDto)_cboCategory.SelectedItem!).CategoryId;
    public decimal Price => _numPrice.Value;

    // Empty input maps to null, not "" - a blank description and "never set"
    // should mean the same thing everywhere this is read.
    public string? Description => _txtDescription.Text.Trim() is { Length: > 0 } trimmed ? trimmed : null;

    // Set only when the user picks a new file this session - null means
    // "leave the existing photo (or lack of one) alone".
    public string? SelectedImagePath { get; private set; }

    // True only when an existing photo was explicitly removed via the button -
    // distinct from SelectedImagePath being null (which just means "no change").
    public bool RemoveImageRequested { get; private set; }

    public FormItemEditDialog(string title, List<CategoryDto> categories, string initialName = "", int? initialCategoryId = null, decimal initialPrice = 0, string? initialImagePreviewUrl = null, string? initialDescription = null)
    {
        _hadInitialImage = initialImagePreviewUrl is not null;

        Text = title;
        ClientSize = new Size(420, 560);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = new Font("Segoe UI", 12F);

        var lblName = new Label { Text = "Item Name", Location = new Point(20, 20), Size = new Size(360, 28) };
        _txtName = new TextBox { Text = initialName, Location = new Point(20, 52), Size = new Size(360, 36), Font = new Font("Segoe UI", 14F), MaxLength = 50 };

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

        var lblDescription = new Label { Text = "Description (optional)", Location = new Point(20, 248), Size = new Size(360, 28) };
        _txtDescription = new TextBox
        {
            Text = initialDescription ?? "",
            Location = new Point(20, 280),
            Size = new Size(360, 50),
            Font = new Font("Segoe UI", 11F),
            MaxLength = 500,
            Multiline = true,
            AcceptsReturn = true
        };

        var lblPhoto = new Label { Text = "Photo", Location = new Point(20, 338), Size = new Size(360, 28) };
        _picPreview = new PictureBox
        {
            Location = new Point(20, 370),
            Size = new Size(100, 100),
            BorderStyle = BorderStyle.FixedSingle,
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.WhiteSmoke
        };
        if (initialImagePreviewUrl is not null)
        {
            try { _picPreview.LoadAsync(initialImagePreviewUrl); } catch { /* best-effort preview only */ }
        }

        var btnChooseImage = new Button { Text = "Choose Image...", Location = new Point(134, 370), Size = new Size(246, 40), Font = new Font("Segoe UI", 11F) };
        btnChooseImage.Click += (_, _) =>
        {
            using var openDialog = new OpenFileDialog { Filter = "Images (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png" };
            if (openDialog.ShowDialog(this) != DialogResult.OK) return;

            SelectedImagePath = openDialog.FileName;
            RemoveImageRequested = false;
            _picPreview.ImageLocation = null;
            _picPreview.Image = Image.FromFile(openDialog.FileName);
            _btnRemoveImage.Enabled = true;
        };

        _btnRemoveImage = new Button { Text = "Remove Photo", Location = new Point(134, 416), Size = new Size(246, 36), Font = new Font("Segoe UI", 10F), Enabled = _hadInitialImage };
        _btnRemoveImage.Click += (_, _) =>
        {
            SelectedImagePath = null;
            RemoveImageRequested = true;
            _picPreview.Image = null;
            _picPreview.ImageLocation = null;
            _btnRemoveImage.Enabled = false;
        };

        var btnSave = new Button { Text = "Save", Location = new Point(20, 482), Size = new Size(170, 50), Font = new Font("Segoe UI", 12F, FontStyle.Bold), DialogResult = DialogResult.OK };
        var btnCancel = new Button { Text = "Cancel", Location = new Point(210, 482), Size = new Size(170, 50), Font = new Font("Segoe UI", 12F), DialogResult = DialogResult.Cancel };

        Controls.Add(lblName);
        Controls.Add(_txtName);
        Controls.Add(lblCategory);
        Controls.Add(_cboCategory);
        Controls.Add(lblPrice);
        Controls.Add(_numPrice);
        Controls.Add(lblDescription);
        Controls.Add(_txtDescription);
        Controls.Add(lblPhoto);
        Controls.Add(_picPreview);
        Controls.Add(btnChooseImage);
        Controls.Add(_btnRemoveImage);
        Controls.Add(btnSave);
        Controls.Add(btnCancel);

        AcceptButton = btnSave;
        CancelButton = btnCancel;
    }

    public bool IsValid => ItemName.Length > 0 && _cboCategory.SelectedItem is not null;
}
