using System.Globalization;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.UI;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace StreamWeaver.UI.Converters;

/// <summary>
/// Converts a color string (hex code like "#RRGGBB", "#AARRGGBB", "#RGB" or a named color like "Red")
/// into a <see cref="SolidColorBrush"/>.
/// Provides fallback options via the ConverterParameter or defaults to Gray.
/// </summary>
public partial class StringToBrushConverter : IValueConverter
{
    private static readonly Lazy<ILogger<StringToBrushConverter>?> s_lazyLogger = new(() =>
    {
        try
        {
            return App.GetService<ILogger<StringToBrushConverter>>();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StringToBrushConverter.StaticInit] Failed to get ILogger: {ex.Message}");
            return null;
        }
    });

    private static ILogger<StringToBrushConverter>? Logger => s_lazyLogger.Value;

    /// <summary>
    /// Converts a color string representation into a SolidColorBrush.
    /// </summary>
    /// <param name="value">The color string (hex or named color).</param>
    /// <param name="targetType">The type of the target property (expected to be Brush or compatible).</param>
    /// <param name="parameter">An optional fallback color (as Brush, Color, or string) or null.</param>
    /// <param name="language">The language/culture (not used).</param>
    /// <returns>A <see cref="SolidColorBrush"/> corresponding to the input string, or a fallback brush.</returns>
    public object Convert(object? value, Type targetType, object? parameter, string language)
    {
        if (value is string colorString && !string.IsNullOrEmpty(colorString))
        {
            try
            {
                if (colorString.StartsWith('#'))
                {
                    byte a = 255;
                    byte r,
                        g,
                        b;
                    string hex = colorString.TrimStart('#');

                    switch (hex.Length)
                    {
                        case 8:
                            a = byte.Parse(hex.AsSpan(0, 2), NumberStyles.HexNumber);
                            r = byte.Parse(hex.AsSpan(2, 2), NumberStyles.HexNumber);
                            g = byte.Parse(hex.AsSpan(4, 2), NumberStyles.HexNumber);
                            b = byte.Parse(hex.AsSpan(6, 2), NumberStyles.HexNumber);
                            break;
                        case 6:
                            r = byte.Parse(hex.AsSpan(0, 2), NumberStyles.HexNumber);
                            g = byte.Parse(hex.AsSpan(2, 2), NumberStyles.HexNumber);
                            b = byte.Parse(hex.AsSpan(4, 2), NumberStyles.HexNumber);
                            break;
                        case 3:
                            r = byte.Parse($"{hex[0]}{hex[0]}", NumberStyles.HexNumber);
                            g = byte.Parse($"{hex[1]}{hex[1]}", NumberStyles.HexNumber);
                            b = byte.Parse($"{hex[2]}{hex[2]}", NumberStyles.HexNumber);
                            break;
                        default:
                            Logger?.LogWarning("Invalid hex color string length ({Length}): {ColorString}. Using fallback.", hex.Length, colorString);
                            return GetFallbackBrush(parameter, colorString);
                    }

                    return new SolidColorBrush(Color.FromArgb(a, r, g, b));
                }

                PropertyInfo? colorProp = typeof(Colors).GetProperty(
                    colorString,
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.IgnoreCase
                );
                if (colorProp?.GetValue(null) is Color namedColor)
                {
                    return new SolidColorBrush(namedColor);
                }
                else
                {
                    Logger?.LogWarning(
                        "Color string '{ColorString}' is not a valid hex code or recognized named color. Using fallback.",
                        colorString
                    );
                }
            }
            catch (FormatException formatEx)
            {
                Logger?.LogError(formatEx, "Format exception converting color string '{ColorString}'. Using fallback.", colorString);
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "Error converting color string '{ColorString}'. Using fallback.", colorString);
            }
        }
        else if (value != null)
        {
            Logger?.LogDebug("Input value is not a string. Value type: {ValueType}. Using fallback.", value.GetType().Name);
        }

        return GetFallbackBrush(parameter, value?.ToString());
    }

    /// <summary>
    /// Determines and returns a fallback SolidColorBrush.
    /// Uses the converter parameter if provided and valid, otherwise defaults to Gray.
    /// </summary>
    /// <param name="parameter">The converter parameter, potentially containing a fallback Brush, Color, or color string.</param>
    /// <param name="originalValueForLog">The original value being converted, for logging purposes.</param>
    /// <returns>A fallback <see cref="SolidColorBrush"/>.</returns>
    private SolidColorBrush GetFallbackBrush(object? parameter, string? originalValueForLog = null)
    {
        Logger?.LogTrace(
            "Using fallback brush for original value '{OriginalValue}'. Fallback Parameter: {FallbackParameter}",
            originalValueForLog ?? "null",
            parameter ?? "null"
        );

        if (parameter is SolidColorBrush brush)
            return brush;
        if (parameter is Color color)
            return new SolidColorBrush(color);

        if (parameter is string colorString)
        {
            object fallback = Convert(colorString, typeof(SolidColorBrush), null, "");
            if (fallback is SolidColorBrush sb)
                return sb;
        }

        return new SolidColorBrush(Colors.Gray);
    }

    /// <summary>
    /// Converts a value back - Not implemented for this converter.
    /// </summary>
    /// <exception cref="NotImplementedException">Always thrown.</exception>
    public object ConvertBack(object value, Type targetType, object? parameter, string language)
    {
        Logger?.LogWarning("ConvertBack called but is not implemented.");

        throw new NotImplementedException("StringToBrushConverter does not support ConvertBack.");
    }
}
