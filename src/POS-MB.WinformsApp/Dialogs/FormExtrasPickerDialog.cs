using POS_MB.WinformsApp.Models;

namespace POS_MB.WinformsApp.Dialogs;

// Replaces the original ContextMenuStrip picker for OrderTakingControl's
// "+Extra" button - a grid of tiles (same look as the category/item tiles
// on the main order-taking screen) reads faster and is easier to tap
// accurately mid-rush than a vertical text list. FlowLayoutPanel wraps
// naturally once a row's tiles fill the available width - ClientSize below
// is sized so exactly 5 fit per row (160 tile + 16 margin = 176 each),
// so a cashier sees more extras at once without needing to scroll.
//
// Deliberately stays open across multiple taps instead of closing after
// one selection - adding three different extras to the same item used to
// mean reopening this dialog three separate times, which was slow mid-
// rush. Each tap fires ItemSelected immediately and the dialog only
// closes via its own "Done" button (or the caller's ShowDialog return).
public class FormExtrasPickerDialog : Form
{
    public event Action<ItemDto>? ItemSelected;

    public FormExtrasPickerDialog(string title, List<ItemDto> items)
    {
        Text = title;
        ClientSize = new Size(940, 420);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = new Font("Segoe UI", 12F);

        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Padding = new Padding(10)
        };

        foreach (var item in items)
        {
            var captured = item;
            var button = new Button
            {
                Text = $"{captured.ItemName}\n{captured.Price:0.00}",
                Width = 160,
                Height = 100,
                Margin = new Padding(8),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(233, 236, 239),
                Font = new Font("Segoe UI", 12F, FontStyle.Bold)
            };
            button.FlatAppearance.BorderSize = 1;
            button.Click += (_, _) => ItemSelected?.Invoke(captured);
            flow.Controls.Add(button);
        }

        var btnDone = new Button
        {
            Text = "Done",
            Dock = DockStyle.Bottom,
            Height = 50,
            Font = new Font("Segoe UI", 12F, FontStyle.Bold),
            DialogResult = DialogResult.OK
        };

        Controls.Add(flow);
        Controls.Add(btnDone);
        AcceptButton = btnDone;
        CancelButton = btnDone;
    }
}
