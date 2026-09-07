namespace POS_MB.WinformsApp.Dialogs;

public class FormCategoryEditDialog : Form
{
    private readonly TextBox _txtName;
    private readonly PictureBox _picPreview;
    private readonly Button _btnRemoveImage;
    private readonly bool _hadInitialImage;

    public string CategoryName => _txtName.Text.Trim();

    // Set only when the user picks a new file this session - null means
    // "leave the existing photo (or lack of one) alone".
    public string? SelectedImagePath { get; private set; }

    // True only when an existing photo was explicitly removed via the button -
    // distinct from SelectedImagePath being null (which just means "no change").
    public bool RemoveImageRequested { get; private set; }

    public FormCategoryEditDialog(string title, string initialName = "", string? initialImagePreviewUrl = null)
    {
        _hadInitialImage = initialImagePreviewUrl is not null;

        Text = title;
        ClientSize = new Size(420, 330);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = new Font("Segoe UI", 12F);

        var lblName = new Label { Text = "Category Name", Location = new Point(20, 20), Size = new Size(360, 28) };
        // Matches Categories.CategoryName NVARCHAR(20) in schema.sql - same
        // reasoning as FormTextInputDialog's maxLength parameter.
        _txtName = new TextBox { Text = initialName, Location = new Point(20, 52), Size = new Size(360, 36), Font = new Font("Segoe UI", 14F), MaxLength = 20 };

        var lblPhoto = new Label { Text = "Photo", Location = new Point(20, 96), Size = new Size(360, 28) };
        _picPreview = new PictureBox
        {
            Location = new Point(20, 128),
            Size = new Size(100, 100),
            BorderStyle = BorderStyle.FixedSingle,
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.WhiteSmoke
        };
        if (initialImagePreviewUrl is not null)
        {
            try { _picPreview.LoadAsync(initialImagePreviewUrl); } catch { /* best-effort preview only */ }
        }

        var btnChooseImage = new Button { Text = "Choose Image...", Location = new Point(134, 128), Size = new Size(246, 40), Font = new Font("Segoe UI", 11F) };
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

        _btnRemoveImage = new Button { Text = "Remove Photo", Location = new Point(134, 174), Size = new Size(246, 36), Font = new Font("Segoe UI", 10F), Enabled = _hadInitialImage };
        _btnRemoveImage.Click += (_, _) =>
        {
            SelectedImagePath = null;
            RemoveImageRequested = true;
            _picPreview.Image = null;
            _picPreview.ImageLocation = null;
            _btnRemoveImage.Enabled = false;
        };

        var btnSave = new Button { Text = "Save", Location = new Point(20, 252), Size = new Size(170, 50), Font = new Font("Segoe UI", 12F, FontStyle.Bold), DialogResult = DialogResult.OK };
        var btnCancel = new Button { Text = "Cancel", Location = new Point(210, 252), Size = new Size(170, 50), Font = new Font("Segoe UI", 12F), DialogResult = DialogResult.Cancel };

        Controls.Add(lblName);
        Controls.Add(_txtName);
        Controls.Add(lblPhoto);
        Controls.Add(_picPreview);
        Controls.Add(btnChooseImage);
        Controls.Add(_btnRemoveImage);
        Controls.Add(btnSave);
        Controls.Add(btnCancel);

        AcceptButton = btnSave;
        CancelButton = btnCancel;
        Shown += (_, _) => { _txtName.Focus(); _txtName.SelectAll(); };
    }

    public bool IsValid => CategoryName.Length > 0;
}
