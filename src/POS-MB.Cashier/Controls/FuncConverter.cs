using System.Globalization;

namespace POS_MB.Cashier.Controls;

// A tiny one-way IValueConverter wrapping a plain Func - used by
// DataGridView to bind each cell Label's Text to "run this column's
// GetText against the row object", without needing a third-party
// converter package for something this small.
public class FuncConverter(Func<object, string> convert) : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null ? "" : convert(value);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
