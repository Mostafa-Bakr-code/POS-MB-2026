using System.Globalization;
using POS_MB.WinformsApp.Api;

namespace POS_MB.WinformsApp.Controls;

public class SettingsControl : UserControl
{
    private const string DefaultTaxRateKey = "DefaultTaxRate";
    private const decimal FallbackTaxRate = 14.00m;

    private readonly ApiClient _apiClient = new();
    private readonly NumericUpDown _numTaxRate;
    private readonly Button _btnSave;
    private readonly Label _lblStatus;

    public SettingsControl()
    {
        Font = new Font("Segoe UI", 12F);
        Padding = new Padding(20);

        var lblTitle = new Label
        {
            Text = "Tax Settings",
            Dock = DockStyle.Top,
            Height = 40,
            Font = new Font("Segoe UI", 16F, FontStyle.Bold)
        };

        var lblTaxRate = new Label
        {
            Text = "Default Tax Rate (%) - applied to newly created items unless overridden",
            Location = new Point(20, 70),
            Size = new Size(500, 28)
        };

        _numTaxRate = new NumericUpDown
        {
            Location = new Point(20, 102),
            Size = new Size(150, 40),
            Font = new Font("Segoe UI", 14F),
            DecimalPlaces = 2,
            Minimum = 0,
            Maximum = 100,
            Increment = 0.5m
        };

        _btnSave = new Button
        {
            Text = "Save",
            Location = new Point(190, 102),
            Size = new Size(150, 40),
            Font = new Font("Segoe UI", 12F, FontStyle.Bold)
        };
        _btnSave.Click += async (_, _) => await SaveAsync();

        _lblStatus = new Label
        {
            Location = new Point(20, 150),
            Size = new Size(500, 28),
            ForeColor = Color.Green
        };

        Controls.Add(lblTaxRate);
        Controls.Add(_numTaxRate);
        Controls.Add(_btnSave);
        Controls.Add(_lblStatus);
        Controls.Add(lblTitle);

        Load += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        var value = await _apiClient.GetSettingValueAsync(DefaultTaxRateKey);

        _numTaxRate.Value = value is not null && decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var rate)
            ? rate
            : FallbackTaxRate;
    }

    private async Task SaveAsync()
    {
        _lblStatus.Text = "";
        _btnSave.Enabled = false;
        try
        {
            await _apiClient.SetSettingValueAsync(DefaultTaxRateKey, _numTaxRate.Value.ToString(CultureInfo.InvariantCulture));
            _lblStatus.ForeColor = Color.Green;
            _lblStatus.Text = "Saved. This applies to items created from now on, not existing ones.";
        }
        catch (Exception ex)
        {
            _lblStatus.ForeColor = Color.Red;
            _lblStatus.Text = $"Could not save: {ex.Message}";
        }
        finally
        {
            _btnSave.Enabled = true;
        }
    }
}
