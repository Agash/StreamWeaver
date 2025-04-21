using Microsoft.UI.Xaml.Data;
using System;

namespace StreamWeaver.UI.Converters;

/// <summary>
/// Converts a boolean value to one of two strings provided in the ConverterParameter.
/// Parameter format: "TrueValue;FalseValue" (e.g., "Active;Closed").
/// Defaults to boolean.ToString() if parameter is missing or invalid.
/// </summary>
public partial class BoolToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not bool boolValue)
        {
            return string.Empty; // Or return value?.ToString() ?? string.Empty;
        }

        if (parameter is string paramString && !string.IsNullOrWhiteSpace(paramString))
        {
            string[] parts = paramString.Split(';');
            if (parts.Length == 2)
            {
                // Return the first part if true, the second part if false
                return boolValue ? parts[0] : parts[1];
            }
        }

        // Fallback if parameter is invalid or missing
        return boolValue.ToString();
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        // ConvertBack is typically not needed for display purposes
        throw new NotImplementedException();
    }
}
