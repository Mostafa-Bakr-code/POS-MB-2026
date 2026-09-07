using System.Globalization;

namespace POS_MB.Mobile.Converters;

// Used for the item-photo placeholder "avatar" on the menu (see MenuPage.xaml)
// until real item images exist - an initial letter reads far better than an
// empty box.
public class FirstLetterConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string { Length: > 0 } text ? text[0].ToString().ToUpperInvariant() : "?";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
