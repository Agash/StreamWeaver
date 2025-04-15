using Microsoft.UI.Xaml.Data;

namespace StreamWeaver.UI.Converters;

/// <summary>
/// Converts a boolean value to its inverse. True becomes False, False becomes True.
/// </summary>
public partial class BoolInverterConverter : IValueConverter
{
    /// <summary>
    /// Inverts the boolean value.
    /// </summary>
    public object Convert(object value, Type targetType, object parameter, string language) => value is bool b ? !b : (object)false;

    /// <summary>
    /// Inverts the boolean value back (same logic).
    /// </summary>
    public object ConvertBack(object value, Type targetType, object parameter, string language) => value is bool b ? !b : (object)false;
}
