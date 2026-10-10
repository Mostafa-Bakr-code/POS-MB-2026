using System.Globalization;
using POS_MB.Printing;
using POS_MB.WinformsApp.Api;
using POS_MB.WinformsApp.Dialogs;
using POS_MB.WinformsApp.Models;

namespace POS_MB.WinformsApp.Controls;

public class SettingsControl : UserControl
{
    private const string DefaultTaxRateKey = "DefaultTaxRate";
    private const decimal FallbackTaxRate = 14.00m;
    private const string TimeZoneOffsetKey = "TimeZoneOffsetHours";
    private const decimal FallbackTimeZoneOffset = 0m;
    // Matches clsOrderBusiness.MobileOrderAutoCancelMinutesSettingKey - duplicated
    // deliberately, not shared via a project reference, same reasoning as every
    // other setting key WinForms keeps its own copy of.
    private const string AutoCancelMinutesKey = "MobileOrderAutoCancelMinutes";
    private const decimal FallbackAutoCancelMinutes = 10m;

    // Matches the Cashier/Mobile apps' own copy of this key - the setting
    // that marks which category's items show up behind a cart line's
    // "+Extra" button.
    private const string ExtrasCategoryKey = "ExtrasCategoryId";

    private readonly ApiClient _apiClient = new();
    private readonly NumericUpDown _numTaxRate;
    private readonly NumericUpDown _numTimeZoneOffset;
    private readonly NumericUpDown _numAutoCancelMinutes;
    private readonly ComboBox _cboExtrasCategory;
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
    private readonly NumericUpDown _numOrderNumberWrapAt;
    private readonly ComboBox _cboDateTimeDisplay;
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

        var lblAutoCancelMinutes = new Label
        {
            Text = "Auto-cancel a mobile order if still unaccepted after this many minutes (safety net - see Order Status)",
            Location = new Point(20, 272),
            Size = new Size(500, 28)
        };

        _numAutoCancelMinutes = new NumericUpDown
        {
            Location = new Point(20, 304),
            Size = new Size(150, 40),
            Font = new Font("Segoe UI", 14F),
            Minimum = 1,
            Maximum = 240,
            Increment = 1m
        };

        var lblExtrasCategory = new Label
        {
            Text = "Extras category - which category's items show up behind a cart line's \"+Extra\" button (e.g. Extra Cheese, Extra Tomato)",
            Location = new Point(20, 354),
            Size = new Size(500, 28)
        };

        _cboExtrasCategory = new ComboBox
        {
            Location = new Point(20, 386),
            Size = new Size(260, 36),
            Font = new Font("Segoe UI", 12F),
            DropDownStyle = ComboBoxStyle.DropDownList,
            DisplayMember = "CategoryName"
        };

        // On its own row below every field (not beside any one of them) so it's
        // visually obvious this single button saves the whole section, not just
        // whichever field happens to be next to it.
        _btnSaveShared = new Button
        {
            Text = "Save Tax Rate && Time Zone && Auto-Cancel && Extras Category",
            Location = new Point(20, 432),
            Size = new Size(460, 40),
            Font = new Font("Segoe UI", 12F, FontStyle.Bold)
        };
        _btnSaveShared.Click += async (_, _) => await SaveSharedAsync();

        _lblSharedStatus = new Label
        {
            Location = new Point(20, 480),
            Size = new Size(500, 28),
            ForeColor = Color.Green
        };

        var lblPrinterSection = new Label
        {
            Text = "Printer Settings (this PC only)",
            Location = new Point(20, 528),
            Size = new Size(500, 26),
            Font = new Font("Segoe UI", 12F, FontStyle.Bold)
        };

        var lblClient = new Label { Text = "Client Receipt Printer IP", Location = new Point(20, 560), Size = new Size(220, 28) };
        _txtClientIp = new TextBox { Location = new Point(20, 592), Size = new Size(220, 36), Font = new Font("Segoe UI", 14F) };
        var lblClientPort = new Label { Text = "Port", Location = new Point(250, 560), Size = new Size(100, 28) };
        _numClientPort = new NumericUpDown { Location = new Point(250, 592), Size = new Size(100, 36), Minimum = 1, Maximum = 65535, Value = 9100, Font = new Font("Segoe UI", 14F) };
        var btnPreviewClient = new Button { Text = "Preview", Location = new Point(360, 592), Size = new Size(110, 36), Font = new Font("Segoe UI", 11F) };
        btnPreviewClient.Click += (_, _) => ShowPreview(isClient: true);
        var btnTestClient = new Button { Text = "Test Print", Location = new Point(480, 592), Size = new Size(130, 36), Font = new Font("Segoe UI", 11F) };
        btnTestClient.Click += async (_, _) => await TestPrintAsync(_txtClientIp.Text, (int)_numClientPort.Value, isClient: true);

