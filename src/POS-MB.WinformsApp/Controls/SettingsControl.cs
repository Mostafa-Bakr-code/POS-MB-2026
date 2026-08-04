using System.Globalization;
using POS_MB.Printing;
using POS_MB.WinformsApp.Api;
using POS_MB.WinformsApp.Dialogs;

namespace POS_MB.WinformsApp.Controls;

public class SettingsControl : UserControl
{
    private const string DefaultTaxRateKey = "DefaultTaxRate";
    private const decimal FallbackTaxRate = 14.00m;
    private const string TimeZoneOffsetKey = "TimeZoneOffsetHours";
    private const decimal FallbackTimeZoneOffset = 0m;

    private readonly ApiClient _apiClient = new();
    private readonly NumericUpDown _numTaxRate;
    private readonly NumericUpDown _numTimeZoneOffset;
    private readonly Button _btnSaveShared;
    private readonly Label _lblSharedStatus;

    // Printer fields are saved to a local file on THIS machine only (see
    // PrinterSettings.Load/Save), never the shared database - kept in the same
    // screen as the settings above for convenience, but visually separated and
    // saved independently so it's clear they don't apply to other terminals.
    private readonly TextBox _txtClientIp;
    private readonly NumericUpDown _numClientPort;
    private readonly TextBox _txtKitchenIp;
    private readonly NumericUpDown _numKitchenPort;
    private readonly CheckBox _chkShowOrderTime;
    private readonly ComboBox _cboTaxDisplayMode;
    private readonly NumericUpDown _numClientFontSize;
    private readonly NumericUpDown _numKitchenFontSize;
    private readonly Button _btnSavePrinters;
    private readonly Label _lblPrinterStatus;

