using POS_MB.WinformsApp.Models;

namespace POS_MB.WinformsApp.Dialogs;

public class FormOrderDetailDialog : Form
{
    public FormOrderDetailDialog(OrderDto order, Dictionary<int, string> itemNamesById, string? cashierName)
    {
        Text = $"Order #{order.SerialNumber ?? order.OrderId}";
        Size = new Size(560, 560);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = new Font("Segoe UI", 12F);

        var lblHeader = new Label
        {
            Dock = DockStyle.Top,
            Height = 150,
            Padding = new Padding(20, 15, 20, 0),
            Text =
                $"Date: {order.Date:yyyy-MM-dd HH:mm}\n" +
                $"Source: {order.OrderSource}{(cashierName is null ? "" : $"  (Cashier: {cashierName})")}\n" +
                $"Status: {order.Status}{(order.IsComplimentary ? "  (Complimentary)" : "")}\n" +
                $"Total: {order.Total:0.00}"
        };

        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AutoGenerateColumns = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            RowTemplate = { Height = 36 },
            Font = new Font("Segoe UI", 11F)
        };
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Item", Width = 180 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Qty", Width = 60 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Unit Price", Width = 100 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Line Total", Width = 100 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Comment", Width = 110 });

        foreach (var line in order.Items)
        {
            var name = itemNamesById.GetValueOrDefault(line.ItemId, $"Item #{line.ItemId}");
            grid.Rows.Add(name, line.Quantity, line.Price.ToString("0.00"), line.TotalItemsPrice.ToString("0.00"), line.Comment ?? "");
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
        Controls.Add(lblHeader);

        AcceptButton = btnClose;
        CancelButton = btnClose;
    }
}