        var lblKitchen = new Label { Text = "Kitchen Printer IP", Location = new Point(20, 640), Size = new Size(220, 28) };
        _txtKitchenIp = new TextBox { Location = new Point(20, 672), Size = new Size(220, 36), Font = new Font("Segoe UI", 14F) };
        var lblKitchenPort = new Label { Text = "Port", Location = new Point(250, 640), Size = new Size(100, 28) };
        _numKitchenPort = new NumericUpDown { Location = new Point(250, 672), Size = new Size(100, 36), Minimum = 1, Maximum = 65535, Value = 9100, Font = new Font("Segoe UI", 14F) };
        var btnPreviewKitchen = new Button { Text = "Preview", Location = new Point(360, 672), Size = new Size(110, 36), Font = new Font("Segoe UI", 11F) };
        btnPreviewKitchen.Click += (_, _) => ShowPreview(isClient: false);
        var btnTestKitchen = new Button { Text = "Test Print", Location = new Point(480, 672), Size = new Size(130, 36), Font = new Font("Segoe UI", 11F) };
        btnTestKitchen.Click += async (_, _) => await TestPrintAsync(_txtKitchenIp.Text, (int)_numKitchenPort.Value, isClient: false);

        var lblBothReceiptsSection = new Label
        {
            Text = "Both Receipts",
            Location = new Point(20, 720),
            Size = new Size(400, 26),
            Font = new Font("Segoe UI", 11F, FontStyle.Bold)
        };

        // Doesn't touch the real order number anywhere else (Order History,
        // reports, the cashier's own screen) - only what gets printed. 0 = print
        // the real number as-is; cancelling this later is just setting it back to
        // 0, no code change needed.
        var lblWrapAt = new Label { Text = "Wrap order number on receipts after (0 = don't wrap)", Location = new Point(20, 752), Size = new Size(400, 28) };
        _numOrderNumberWrapAt = new NumericUpDown
        {
            Location = new Point(20, 784),
            Size = new Size(120, 36),
            Font = new Font("Segoe UI", 14F),
            Minimum = 0,
            Maximum = 9999
        };

        var lblReceiptOptions = new Label
        {
            Text = "Customer Receipt Options",
            Location = new Point(20, 832),
            Size = new Size(400, 26),
            Font = new Font("Segoe UI", 11F, FontStyle.Bold)
        };