    public SettingsControl()
    {
        Font = new Font("Segoe UI", 12F);
        Padding = new Padding(20);
        AutoScroll = true;

        var lblTitle = new Label
        {
            Text = "Settings",
            Dock = DockStyle.Top,
            Height = 40,
            Font = new Font("Segoe UI", 16F, FontStyle.Bold)
        };

        var lblSharedSection = new Label
        {
            Text = "Shared Settings (applies to every terminal)",
            Location = new Point(20, 60),
            Size = new Size(500, 26),
            Font = new Font("Segoe UI", 12F, FontStyle.Bold)
        };

        var lblTaxRate = new Label
        {
            Text = "Default Tax Rate (%) - applied to newly created items unless overridden",
            Location = new Point(20, 92),
            Size = new Size(500, 28)
        };

        _numTaxRate = new NumericUpDown
        {
            Location = new Point(20, 124),
            Size = new Size(150, 40),
            Font = new Font("Segoe UI", 14F),
            DecimalPlaces = 2,
            Minimum = 0,
            Maximum = 100,
            Increment = 0.5m
        };

        var lblTimeZoneOffset = new Label
        {
            Text = "Time Zone Offset (hours from UTC) - used so reports and order history match your local calendar day",
            Location = new Point(20, 182),
            Size = new Size(500, 28)
        };

        _numTimeZoneOffset = new NumericUpDown
        {
            Location = new Point(20, 214),
            Size = new Size(150, 40),
            Font = new Font("Segoe UI", 14F),
            DecimalPlaces = 1,
            Minimum = -12,
            Maximum = 14,
            Increment = 1m
        };

        // On its own row below both fields (not beside either one) so it's
        // visually obvious this single button saves the whole section, not just
        // whichever field happens to be next to it.
        _btnSaveShared = new Button
        {
            Text = "Save Tax Rate && Time Zone",
            Location = new Point(20, 264),
            Size = new Size(280, 40),
            Font = new Font("Segoe UI", 12F, FontStyle.Bold)
        };
        _btnSaveShared.Click += async (_, _) => await SaveSharedAsync();

        _lblSharedStatus = new Label
        {
            Location = new Point(20, 312),
            Size = new Size(500, 28),
            ForeColor = Color.Green
        };

        var lblPrinterSection = new Label
        {
            Text = "Printer Settings (this PC only)",
            Location = new Point(20, 360),
            Size = new Size(500, 26),
            Font = new Font("Segoe UI", 12F, FontStyle.Bold)
        };

        var lblClient = new Label { Text = "Client Receipt Printer IP", Location = new Point(20, 392), Size = new Size(220, 28) };
        _txtClientIp = new TextBox { Location = new Point(20, 424), Size = new Size(220, 36), Font = new Font("Segoe UI", 14F) };
        var lblClientPort = new Label { Text = "Port", Location = new Point(250, 392), Size = new Size(100, 28) };
        _numClientPort = new NumericUpDown { Location = new Point(250, 424), Size = new Size(100, 36), Minimum = 1, Maximum = 65535, Value = 9100, Font = new Font("Segoe UI", 14F) };
        var btnPreviewClient = new Button { Text = "Preview", Location = new Point(360, 424), Size = new Size(110, 36), Font = new Font("Segoe UI", 11F) };
        btnPreviewClient.Click += (_, _) => ShowPreview(isClient: true);
        var btnTestClient = new Button { Text = "Test Print", Location = new Point(480, 424), Size = new Size(130, 36), Font = new Font("Segoe UI", 11F) };
        btnTestClient.Click += async (_, _) => await TestPrintAsync(_txtClientIp.Text, (int)_numClientPort.Value, isClient: true);

        var lblKitchen = new Label { Text = "Kitchen Printer IP", Location = new Point(20, 472), Size = new Size(220, 28) };
        _txtKitchenIp = new TextBox { Location = new Point(20, 504), Size = new Size(220, 36), Font = new Font("Segoe UI", 14F) };
        var lblKitchenPort = new Label { Text = "Port", Location = new Point(250, 472), Size = new Size(100, 28) };
        _numKitchenPort = new NumericUpDown { Location = new Point(250, 504), Size = new Size(100, 36), Minimum = 1, Maximum = 65535, Value = 9100, Font = new Font("Segoe UI", 14F) };
        var btnPreviewKitchen = new Button { Text = "Preview", Location = new Point(360, 504), Size = new Size(110, 36), Font = new Font("Segoe UI", 11F) };
        btnPreviewKitchen.Click += (_, _) => ShowPreview(isClient: false);
        var btnTestKitchen = new Button { Text = "Test Print", Location = new Point(480, 504), Size = new Size(130, 36), Font = new Font("Segoe UI", 11F) };
        btnTestKitchen.Click += async (_, _) => await TestPrintAsync(_txtKitchenIp.Text, (int)_numKitchenPort.Value, isClient: false);

        var lblReceiptOptions = new Label
        {
            Text = "Customer Receipt Options",
            Location = new Point(20, 552),
            Size = new Size(400, 26),
            Font = new Font("Segoe UI", 11F, FontStyle.Bold)
        };

        // Off by default (see PrinterSettings) - an exact order timestamp on the
        // customer's own copy invites disputes over how long an order took.
        // Kept toggleable here rather than removed outright, in case it's ever
        // wanted back.
        _chkShowOrderTime = new CheckBox
        {
            Text = "Show order time on customer receipt",
            Location = new Point(20, 584),
            Size = new Size(400, 28)
        };

        var lblTaxDisplayMode = new Label { Text = "VAT display on customer receipt", Location = new Point(20, 616), Size = new Size(300, 28) };
        _cboTaxDisplayMode = new ComboBox
        {
            Location = new Point(20, 648),
            Size = new Size(260, 36),
            Font = new Font("Segoe UI", 12F),
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        // Order matches the TaxDisplayMode enum values (None=0, TotalOnly=1,
        // PerItem=2) so SelectedIndex can be cast directly to the enum.
        _cboTaxDisplayMode.Items.AddRange(["Don't show VAT", "VAT on total only", "VAT on every item"]);
        _cboTaxDisplayMode.SelectedIndex = (int)TaxDisplayMode.PerItem;

        var lblClientFontSize = new Label { Text = "Client receipt font size (1 = normal, up to 8)", Location = new Point(20, 696), Size = new Size(400, 28) };
        _numClientFontSize = new NumericUpDown
        {
            Location = new Point(20, 728),
            Size = new Size(100, 36),
            Font = new Font("Segoe UI", 14F),
            Minimum = 1,
            Maximum = 8,
            Value = 1
        };

        var lblKitchenFontSize = new Label { Text = "Kitchen ticket font size (1 = normal, up to 8)", Location = new Point(20, 776), Size = new Size(400, 28) };
        _numKitchenFontSize = new NumericUpDown
        {
            Location = new Point(20, 808),
            Size = new Size(100, 36),
            Font = new Font("Segoe UI", 14F),
            Minimum = 1,
            Maximum = 8,
            Value = 2
        };

        // Same reasoning as the shared-settings Save button above - on its own
        // row below both printer fields, not beside either one.
        _btnSavePrinters = new Button
        {
            Text = "Save Printer Settings",
            Location = new Point(20, 856),
            Size = new Size(220, 40),
            Font = new Font("Segoe UI", 12F, FontStyle.Bold)
        };
        _btnSavePrinters.Click += (_, _) => SavePrinters();

        _lblPrinterStatus = new Label
        {
            Location = new Point(20, 904),
            Size = new Size(500, 28),
            ForeColor = Color.Green
        };

        Controls.Add(lblTitle);
        Controls.Add(lblSharedSection);
        Controls.Add(lblTaxRate);
        Controls.Add(_numTaxRate);
        Controls.Add(lblTimeZoneOffset);
        Controls.Add(_numTimeZoneOffset);
        Controls.Add(_btnSaveShared);
        Controls.Add(_lblSharedStatus);
        Controls.Add(lblPrinterSection);
        Controls.Add(lblClient);
        Controls.Add(_txtClientIp);
        Controls.Add(lblClientPort);
        Controls.Add(_numClientPort);
        Controls.Add(btnPreviewClient);
        Controls.Add(btnTestClient);
        Controls.Add(lblKitchen);
        Controls.Add(_txtKitchenIp);
        Controls.Add(lblKitchenPort);
        Controls.Add(_numKitchenPort);
        Controls.Add(btnPreviewKitchen);
        Controls.Add(btnTestKitchen);
        Controls.Add(lblReceiptOptions);
        Controls.Add(_chkShowOrderTime);
        Controls.Add(lblTaxDisplayMode);
        Controls.Add(_cboTaxDisplayMode);
        Controls.Add(lblClientFontSize);
        Controls.Add(_numClientFontSize);
        Controls.Add(lblKitchenFontSize);
        Controls.Add(_numKitchenFontSize);
        Controls.Add(_btnSavePrinters);
        Controls.Add(_lblPrinterStatus);

        Load += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        var value = await _apiClient.GetSettingValueAsync(DefaultTaxRateKey);

        _numTaxRate.Value = value is not null && decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var rate)
            ? rate
            : FallbackTaxRate;

        var offsetValue = await _apiClient.GetSettingValueAsync(TimeZoneOffsetKey);

        _numTimeZoneOffset.Value = offsetValue is not null && decimal.TryParse(offsetValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var offset)
            ? offset
            : FallbackTimeZoneOffset;

        var printerSettings = PrinterSettings.Load();
        _txtClientIp.Text = printerSettings.ClientPrinterIp;
        _numClientPort.Value = printerSettings.ClientPrinterPort;
        _txtKitchenIp.Text = printerSettings.KitchenPrinterIp;
        _numKitchenPort.Value = printerSettings.KitchenPrinterPort;
        _chkShowOrderTime.Checked = printerSettings.ShowOrderTimeOnReceipt;
        _cboTaxDisplayMode.SelectedIndex = (int)printerSettings.TaxDisplayMode;
        _numClientFontSize.Value = printerSettings.ClientReceiptFontSize;
        _numKitchenFontSize.Value = printerSettings.KitchenTicketFontSize;
    }

