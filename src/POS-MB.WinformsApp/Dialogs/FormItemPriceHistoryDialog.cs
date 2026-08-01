using POS_MB.WinformsApp.Models;
using POS_MB.WinformsApp.Session;

namespace POS_MB.WinformsApp.Dialogs;

public class FormItemPriceHistoryDialog : Form
{
    public FormItemPriceHistoryDialog(string itemName, List<ItemPriceHistoryDto> history)
    {
        Text = $"Price History - {itemName}";
        ClientSize = new Size(560, 480);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = new Font("Segoe UI", 12F);

        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AutoGenerateColumns = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            RowTemplate = { Height = 36 },
            Font = new Font("Segoe UI", 11F)
        };
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Date", FillWeight = 110, MinimumWidth = 140 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Changed By", FillWeight = 100, MinimumWidth = 130 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Price", FillWeight = 110, MinimumWidth = 140 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Tax Rate", FillWeight = 90, MinimumWidth = 110 });

        if (history.Count == 0)
        {
            grid.Rows.Add("No price changes recorded yet.", "", "", "");
        }
        else
        {
            foreach (var entry in history)
            {
                grid.Rows.Add(
                    AppSession.ToLocalDisplay(entry.ChangedAt).ToString("yyyy-MM-dd HH:mm"),
                    entry.ChangedByUserName ?? "(unknown)",
                    $"{entry.OldPrice:0.00} -> {entry.NewPrice:0.00}",
                    $"{entry.OldTaxRate:0.00}% -> {entry.NewTaxRate:0.00}%");
            }
        }

        var btnClose = new Button
        {
            Text = "Close",
            Dock = DockStyle.Bottom,
            Height = 50,
            Font = new Font("Segoe UI", 12F, FontStyle.Bold),
            DialogResult = DialogResult.OK
        };

        Controls.Add(grid);
        Controls.Add(btnClose);

        AcceptButton = btnClose;
        CancelButton = btnClose;
    }
}
