using System.Globalization;

namespace POS_MB.Cashier.Controls;

// Replaces WinForms' NumericUpDown - used for the cart's quantity field
// (Phase 5) and Settings' font-size fields (Phase 7, which need Increment
// values smaller than 1 - see PrinterSettings.KitchenTicketFontSize's own
// half-step support). MAUI has no built-in numeric-spinner control, so this
// is a small reusable ContentView instead of hand-rolling the same -/+/entry
// layout in every screen that needs one.
public partial class NumericStepper : ContentView
{
    // Guards against OnEntryTextChanged re-parsing and re-writing the Entry's
    // own text while THIS control is the one that just set it (e.g. from
    // SetValue/the +/- buttons) - without this, every value change would
    // recurse through TextChanged once more, and typing mid-edit could fight
    // with the reformatted text being written back.
    private bool _suppressTextChanged;

    public static readonly BindableProperty MinimumProperty =
        BindableProperty.Create(nameof(Minimum), typeof(decimal), typeof(NumericStepper), 1m);

    public static readonly BindableProperty MaximumProperty =
        BindableProperty.Create(nameof(Maximum), typeof(decimal), typeof(NumericStepper), 999m);

    public static readonly BindableProperty IncrementProperty =
        BindableProperty.Create(nameof(Increment), typeof(decimal), typeof(NumericStepper), 1m);

    // 0 for whole-number fields (cart quantity); 1 for fields that need a
    // half-step like KitchenTicketFontSize (1, 1.5, 2, ...).
    public static readonly BindableProperty DecimalPlacesProperty =
        BindableProperty.Create(nameof(DecimalPlaces), typeof(int), typeof(NumericStepper), 0);

    public static readonly BindableProperty ValueProperty =
        BindableProperty.Create(nameof(Value), typeof(decimal), typeof(NumericStepper), 1m,
            BindingMode.TwoWay, propertyChanged: OnValueChanged);

    public decimal Minimum
    {
        get => (decimal)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public decimal Maximum
    {
        get => (decimal)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public decimal Increment
    {
        get => (decimal)GetValue(IncrementProperty);
        set => SetValue(IncrementProperty, value);
    }

    public int DecimalPlaces
    {
        get => (int)GetValue(DecimalPlacesProperty);
        set => SetValue(DecimalPlacesProperty, value);
    }

    public decimal Value
    {
        get => (decimal)GetValue(ValueProperty);
        set => SetValue(ValueProperty, Math.Clamp(value, Minimum, Maximum));
    }

    public event EventHandler<decimal>? ValueChanged;

    public NumericStepper()
    {
        InitializeComponent();
        RefreshEntryText();
    }

    private static void OnValueChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var stepper = (NumericStepper)bindable;
        stepper.RefreshEntryText();
        stepper.ValueChanged?.Invoke(stepper, (decimal)newValue);
    }

    private void RefreshEntryText()
    {
        _suppressTextChanged = true;
        ValueEntry.Text = Value.ToString($"F{DecimalPlaces}", CultureInfo.InvariantCulture);
        _suppressTextChanged = false;
    }

    private void OnDecrementClicked(object? sender, EventArgs e) => Value -= Increment;

    private void OnIncrementClicked(object? sender, EventArgs e) => Value += Increment;

    private void OnEntryTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_suppressTextChanged) return;

        if (decimal.TryParse(e.NewTextValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
            Value = parsed;
    }
}
