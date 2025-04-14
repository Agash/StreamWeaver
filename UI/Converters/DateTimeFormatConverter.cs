using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Data;

namespace StreamWeaver.UI.Converters;

/// <summary>
/// Converts a DateTime or DateTimeOffset object to a formatted string representation in the user's local time zone.
/// The desired format string can be passed via the ConverterParameter (e.g., "HH:mm:ss", "yyyy-MM-dd").
/// Defaults to the "g" (general short date/time) format if no parameter is provided or if the parameter is invalid.
/// </summary>
public partial class DateTimeFormatConverter : IValueConverter
{
    private static readonly Lazy<ILogger<DateTimeFormatConverter>?> s_lazyLogger = new(() =>
    {
        try
        {
            return App.GetService<ILogger<DateTimeFormatConverter>>();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DateTimeFormatConverter.StaticInit] Failed to get ILogger: {ex.Message}");
            return null;
        }
    });

    private static ILogger<DateTimeFormatConverter>? Logger => s_lazyLogger.Value;

    /// <summary>
    /// Converts a DateTime or DateTimeOffset to a formatted string (local time).
    /// </summary>
    /// <param name="value">The DateTime or DateTimeOffset object to convert.</param>
    /// <param name="targetType">The type of the target property (expected to be string).</param>
    /// <param name="parameter">The format string (e.g., "HH:mm:ss"). Defaults to "g".</param>
    /// <param name="language">The language/culture (not used).</param>
    /// <returns>A formatted string representation of the date/time in the local time zone, or the original value's string representation on failure.</returns>
    public object Convert(object? value, Type targetType, object parameter, string language)
    {
        string format = parameter as string ?? "g";

        try
        {
            switch (value)
            {
                case DateTime dt:

                    return dt.ToLocalTime().ToString(format);
                case DateTimeOffset dto:

                    return dto.LocalDateTime.ToString(format);
            }
        }
        catch (FormatException ex)
        {
            Logger?.LogError(ex, "Invalid format string provided: '{FormatString}'. Using default format 'g'.", format);

            format = "g";
            try
            {
                switch (value)
                {
                    case DateTime dtErr:
                        return dtErr.ToLocalTime().ToString(format);
                    case DateTimeOffset dtoErr:
                        return dtoErr.LocalDateTime.ToString(format);
                }
            }
            catch (Exception fallbackEx)
            {
                Logger?.LogError(fallbackEx, "Error formatting date/time value even with default format 'g'. Value: {Value}", value);
            }
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to format date/time value. Value: {Value}, Format: {FormatString}", value, format);
        }

        Logger?.LogTrace("Value was not DateTime or DateTimeOffset, or formatting failed. Returning default ToString(). Value: {Value}", value);
        return value?.ToString() ?? string.Empty;
    }

    /// <summary>
    /// Converts a value back - Not implemented for this converter.
    /// </summary>
    /// <exception cref="NotImplementedException">Always thrown.</exception>
    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        Logger?.LogWarning("ConvertBack called but is not implemented.");
        throw new NotImplementedException("DateTimeFormatConverter does not support ConvertBack.");
    }
}
