namespace POS_MB.Printing;

// Two different layouts for the same order - the customer cares about prices and
// a total, the kitchen only cares about what to make and any special comments.
// Both the real ESC/POS bytes and the on-screen preview (see EscPosDocument) come
// from these same *Document methods, so there's no separate "preview logic" that
// could ever drift out of sync with what actually prints.
public static class ReceiptBuilder
{
    // showOrderTime/showTaxBreakdown are opt-in customer-receipt display choices
    // (see PrinterSettings) - both real content that always gets computed and
    // stored correctly regardless, just optionally hidden from the customer's
    // own copy. Comments are never shown to the customer at all (no toggle) -
    // they're kitchen-only information (see KitchenTicketDocument).
    public static byte[] BuildCustomerReceipt(ReceiptOrder order, bool showOrderTime, bool showTaxBreakdown) =>
        CustomerReceiptDocument(order, showOrderTime, showTaxBreakdown).ToBytes();
    public static string PreviewCustomerReceipt(ReceiptOrder order, bool showOrderTime, bool showTaxBreakdown) =>
        CustomerReceiptDocument(order, showOrderTime, showTaxBreakdown).ToPreviewText();

    public static byte[] BuildKitchenTicket(ReceiptOrder order) => KitchenTicketDocument(order).ToBytes();
    public static string PreviewKitchenTicket(ReceiptOrder order) => KitchenTicketDocument(order).ToPreviewText();

    private static EscPosDocument CustomerReceiptDocument(ReceiptOrder order, bool showOrderTime, bool showTaxBreakdown)
    {
        var doc = new EscPosDocument()
            .Center().DoubleHeight(true).Bold(true)
            .Line("Dimashk Street")
            .DoubleHeight(false).Bold(false)
            .Line($"Order #{order.SerialNumber}");

        if (showOrderTime)
            doc.Line(order.LocalDate.ToString("yyyy-MM-dd HH:mm"));

        doc.Left().Divider();

        if (order.IsComplimentary)
            doc.Center().Bold(true).Line("*** COMPLIMENTARY ***").Bold(false).Left().Divider();

        var totalTax = 0m;

        foreach (var item in order.Items)
        {
            var lineTotalInclTax = item.Price * item.Quantity;
            doc.Line($"{item.Quantity} x {item.Name}");

            if (showTaxBreakdown)
            {
                // Price is tax-inclusive, so the tax-exclusive amount is derived
                // by dividing it back out - same formula used everywhere else in
                // the app (reports, etc.) so this always agrees with them.
                var lineTotalExclTax = lineTotalInclTax / (1 + item.TaxRate / 100);
                var lineTax = lineTotalInclTax - lineTotalExclTax;
                totalTax += lineTax;

                doc.Line($"  Price: {lineTotalExclTax,8:0.00}");
                doc.Line($"  Tax:   {lineTax,8:0.00}");
                doc.Line($"  Total: {lineTotalInclTax,8:0.00}");
            }
            else
            {
                doc.Line($"  {lineTotalInclTax,30:0.00}");
            }
        }

        doc.Divider();

        if (showTaxBreakdown)
        {
            doc.Line($"Subtotal: {(order.Total - totalTax),8:0.00}")
                .Line($"Tax:      {totalTax,8:0.00}");
        }

        doc.Bold(true)
            .Line($"Total:    {order.Total,8:0.00}")
            .Bold(false)
            .Feed()
            .Cut();

        return doc;
    }

    private static EscPosDocument KitchenTicketDocument(ReceiptOrder order)
    {
        var doc = new EscPosDocument()
            .Center().DoubleHeight(true).Bold(true)
            .Line($"KITCHEN - Order #{order.SerialNumber}")
            .DoubleHeight(false)
            .Line(order.LocalDate.ToString("yyyy-MM-dd HH:mm"))
            .Bold(false).Left()
            .Divider();

        if (order.IsComplimentary)
            doc.Center().Bold(true).Line("*** COMPLIMENTARY ***").Bold(false).Left().Divider();

        foreach (var item in order.Items)
        {
            doc.Bold(true).DoubleHeight(true)
                .Line($"{item.Quantity} x {item.Name}")
                .DoubleHeight(false).Bold(false);

            if (!string.IsNullOrWhiteSpace(item.Comment))
                doc.Bold(true).Line($">> {item.Comment}").Bold(false);

            doc.NewLine();
        }

        doc.Feed().Cut();

        return doc;
    }
}
