namespace POS_MB.WinformsApp.Dialogs;

// Small reusable modal for "enter a name" style edits (category name, item name, etc.)
public class FormTextInputDialog : Form
{
    private readonly TextBox _txtValue;

    public string Value => _txtValue.Text.Trim();

    // maxLength should match the target database column's size (e.g. NVARCHAR(50))
    // - without it, nothing stops the user from typing more than the database can
    // store, which surfaces as an unhelpful raw 500 error when saving instead of
    // being caught here where it can't happen at all.
    public FormTextInputDialog(string title, string label, string initialValue = "", int maxLength = 100)
    {
        Text = title;
        ClientSize = new Size(420, 190);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = new Font("Segoe UI", 12F);

        var lbl = new Label { Text = label, Location = new Point(20, 20), Size = new Size(360, 28) };
        _txtValue = new TextBox { Text = initialValue, Location = new Point(20, 52), Size = new Size(360, 36), Font = new Font("Segoe UI", 14F), MaxLength = maxLength };

        var btnSave = new Button
        {
            Text = "Save",
            Location = new Point(20, 110),
            Size = new Size(170, 50),
            Font = new Font("Segoe UI", 12F, FontStyle.Bold),
            DialogResult = DialogResult.OK
        };
        var btnCancel = new Button
        {
            Text = "Cancel",
            Location = new Point(210, 110),
            Size = new Size(170, 50),
            Font = new Font("Segoe UI", 12F),
            DialogResult = DialogResult.Cancel
        };

        Controls.Add(lbl);
        Controls.Add(_txtValue);
        Controls.Add(btnSave);
        Controls.Add(btnCancel);

        AcceptButton = btnSave;
        CancelButton = btnCancel;
        Shown += (_, _) => { _txtValue.Focus(); _txtValue.SelectAll(); };
    }
}
