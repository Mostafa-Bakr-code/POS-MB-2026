using System.Globalization;
using POS_MB.Cashier.Api;
using POS_MB.Cashier.Controls;
using POS_MB.Printing;

namespace POS_MB.Cashier.Pages;

// Replaces SettingsControl - shared settings (synced to the server, apply
// to every terminal) plus this device's own local-only printer settings
// (PrinterSettings.Load()/Save(), now MAUI-safe via
// PrinterSettings.BaseDirectoryProvider set once in MauiProgram.cs). Test
// print / preview / diagnostic actions call straight into
// ReceiptBuilder/NetworkReceiptPrinter, unchanged from every other screen
// that already exercises them (OrderTakingPage, KitchenTicketPrintService).
public partial class SettingsPage : ContentPage
{
    private const string DefaultTaxRateKey = "DefaultTaxRate";
    private const decimal FallbackTaxRate = 14.00m;
    private const string TimeZoneOffsetKey = "TimeZoneOffsetHours";
    private const decimal FallbackTimeZoneOffset = 0m;

    // Matches clsOrderBusiness.MobileOrderAutoCancelMinutesSettingKey and
    // OrderStatusPage's own copy - duplicated deliberately, same reasoning
    // as every other setting key this app keeps its own copy of.
    private const string AutoCancelMinutesKey = "MobileOrderAutoCancelMinutes";
    private const decimal FallbackAutoCancelMinutes = 10m;

    private readonly ApiClient _apiClient = new();

    public SettingsPage()
    {
        InitializeComponent();
        TaxDisplayModePicker.SelectedIndex = (int)TaxDisplayMode.PerItem;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        var taxValue = await _apiClient.GetSettingValueAsync(DefaultTaxRateKey);
        TaxRateStepper.Value = taxValue is not null && decimal.TryParse(taxValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var rate)
            ? rate
            : FallbackTaxRate;

        var offsetValue = await _apiClient.GetSettingValueAsync(TimeZoneOffsetKey);
        TimeZoneOffsetStepper.Value = offsetValue is not null && decimal.TryParse(offsetValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var offset)
            ? offset
            : FallbackTimeZoneOffset;

        var autoCancelValue = await _apiClient.GetSettingValueAsync(AutoCancelMinutesKey);
        AutoCancelMinutesStepper.Value = autoCancelValue is not null && decimal.TryParse(autoCancelValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var autoCancelMinutes)
            ? autoCancelMinutes
            : FallbackAutoCancelMinutes;

        var printerSettings = PrinterSettings.Load();
        ClientIpEntry.Text = printerSettings.ClientPrinterIp;
        ClientPortStepper.Value = printerSettings.ClientPrinterPort;
        KitchenIpEntry.Text = printerSettings.KitchenPrinterIp;
        KitchenPortStepper.Value = printerSettings.KitchenPrinterPort;
        WrapAtStepper.Value = printerSettings.ReceiptOrderNumberWrapAt;
        ShowOrderTimeCheck.IsChecked = printerSettings.ShowOrderTimeOnReceipt;
        TaxDisplayModePicker.SelectedIndex = (int)printerSettings.TaxDisplayMode;
        ClientFontSizeStepper.Value = printerSettings.ClientReceiptFontSize;
        KitchenFontSizeStepper.Value = printerSettings.KitchenTicketFontSize;
    }

    private async void OnSaveSharedClicked(object? sender, EventArgs e)
    {
        SharedStatusLabel.Text = "";
        SaveSharedButton.IsEnabled = false;
        try
        {
            var (taxOk, taxError) = await _apiClient.SetSettingValueAsync(DefaultTaxRateKey, TaxRateStepper.Value.ToString(CultureInfo.InvariantCulture));
            var (offsetOk, offsetError) = await _apiClient.SetSettingValueAsync(TimeZoneOffsetKey, TimeZoneOffsetStepper.Value.ToString(CultureInfo.InvariantCulture));
            var (autoCancelOk, autoCancelError) = await _apiClient.SetSettingValueAsync(AutoCancelMinutesKey, ((int)AutoCancelMinutesStepper.Value).ToString(CultureInfo.InvariantCulture));

            if (taxOk && offsetOk && autoCancelOk)
            {
                SharedStatusLabel.TextColor = Color.FromArgb("#28C882");
                SharedStatusLabel.Text = "Saved.";
            }
            else
            {
                SharedStatusLabel.TextColor = Color.FromArgb("#FF8C8C");
                SharedStatusLabel.Text = $"Could not save: {taxError ?? offsetError ?? autoCancelError}";
            }
        }
        finally
        {
            SaveSharedButton.IsEnabled = true;
        }
    }

    private void OnSavePrintersClicked(object? sender, EventArgs e)
    {
        var settings = new PrinterSettings
        {
            ClientPrinterIp = ClientIpEntry.Text?.Trim() ?? "",
            ClientPrinterPort = (int)ClientPortStepper.Value,
            KitchenPrinterIp = KitchenIpEntry.Text?.Trim() ?? "",
            KitchenPrinterPort = (int)KitchenPortStepper.Value,
            ReceiptOrderNumberWrapAt = (int)WrapAtStepper.Value,
            ShowOrderTimeOnReceipt = ShowOrderTimeCheck.IsChecked,
            TaxDisplayMode = (TaxDisplayMode)TaxDisplayModePicker.SelectedIndex,
            ClientReceiptFontSize = ClientFontSizeStepper.Value,
            KitchenTicketFontSize = KitchenFontSizeStepper.Value
        };
        settings.Save();

        PrinterStatusLabel.TextColor = Color.FromArgb("#28C882");
        PrinterStatusLabel.Text = "Saved.";
    }

