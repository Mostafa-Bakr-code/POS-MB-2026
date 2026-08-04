namespace POS_MB.Printing;

// Two different layouts for the same order - the customer cares about prices and
// a total, the kitchen only cares about what to make and any special comments.
// Both the real ESC/POS bytes and the on-screen preview (see EscPosDocument) come
// from these same *Document methods, so there's no separate "preview logic" that
// could ever drift out of sync with what actually prints.
public static class ReceiptBuilder
{
    // showOrderTime/taxDisplayMode are opt-in customer-receipt display choices
    // (see PrinterSettings) - both real content that always gets computed
    // correctly regardless, just optionally hidden/summarized on the customer's
    // own copy. Comments are never shown to the customer at all (no toggle) -
    // they're kitchen-only information (see KitchenTicketDocument).
    public static byte[] BuildCustomerReceipt(ReceiptOrder order, bool showOrderTime, TaxDisplayMode taxDisplayMode, int fontSize = 1) =>
        CustomerReceiptDocument(order, showOrderTime, taxDisplayMode, fontSize).ToBytes();
    public static string PreviewCustomerReceipt(ReceiptOrder order, bool showOrderTime, TaxDisplayMode taxDisplayMode, int fontSize = 1) =>
        CustomerReceiptDocument(order, showOrderTime, taxDisplayMode, fontSize).ToPreviewText();

    public static byte[] BuildKitchenTicket(ReceiptOrder order, int fontSize = 2) => KitchenTicketDocument(order, fontSize).ToBytes();
    public static string PreviewKitchenTicket(ReceiptOrder order, int fontSize = 2) => KitchenTicketDocument(order, fontSize).ToPreviewText();

    private static EscPosDocument CustomerReceiptDocument(ReceiptOrder order, bool showOrderTime, TaxDisplayMode taxDisplayMode, int fontSize)
    {
        var doc = new EscPosDocument()
            .Center().DoubleHeight(true).Bold(true)
            .Line("Dimashk Street")
            .DoubleHeight(false)
            .Size(fontSize, fontSize)
            .Line($"Order #{order.SerialNumber}")
            .Size(1, 1).Bold(false);

        if (showOrderTime)
            doc.Line(order.LocalDate.ToString("yyyy-MM-dd HH:mm"));

        doc.Left().Divider();

        if (order.IsComplimentary)
            doc.Center().Bold(true).Line("*** COMPLIMENTARY ***").Bold(false).Left().Divider();

        var totalTax = 0m;
        var distinctRates = order.Items.Select(i => i.TaxRate).Distinct().ToList();
        // Only meaningful to show "VAT 14%" when every item actually shares that
        // rate - if items differ, showing one rate would be misleading, so the
        // order-level VAT line falls back to a plain "VAT" label in that case.
        var uniformRateLabel = distinctRates.Count == 1 ? $"VAT {distinctRates[0]:0.##}%" : "VAT";

        foreach (var item in order.Items)
        {
            var lineTotalInclTax = item.Price * item.Quantity;
            doc.Bold(true).Size(fontSize, fontSize)
                .Line($"{item.Quantity} x {item.Name}")
                .Size(1, 1).Bold(false);

            // Price is tax-inclusive, so the tax-exclusive amount is derived by
            // dividing it back out - same formula used everywhere else in the
            // app (reports, etc.) so this always agrees with them.
            var lineTotalExclTax = lineTotalInclTax / (1 + item.TaxRate / 100);
            var lineTax = lineTotalInclTax - lineTotalExclTax;
            totalTax += lineTax;

            if (taxDisplayMode == TaxDisplayMode.PerItem)
            {
                doc.Line($"  Price:        {lineTotalExclTax,8:0.00}");
                doc.Line($"  VAT {item.TaxRate,4:0.##}%:    {lineTax,8:0.00}");
                doc.Line($"  Total:        {lineTotalInclTax,8:0.00}");
            }
            else
            {
                doc.Line($"  {lineTotalInclTax,30:0.00}");
            }
        }

        doc.Divider();

        if (taxDisplayMode is TaxDisplayMode.PerItem or TaxDisplayMode.TotalOnly)
        {
            doc.Line($"Subtotal: {(order.Total - totalTax),8:0.00}")
                .Line($"{uniformRateLabel}: {totalTax,8:0.00}");
        }

        doc.Bold(true)
            .Line($"Total:    {order.Total,8:0.00}")
            .Bold(false)
            .Feed()
            .Cut();

        return doc;
    }

    private static EscPosDocument KitchenTicketDocument(ReceiptOrder order, int fontSize)
    {
        var doc = new EscPosDocument()
            .Center().Size(fontSize, fontSize).Bold(true)
            .Line($"KITCHEN - Order #{order.SerialNumber}")
            .Size(1, 1)
            .Line(order.LocalDate.ToString("yyyy-MM-dd HH:mm"))
            .Bold(false).Left()
            .Divider();

        if (order.IsComplimentary)
            doc.Center().Bold(true).Line("*** COMPLIMENTARY ***").Bold(false).Left().Divider();

        foreach (var item in order.Items)
        {
            doc.Bold(true).Size(fontSize, fontSize)
                .Line($"{item.Quantity} x {item.Name}")
                .Size(1, 1).Bold(false);

            if (!string.IsNullOrWhiteSpace(item.Comment))
                doc.Bold(true).Line($">> {item.Comment}").Bold(false);

            doc.NewLine();
        }

        doc.Feed().Cut();

        return doc;
    }
}
