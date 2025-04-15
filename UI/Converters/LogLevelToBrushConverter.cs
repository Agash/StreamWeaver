using Microsoft.Extensions.Logging;
using Microsoft.UI;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using System;
using Windows.UI; // Need this for Color struct

namespace StreamWeaver.UI.Converters;

public partial class LogLevelToBrushConverter : IValueConverter
{
    // Define base colors (can be adjusted to your theme)
    private static readonly Color s_debugColor = Colors.Gray;
    private static readonly Color s_infoColor = Colors.CornflowerBlue; // Or keep Theme default (null)?
    private static readonly Color s_warningColor = Colors.Orange;
    private static readonly Color s_errorColor = Colors.OrangeRed;
    private static readonly Color s_criticalColor = Colors.Red;
    private static readonly Color s_traceColor = Colors.DarkGray;
    private static readonly Color s_noneColor = Colors.Black; // Should not happen often

    // Define Brushes (cached for performance)
    // Solid Brushes (for Icon)
    private static readonly SolidColorBrush s_debugBrush = new(s_debugColor);
    private static readonly SolidColorBrush s_infoBrush = new(s_infoColor);
    private static readonly SolidColorBrush s_warningBrush = new(s_warningColor);
    private static readonly SolidColorBrush s_errorBrush = new(s_errorColor);
    private static readonly SolidColorBrush s_criticalBrush = new(s_criticalColor);
    private static readonly SolidColorBrush s_traceBrush = new(s_traceColor);
    private static readonly SolidColorBrush s_noneBrush = new(s_noneColor);
    private static readonly SolidColorBrush s_defaultBrush = new(Colors.Transparent); // Transparent default for background

    // Subtle Brushes (for Background) - Adjust Alpha (0x1A is ~10% opacity)
    private const byte SubtleAlpha = 0x1A; // Approx 10% opacity - ADJUST AS NEEDED
    private static readonly SolidColorBrush s_debugBgBrush = new(Color.FromArgb(SubtleAlpha, s_debugColor.R, s_debugColor.G, s_debugColor.B));
    private static readonly SolidColorBrush s_infoBgBrush = new(Color.FromArgb(SubtleAlpha, s_infoColor.R, s_infoColor.G, s_infoColor.B));
    private static readonly SolidColorBrush s_warningBgBrush = new(Color.FromArgb(SubtleAlpha, s_warningColor.R, s_warningColor.G, s_warningColor.B));
    private static readonly SolidColorBrush s_errorBgBrush = new(Color.FromArgb(SubtleAlpha, s_errorColor.R, s_errorColor.G, s_errorColor.B));
    private static readonly SolidColorBrush s_criticalBgBrush = new(Color.FromArgb(SubtleAlpha, s_criticalColor.R, s_criticalColor.G, s_criticalColor.B));
    private static readonly SolidColorBrush s_traceBgBrush = new(Color.FromArgb(SubtleAlpha, s_traceColor.R, s_traceColor.G, s_traceColor.B));
    // No background for None or default Info level? Or a very faint gray? Let's default to transparent.

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not LogLevel level)
        {
            return s_defaultBrush; // Default transparent
        }

        bool useSubtleBackground = parameter is string strParam && strParam.Equals("Background", StringComparison.OrdinalIgnoreCase);

        if (useSubtleBackground)
        {
            return level switch
            {
                LogLevel.Information => s_infoBgBrush, // TODO: Decide if Info needs background
                LogLevel.Debug => s_debugBgBrush,
                LogLevel.Warning => s_warningBgBrush,
                LogLevel.Error => s_errorBgBrush,
                LogLevel.Critical => s_criticalBgBrush,
                LogLevel.Trace => s_traceBgBrush,
                _ => s_defaultBrush // Default to transparent background
            };
        }
        else // Return solid color for icon
        {
            return level switch
            {
                LogLevel.Information => s_infoBrush,
                LogLevel.Debug => s_debugBrush,
                LogLevel.Warning => s_warningBrush,
                LogLevel.Error => s_errorBrush,
                LogLevel.Critical => s_criticalBrush,
                LogLevel.Trace => s_traceBrush,
                LogLevel.None => s_noneBrush,
                _ => s_defaultBrush // Should not happen for icons
            };
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
