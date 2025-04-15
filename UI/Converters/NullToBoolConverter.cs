using Microsoft.UI.Xaml.Data;

namespace StreamWeaver.UI.Converters;

public partial class NullToBoolConverter : IValueConverter
{
    /// <summary>
    /// Converts a null/non-null value to a boolean.
    /// </summary>
    /// <param name="value">The object value to check.</param>
    /// <param name="targetType">Not used.</param>
    /// <param name="parameter">Optional parameter: "Invert". If set to "Invert", returns true for null, false for non-null.</param>
    /// <param name="language">Not used.</param>
    /// <returns>False if value is null, True if value is not null (unless inverted).</returns>
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool isNull = value == null;
        bool invert = parameter is string strParam && strParam.Equals("Invert", StringComparison.OrdinalIgnoreCase);

        return invert ? isNull : !isNull;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        // ConvertBack is not needed for one-way binding used here
        throw new NotImplementedException();
    }
}
