using Microsoft.UI.Xaml.Data;

namespace StreamWeaver.UI.Converters;

/// <summary>
/// Converts a boolean value to an opacity value.
/// True returns 1.0 (fully opaque).
/// False returns a reduced opacity (default 0.5, can be set via parameter).
/// Parameter "Invert" reverses this logic.
/// </summary>
public class BoolToOpacityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool invert = false;
        double falseOpacity = 0.5;

        if (parameter is string paramString)
        {
            string[] parts = paramString.Split(',');
            foreach (string part in parts)
            {
                if (part.Trim().Equals("Invert", StringComparison.OrdinalIgnoreCase))
                {
                    invert = true;
                }
                else if (
                    double.TryParse(
                        part.Trim(),
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out double opacityValue
                    )
                )
                {
                    falseOpacity = Math.Clamp(opacityValue, 0.0, 1.0);
                }
            }
        }

        bool boolValue = value is bool b && b;

        if (invert)
        {
            boolValue = !boolValue;
        }

        return boolValue ? 1.0 : falseOpacity;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}
