using Microsoft.UI.Xaml; // For Visibility
using Microsoft.UI.Xaml.Data;

namespace StreamWeaver.UI.Converters;

public partial class NullToVisibilityConverter : IValueConverter
{
    /// <summary>
    /// Converts a null/non-null value to Visibility.Visible or Visibility.Collapsed.
    /// </summary>
    /// <param name="value">The object value to check.</param>
    /// <param name="targetType">Not used.</param>
    /// <param name="parameter">Optional parameter: "Invert" or "VisibleWhenNull". If set, returns Visible for null, Collapsed for non-null.</param>
    /// <param name="language">Not used.</param>
    /// <returns>Visibility.Collapsed if value is null, Visibility.Visible if value is not null (unless inverted).</returns>
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool isNull = value == null;
        bool invert = parameter is string strParam &&
                      (strParam.Equals("Invert", StringComparison.OrdinalIgnoreCase) ||
                       strParam.Equals("VisibleWhenNull", StringComparison.OrdinalIgnoreCase));

        bool shouldBeVisible = invert ? isNull : !isNull;

        return shouldBeVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}