    private async Task SaveSharedAsync()
    {
        _lblSharedStatus.Text = "";
        _btnSaveShared.Enabled = false;
        try
        {
            await _apiClient.SetSettingValueAsync(DefaultTaxRateKey, _numTaxRate.Value.ToString(CultureInfo.InvariantCulture));
            await _apiClient.SetSettingValueAsync(TimeZoneOffsetKey, _numTimeZoneOffset.Value.ToString(CultureInfo.InvariantCulture));
            _lblSharedStatus.ForeColor = Color.Green;
            _lblSharedStatus.Text = "Saved.";
        }
        catch (Exception ex)
        {
            _lblSharedStatus.ForeColor = Color.Red;
            _lblSharedStatus.Text = $"Could not save: {ex.Message}";
        }
        finally
        {
            _btnSaveShared.Enabled = true;
        }
    }

    private void SavePrinters()
    {
        var settings = new PrinterSettings
        {
            ClientPrinterIp = _txtClientIp.Text.Trim(),
            ClientPrinterPort = (int)_numClientPort.Value,
            KitchenPrinterIp = _txtKitchenIp.Text.Trim(),
            KitchenPrinterPort = (int)_numKitchenPort.Value,
            ShowOrderTimeOnReceipt = _chkShowOrderTime.Checked,
            TaxDisplayMode = (TaxDisplayMode)_cboTaxDisplayMode.SelectedIndex,
            ClientReceiptFontSize = (int)_numClientFontSize.Value,
            KitchenTicketFontSize = (int)_numKitchenFontSize.Value
        };
        settings.Save();

        _lblPrinterStatus.ForeColor = Color.Green;
        _lblPrinterStatus.Text = "Saved.";
    }

