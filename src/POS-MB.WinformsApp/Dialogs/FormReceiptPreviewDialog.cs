namespace POS_MB.WinformsApp.Dialogs;

// Shows what a receipt would look like on paper - a monospace rendering of the
// exact same content that would be sent to a real printer (see
// ReceiptBuilder.Preview*), so the layout can be checked/adjusted without needing
// physical printer hardware at all. **bold** markers and centering are shown as
// plain text approximations, not pixel-perfect, but the content and order is exact.
public class FormReceiptPreviewDialog : Form
{
    public FormReceiptPreviewDialog(string title, string previewText)
    {
        Text = title;
        ClientSize = new Size(420, 560);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        var txtPreview = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 11F),
            BackColor = Color.White,
            Text = previewText,
            Padding = new Padding(10)
        };

        var btnClose = new Button
        {
            Text = "Close",
            Dock = DockStyle.Bottom,
            Height = 50,
            Font = new Font("Segoe UI", 12F, FontStyle.Bold),
            DialogResult = DialogResult.OK
        };

        Controls.Add(txtPreview);
        Controls.Add(btnClose);

        AcceptButton = btnClose;
        CancelButton = btnClose;
    }
}
