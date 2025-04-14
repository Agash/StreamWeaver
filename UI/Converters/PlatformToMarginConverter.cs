using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace StreamWeaver.UI.Converters;

/// <summary>
/// Converts a Platform string ("YouTube", "Twitch", etc.) to a Thickness Margin.
/// Returns a zero left margin for YouTube and a specified left margin for others,
/// preserving other margin values (like the top offset).
/// </summary>
public partial class PlatformToMarginConverter : IValueConverter
{
    private const double DEFAULT_LEFT_MARGIN = 42.0; // Ellipse (36) + Margin (6)
    private const double DEFAULT_TOP_MARGIN = -2.0; // Existing vertical offset adjustment

    private static readonly Lazy<ILogger<PlatformToMarginConverter>?> s_lazyLogger = new(App.GetService<ILogger<PlatformToMarginConverter>>);

    private static ILogger<PlatformToMarginConverter>? Logger => s_lazyLogger.Value;

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        double topMargin = DEFAULT_TOP_MARGIN;
        double rightMargin = 0;
        double bottomMargin = 0;

        // Allow overriding the non-YouTube left margin via parameter if needed
        double nonYouTubeLeftMargin = DEFAULT_LEFT_MARGIN;
        if (parameter is string paramString && double.TryParse(paramString, out double paramMargin))
        {
            nonYouTubeLeftMargin = paramMargin;
        }

        double leftMargin;
        if (value is string platform)
        {
            if (platform.Equals("YouTube", StringComparison.OrdinalIgnoreCase))
            {
                leftMargin = 0; // No extra left margin needed for YouTube
                Logger?.LogTrace(
                    "Platform is YouTube, returning margin: {Left},{Top},{Right},{Bottom}",
                    leftMargin,
                    topMargin,
                    rightMargin,
                    bottomMargin
                );
            }
            else
            {
                leftMargin = nonYouTubeLeftMargin; // Apply indent for Twitch/Other
                Logger?.LogTrace(
                    "Platform is not YouTube ({Platform}), returning margin: {Left},{Top},{Right},{Bottom}",
                    platform,
                    leftMargin,
                    topMargin,
                    rightMargin,
                    bottomMargin
                );
            }
        }
        else
        {
            Logger?.LogTrace("Value is not a string ({Value}), returning default non-YouTube margin.", value);
            leftMargin = nonYouTubeLeftMargin; // Default to indented margin if value isn't string
        }

        return new Thickness(leftMargin, topMargin, rightMargin, bottomMargin);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}
