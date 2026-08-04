using System.Text.Json;

namespace POS_MB.Printing;

// Lives in a local JSON file on THIS machine only - never the shared database.
// Moving to a new PC or swapping a printer means editing this one file (or the
// values it holds), not touching any other machine or redeploying anything.
public class PrinterSettings
{
    public string ClientPrinterIp { get; set; } = "";
    public int ClientPrinterPort { get; set; } = 9100;

    public string KitchenPrinterIp { get; set; } = "";
    public int KitchenPrinterPort { get; set; } = 9100;

    // Customer-receipt-only options (the kitchen ticket always shows the time and
    // comments - the kitchen needs both). Off by default for time - showing an
    // exact order timestamp on the customer's own copy invites disputes over how
    // long it took, so it's opt-in, not opt-out.
    public bool ShowOrderTimeOnReceipt { get; set; }
    public TaxDisplayMode TaxDisplayMode { get; set; } = TaxDisplayMode.PerItem;

    // 1 = normal size, 2 = double, up to 8 (ESC/POS's own limit). The kitchen
    // ticket defaults larger than normal so it's easy to read at a glance while
    // cooking; the client receipt defaults to normal since nothing prompted a
    // bigger default there, but it's independently adjustable the same way.
    public int KitchenTicketFontSize { get; set; } = 2;
    public int ClientReceiptFontSize { get; set; } = 1;

    private static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "POS-MB", "printer-settings.json");

    public static PrinterSettings Load()
    {
        if (!File.Exists(FilePath)) return new PrinterSettings();

        var json = File.ReadAllText(FilePath);
        return JsonSerializer.Deserialize<PrinterSettings>(json) ?? new PrinterSettings();
    }

    public void Save()
    {
        var directory = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(FilePath, json);
    }
}
