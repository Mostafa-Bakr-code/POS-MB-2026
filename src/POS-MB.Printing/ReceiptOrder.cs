namespace POS_MB.Printing;

// This project's own small, self-contained model of "what goes on a receipt" -
// deliberately not the WinForms OrderDto/OrderItemDto. Keeps the dependency
// one-way (WinForms depends on Printing, never the reverse), so this project
// never needs to know anything about the rest of the app.
// Price is tax-INCLUSIVE (matches the rest of the app's convention - Items.Price
// is the catalog/charged price, TaxRate is the percentage baked into it, not on
// top of it). The tax-exclusive amount and the tax portion are derived from
// these two, not stored separately, so they can never disagree with each other.
public record ReceiptItem(string Name, int Quantity, decimal Price, decimal TaxRate, string? Comment);

public record ReceiptOrder(
    int SerialNumber,
    DateTime LocalDate,
    IReadOnlyList<ReceiptItem> Items,
    decimal Total,
    bool IsComplimentary,
    // A plain display label ("CASHIER"/"MOBILE"), not the WinForms
    // OrderSource enum itself - same reasoning as this whole model being
    // separate from OrderDto (see the file-level comment above): this
    // project stays decoupled from the rest of the app's types, so the
    // caller resolves the enum to text before constructing this.
    string SourceLabel);