    private async Task TestPrintAsync(string ip, int port, bool isClient)
    {
        if (string.IsNullOrWhiteSpace(ip))
        {
            _lblPrinterStatus.ForeColor = Color.Red;
            _lblPrinterStatus.Text = "Enter an IP address first.";
            return;
        }

        _lblPrinterStatus.ForeColor = Color.Black;
        _lblPrinterStatus.Text = "Printing test receipt...";

        try
        {
            var bytes = isClient
                ? ReceiptBuilder.BuildCustomerReceipt(SampleOrder(), _chkShowOrderTime.Checked, (TaxDisplayMode)_cboTaxDisplayMode.SelectedIndex, (int)_numClientFontSize.Value)
                : ReceiptBuilder.BuildKitchenTicket(SampleOrder(), (int)_numKitchenFontSize.Value);

            await new NetworkReceiptPrinter(ip, port).PrintAsync(bytes);

            _lblPrinterStatus.ForeColor = Color.Green;
            _lblPrinterStatus.Text = "Test print sent successfully.";
        }
        catch (Exception ex)
        {
            _lblPrinterStatus.ForeColor = Color.Red;
            _lblPrinterStatus.Text = $"Could not reach printer: {ex.Message}";
        }
    }

    // No physical printer needed to check this - shows exactly what
    // ReceiptBuilder would send, rendered as plain monospace text. Uses whatever
    // is currently checked on screen (even if not yet saved) so you can try
    // different combinations before committing to one.
    private void ShowPreview(bool isClient)
    {
        var text = isClient
            ? ReceiptBuilder.PreviewCustomerReceipt(SampleOrder(), _chkShowOrderTime.Checked, (TaxDisplayMode)_cboTaxDisplayMode.SelectedIndex, (int)_numClientFontSize.Value)
            : ReceiptBuilder.PreviewKitchenTicket(SampleOrder(), (int)_numKitchenFontSize.Value);

        using var dialog = new FormReceiptPreviewDialog(
            isClient ? "Client Receipt Preview" : "Kitchen Ticket Preview", text);
        dialog.ShowDialog(this);
    }

    // Hand-verifiable round numbers (114 @ 14% tax = exactly 100 excl. tax + 14
    // tax, same convention as the automated test suite) plus two separately
    // placed Hotdogs with different comments, to prove same-name items never get
    // merged - each keeps its own line and its own comment.
    private static ReceiptOrder SampleOrder() => new(
        1234, DateTime.Now,
        [
            new ReceiptItem("Hotdog", 1, 114m, 14m, "no ketchup"),
            new ReceiptItem("Hotdog", 1, 114m, 14m, "extra mustard"),
            new ReceiptItem("Marghreta Pizza", 2, 171m, 14m, "extra cheese"),
            new ReceiptItem("Shawerma", 3, 57m, 14m, "no garlic sauce")
        ],
        114m + 114m + 2 * 171m + 3 * 57m, false);
}