        // None by default (see PrinterSettings) - an exact order timestamp
        // on the customer's own copy invites disputes over how long an
        // order took. Was a plain on/off checkbox; a dropdown instead
        // lets the date and time be shown independently (date only, time
        // only, both, or neither).
        var lblDateTimeDisplay = new Label { Text = "Show on customer receipt", Location = new Point(20, 864), Size = new Size(300, 28) };
        _cboDateTimeDisplay = new ComboBox
        {
            Location = new Point(20, 896),
            Size = new Size(260, 36),
            Font = new Font("Segoe UI", 12F),
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        // Order matches the ReceiptDateTimeDisplayMode enum values (None=0,
        // TimeOnly=1, DateOnly=2, Both=3) so SelectedIndex can be cast
        // directly to the enum.
        _cboDateTimeDisplay.Items.AddRange(["Nothing", "Time only", "Date only", "Date and time"]);
        _cboDateTimeDisplay.SelectedIndex = (int)ReceiptDateTimeDisplayMode.None;

        var lblTaxDisplayMode = new Label { Text = "VAT display on customer receipt", Location = new Point(20, 932), Size = new Size(300, 28) };
        _cboTaxDisplayMode = new ComboBox
        {
            Location = new Point(20, 964),
            Size = new Size(260, 36),
            Font = new Font("Segoe UI", 12F),
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        // Order matches the TaxDisplayMode enum values (None=0, TotalOnly=1,
        // PerItem=2) so SelectedIndex can be cast directly to the enum.
        _cboTaxDisplayMode.Items.AddRange(["Don't show VAT", "VAT on total only", "VAT on every item"]);
        _cboTaxDisplayMode.SelectedIndex = (int)TaxDisplayMode.PerItem;

        var lblClientFontSize = new Label { Text = "Client receipt font size (1 = normal, half-steps allowed, up to 8)", Location = new Point(20, 1012), Size = new Size(420, 28) };
        _numClientFontSize = new NumericUpDown
        {
            Location = new Point(20, 1044),
            Size = new Size(100, 36),
            Font = new Font("Segoe UI", 14F),
            Minimum = 1,
            Maximum = 8,
            Increment = 0.5m,
            DecimalPlaces = 1,
            Value = 1
        };

        var lblKitchenFontSize = new Label { Text = "Kitchen ticket font size (1 = normal, half-steps allowed, up to 8)", Location = new Point(20, 1092), Size = new Size(420, 28) };
        _numKitchenFontSize = new NumericUpDown
        {
            Location = new Point(20, 1124),
            Size = new Size(100, 36),
            Font = new Font("Segoe UI", 14F),
            Minimum = 1,
            Maximum = 8,
            Increment = 0.5m,
            DecimalPlaces = 1,
            Value = 2
        };

        // Arabic lines print as a small rendered image rather than as ESC/POS
        // text - live testing proved this printer has no real Arabic
        // character ROM at all (every "Arabic code page" it claimed to
        // support just showed different Latin/CP437-style garbage). No
        // dropdown needed any more - see EscPosDocument.RenderTextAsRaster.
        var btnDiagnosticTest = new Button
        {
            Text = "Print Diagnostic Test (Kitchen)",
            Location = new Point(20, 1172),
            Size = new Size(260, 36),
            Font = new Font("Segoe UI", 11F)
        };
        btnDiagnosticTest.Click += async (_, _) => await DiagnosticTestAsync();

        // Same reasoning as the shared-settings Save button above - on its own
        // row below both printer fields, not beside either one.
        _btnSavePrinters = new Button
        {
            Text = "Save Printer Settings",
            Location = new Point(20, 1252),
            Size = new Size(220, 40),
            Font = new Font("Segoe UI", 12F, FontStyle.Bold)
        };
        _btnSavePrinters.Click += (_, _) => SavePrinters();

        _lblPrinterStatus = new Label
        {
            Location = new Point(20, 1300),
            Size = new Size(500, 28),
            ForeColor = Color.Green
        };

        Controls.Add(lblTitle);
        Controls.Add(lblSharedSection);
        Controls.Add(lblTaxRate);
        Controls.Add(_numTaxRate);
        Controls.Add(lblTimeZoneOffset);
        Controls.Add(_numTimeZoneOffset);
        Controls.Add(lblAutoCancelMinutes);
        Controls.Add(_numAutoCancelMinutes);
        Controls.Add(lblExtrasCategory);
        Controls.Add(_cboExtrasCategory);
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
        Controls.Add(lblBothReceiptsSection);
        Controls.Add(lblWrapAt);
        Controls.Add(_numOrderNumberWrapAt);
        Controls.Add(lblReceiptOptions);
        Controls.Add(lblDateTimeDisplay);
        Controls.Add(_cboDateTimeDisplay);
        Controls.Add(lblTaxDisplayMode);
        Controls.Add(_cboTaxDisplayMode);
        Controls.Add(lblClientFontSize);
        Controls.Add(_numClientFontSize);
        Controls.Add(lblKitchenFontSize);
        Controls.Add(_numKitchenFontSize);
        Controls.Add(btnDiagnosticTest);
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

        var autoCancelValue = await _apiClient.GetSettingValueAsync(AutoCancelMinutesKey);

        _numAutoCancelMinutes.Value = autoCancelValue is not null && decimal.TryParse(autoCancelValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var autoCancelMinutes)
            ? autoCancelMinutes
            : FallbackAutoCancelMinutes;

        var categories = await _apiClient.GetCategoriesAsync();
        _cboExtrasCategory.DataSource = categories;

        var extrasCategoryValue = await _apiClient.GetSettingValueAsync(ExtrasCategoryKey);
        _cboExtrasCategory.SelectedItem = int.TryParse(extrasCategoryValue, out var extrasCategoryId)
            ? categories.FirstOrDefault(c => c.CategoryId == extrasCategoryId)
            : null;

        var printerSettings = PrinterSettings.Load();
        _txtClientIp.Text = printerSettings.ClientPrinterIp;
        _numClientPort.Value = printerSettings.ClientPrinterPort;
        _txtKitchenIp.Text = printerSettings.KitchenPrinterIp;
        _numKitchenPort.Value = printerSettings.KitchenPrinterPort;
        _numOrderNumberWrapAt.Value = printerSettings.ReceiptOrderNumberWrapAt;
        _cboDateTimeDisplay.SelectedIndex = (int)printerSettings.ReceiptDateTimeDisplay;
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
            await _apiClient.SetSettingValueAsync(AutoCancelMinutesKey, ((int)_numAutoCancelMinutes.Value).ToString(CultureInfo.InvariantCulture));

            // No extras category picked is a valid choice (restaurants
            // without add-ons don't need one) - saved as an empty value
            // rather than skipped, so a previously-picked category can be
            // explicitly cleared too.
            var selectedExtrasCategory = _cboExtrasCategory.SelectedItem as CategoryDto;
            await _apiClient.SetSettingValueAsync(ExtrasCategoryKey, selectedExtrasCategory?.CategoryId.ToString() ?? "");

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
            ReceiptOrderNumberWrapAt = (int)_numOrderNumberWrapAt.Value,
            ReceiptDateTimeDisplay = (ReceiptDateTimeDisplayMode)_cboDateTimeDisplay.SelectedIndex,
            TaxDisplayMode = (TaxDisplayMode)_cboTaxDisplayMode.SelectedIndex,
            ClientReceiptFontSize = _numClientFontSize.Value,
            KitchenTicketFontSize = _numKitchenFontSize.Value
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
                ? ReceiptBuilder.BuildCustomerReceipt(SampleOrder(), (ReceiptDateTimeDisplayMode)_cboDateTimeDisplay.SelectedIndex, (TaxDisplayMode)_cboTaxDisplayMode.SelectedIndex, _numClientFontSize.Value)
                : ReceiptBuilder.BuildKitchenTicket(SampleOrder(), _numKitchenFontSize.Value);

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

    private async Task DiagnosticTestAsync()
    {
        var ip = _txtKitchenIp.Text;
        if (string.IsNullOrWhiteSpace(ip))
        {
            _lblPrinterStatus.ForeColor = Color.Red;
            _lblPrinterStatus.Text = "Enter the kitchen printer's IP address first.";
            return;
        }

        _lblPrinterStatus.ForeColor = Color.Black;
        _lblPrinterStatus.Text = "Printing diagnostic test...";

        try
        {
            var bytes = ReceiptBuilder.BuildDiagnosticTest();
            await new NetworkReceiptPrinter(ip, (int)_numKitchenPort.Value).PrintAsync(bytes);

            _lblPrinterStatus.ForeColor = Color.Green;
            _lblPrinterStatus.Text = "Diagnostic test sent successfully.";
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
            ? ReceiptBuilder.PreviewCustomerReceipt(SampleOrder(), (ReceiptDateTimeDisplayMode)_cboDateTimeDisplay.SelectedIndex, (TaxDisplayMode)_cboTaxDisplayMode.SelectedIndex, _numClientFontSize.Value)
            : ReceiptBuilder.PreviewKitchenTicket(SampleOrder(), _numKitchenFontSize.Value);

        using var dialog = new FormReceiptPreviewDialog(
            isClient ? "Client Receipt Preview" : "Kitchen Ticket Preview", text);
        dialog.ShowDialog(this);
    }

    // Hand-verifiable round numbers (114 @ 14% tax = exactly 100 excl. tax + 14
    // tax, same convention as the automated test suite) plus two separately
    // placed Hotdogs with different comments, to prove same-name items never get
    // merged - each keeps its own line and its own comment. One comment is
    // Arabic specifically so Test Print/Preview exercises the image-rendering
    // path (see EscPosDocument.RenderTextAsRaster) without a real order first.
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
