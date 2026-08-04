namespace POS_MB.Printing;

// Deliberately just "send these bytes somewhere" - keeps the receipt-building
// code (what to print) completely separate from how it physically gets to a
// printer, so a different transport could be swapped in later without touching
// anything that calls this.
public interface IReceiptPrinter
{
    Task PrintAsync(byte[] data);
}
