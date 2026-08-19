using POS_MB.WinformsApp.Models;
using POS_MB.WinformsApp.Session;

namespace POS_MB.WinformsApp.Dialogs;

public class FormOrderDetailDialog : Form
{
    public FormOrderDetailDialog(OrderDto order, Dictionary<int, string> itemNamesById)
    {
        var placedBy = order.CashierName is not null ? $"  (Cashier: {order.CashierName})"
            : order.StudentEmail is not null ? $"  (Student: {order.StudentEmail})"
            : "";

        var hasPaymentInfo = order.PaymobTransactionId is not null;
        var hasCancelInfo = order.CancelledBy is not null;

        Text = $"Order #{order.SerialNumber ?? order.OrderId}";
        ClientSize = new Size(600, 560 + (hasPaymentInfo ? 40 : 0) + (hasCancelInfo ? 30 : 0));
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = new Font("Segoe UI", 12F);

        // Only a Mobile order ever paid through Paymob has a transaction id -
        // this is the same id you'd search for on Paymob's own dashboard if
        // a student disputes a charge/refund, so it needs to be somewhere
        // staff can actually find it without asking a developer to check the
        // database. Blank for a Cashier order or one never paid (Cash on
        // pickup isn't a thing here, but a complimentary/manually-created
        // mobile order could still lack one).
        // Paymob creates a separate transaction record for the refund itself
        // (linked to the original charge via its own parent_transaction
        // field, not the same id) - Paymob's own confirmation email
        // references the refund's id, not the original charge's, so showing
        // only one of the two here would leave staff unable to match what
        // the email says against what this order shows.
        var paymentInfo = order.PaymobTransactionId is long transactionId
            ? $"\nPaid via Paymob (Transaction #{transactionId})" +
              (order.RefundedAt is DateTime refundedAt
                  ? $"\nRefunded: {AppSession.ToLocalDisplay(refundedAt):yyyy-MM-dd HH:mm}" +
                    (order.RefundTransactionId is long refundTransactionId ? $" (Refund Transaction #{refundTransactionId})" : "")
                  : "")
            : "";

        // Only a Cancelled order ever has this - tells staff apart, at a
        // glance, whether a student/staff member made the call or one of
        // the two automatic sweeps did (see clsOrderBusiness.CancelAsync).
        var cancelInfo = order.CancelledBy is string cancelledBy ? $"\nCancelled by: {cancelledBy}" : "";

        var lblHeader = new Label
        {
            Dock = DockStyle.Top,
            Height = 150 + (string.IsNullOrEmpty(paymentInfo) ? 0 : 40) + (string.IsNullOrEmpty(cancelInfo) ? 0 : 30),
            Padding = new Padding(20, 15, 20, 0),
            Text =
                $"Date: {AppSession.ToLocalDisplay(order.Date):yyyy-MM-dd HH:mm}\n" +
                $"Source: {order.OrderSource}{placedBy}\n" +
                $"Status: {order.Status}{(order.IsComplimentary ? "  (Complimentary)" : "")}\n" +
                $"Total: {order.Total:0.00}" +
                paymentInfo +
                cancelInfo
        };

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
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Item", FillWeight = 130, MinimumWidth = 150 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Qty", FillWeight = 50, MinimumWidth = 60 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Unit Price", FillWeight = 80, MinimumWidth = 100 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Line Total", FillWeight = 80, MinimumWidth = 100 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Comment", FillWeight = 90, MinimumWidth = 110 });

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