    private async void OnTestClientClicked(object? sender, EventArgs e) =>
        await TestPrintAsync(ClientIpEntry.Text ?? "", (int)ClientPortStepper.Value, isClient: true);

    private async void OnTestKitchenClicked(object? sender, EventArgs e) =>
        await TestPrintAsync(KitchenIpEntry.Text ?? "", (int)KitchenPortStepper.Value, isClient: false);

    private async Task TestPrintAsync(string ip, int port, bool isClient)
    {
        if (string.IsNullOrWhiteSpace(ip))
        {
            PrinterStatusLabel.TextColor = Color.FromArgb("#FF8C8C");
            PrinterStatusLabel.Text = "Enter an IP address first.";
            return;
        }

        PrinterStatusLabel.TextColor = Colors.Black;
        PrinterStatusLabel.Text = "Printing test receipt...";

        try
        {
            var bytes = isClient
                ? ReceiptBuilder.BuildCustomerReceipt(SampleOrder(), ShowOrderTimeCheck.IsChecked, (TaxDisplayMode)TaxDisplayModePicker.SelectedIndex, ClientFontSizeStepper.Value)
                : ReceiptBuilder.BuildKitchenTicket(SampleOrder(), KitchenFontSizeStepper.Value);

            await new NetworkReceiptPrinter(ip, port).PrintAsync(bytes);

            PrinterStatusLabel.TextColor = Color.FromArgb("#28C882");
            PrinterStatusLabel.Text = "Test print sent successfully.";
        }
        catch (Exception ex)
        {
            PrinterStatusLabel.TextColor = Color.FromArgb("#FF8C8C");
            PrinterStatusLabel.Text = $"Could not reach printer: {ex.Message}";
        }
    }

    private async void OnDiagnosticTestClicked(object? sender, EventArgs e)
    {
        var ip = KitchenIpEntry.Text ?? "";
        if (string.IsNullOrWhiteSpace(ip))
        {
            PrinterStatusLabel.TextColor = Color.FromArgb("#FF8C8C");
            PrinterStatusLabel.Text = "Enter the kitchen printer's IP address first.";
            return;
        }

        PrinterStatusLabel.TextColor = Colors.Black;
        PrinterStatusLabel.Text = "Printing diagnostic test...";

        try
        {
            var bytes = ReceiptBuilder.BuildDiagnosticTest();
            await new NetworkReceiptPrinter(ip, (int)KitchenPortStepper.Value).PrintAsync(bytes);

            PrinterStatusLabel.TextColor = Color.FromArgb("#28C882");
            PrinterStatusLabel.Text = "Diagnostic test sent successfully.";
        }
        catch (Exception ex)
        {
            PrinterStatusLabel.TextColor = Color.FromArgb("#FF8C8C");
            PrinterStatusLabel.Text = $"Could not reach printer: {ex.Message}";
        }
    }

    // No physical printer needed to check this - shows exactly what
    // ReceiptBuilder would send, as plain monospace text, using whatever is
    // currently on screen (even if not yet saved) so different combinations
    // can be tried before committing to one.
    private async void OnPreviewClientClicked(object? sender, EventArgs e)
    {
        var text = ReceiptBuilder.PreviewCustomerReceipt(SampleOrder(), ShowOrderTimeCheck.IsChecked, (TaxDisplayMode)TaxDisplayModePicker.SelectedIndex, ClientFontSizeStepper.Value);
        await Navigation.PushModalAsync(new ReceiptPreviewPage("Client Receipt Preview", text));
    }

    private async void OnPreviewKitchenClicked(object? sender, EventArgs e)
    {
        var text = ReceiptBuilder.PreviewKitchenTicket(SampleOrder(), KitchenFontSizeStepper.Value);
        await Navigation.PushModalAsync(new ReceiptPreviewPage("Kitchen Ticket Preview", text));
    }

    // Hand-verifiable round numbers (114 @ 14% tax = exactly 100 excl. tax +
    // 14 tax) plus two separately placed Hotdogs with different comments,
    // to prove same-name items never get merged. One comment is Arabic
    // specifically so Test Print/Preview exercises the raster-image
    // rendering path (see EscPosDocument.RenderTextAsRaster) without a real
    // order first - ported verbatim from SettingsControl.SampleOrder.
    private static ReceiptOrder SampleOrder() => new(
        1234, DateTime.Now,
        [
            new ReceiptItem("Hotdog", 1, 114m, 14m, "بدون كاتشب"),
            new ReceiptItem("Hotdog", 1, 114m, 14m, "extra mustard"),
            new ReceiptItem("Marghreta Pizza", 2, 171m, 14m, "extra cheese"),
            new ReceiptItem("Shawerma", 3, 57m, 14m, "no garlic sauce")
        ],
        114m + 114m + 2 * 171m + 3 * 57m, false, "CASHIER");
}
