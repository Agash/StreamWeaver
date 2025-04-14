using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;

namespace StreamWeaver.UI.Converters;

/// <summary>
/// Converts a known badge identifier string (e.g., "twitch/moderator/1")
/// into a BitmapImage using a predefined static URL map.
/// **Note:** This converter uses a hardcoded map and is less flexible than one querying a service.
/// It may become outdated and does not support dynamic badge fetching (like YouTube badges).
/// </summary>
public partial class BadgeIdentifierToUrlConverter : IValueConverter
{
    private static readonly Lazy<ILogger<BadgeIdentifierToUrlConverter>?> s_lazyLogger = new(() =>
    {
        try
        {
            return App.GetService<ILogger<BadgeIdentifierToUrlConverter>>();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BadgeIdentifierToUrlConverter.StaticInit] Failed to get ILogger: {ex.Message}");
            return null;
        }
    });

    private static ILogger<BadgeIdentifierToUrlConverter>? Logger => s_lazyLogger.Value;

    private static readonly Dictionary<string, string?> s_badgeUrlMap = new()
    {
        { "twitch/moderator/1", "https://static-cdn.jtvnw.net/badges/v1/3267646d-33f0-4b17-b3df-f923a41db1d0/1" },
        { "twitch/subscriber/0", "https://static-cdn.jtvnw.net/badges/v1/5d9f2208-5dd8-11e7-8513-2ff4adfae661/1" },
        { "twitch/vip/1", "https://static-cdn.jtvnw.net/badges/v1/b817aba4-fad8-49e2-b88a-7cc744dfa6ec/1" },
        { "twitch/broadcaster/1", "https://static-cdn.jtvnw.net/badges/v1/5527c58c-fb7d-422d-b71b-f309dcb85cc1/1" },
        { "twitch/partner/1", "https://static-cdn.jtvnw.net/badges/v1/d12a2e27-16f6-41d0-ab77-b780518f00a3/1" },
        { "twitch/glitchcon2020/1", "https://static-cdn.jtvnw.net/badges/v1/1d4b00b9-f976-418f-8f97-3599ea8acfbb/1" },
        { "youtube/member", null },
        { "youtube/moderator", null },
        { "youtube/owner", null },
        { "youtube/verified", null },
    };

    /// <summary>
    /// Converts a known badge identifier string to a BitmapImage using a static map.
    /// </summary>
    /// <param name="value">The badge identifier string (e.g., "twitch/moderator/1").</param>
    /// <param name="targetType">The type of the target property (expected to be ImageSource or compatible).</param>
    /// <param name="parameter">An optional parameter (not used).</param>
    /// <param name="language">The language/culture (not used).</param>
    /// <returns>A <see cref="BitmapImage"/> if the identifier is found in the map and the URL is valid; otherwise, null.</returns>
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string identifier && s_badgeUrlMap.TryGetValue(identifier, out string? url) && !string.IsNullOrEmpty(url))
        {
            try
            {
                return new BitmapImage(new Uri(url));
            }
            catch (FormatException uriEx)
            {
                Logger?.LogError(uriEx, "Invalid URL format for badge identifier {Identifier}. URL: {Url}", identifier, url);
                return null;
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "Error creating BitmapImage for badge identifier {Identifier}. URL: {Url}", identifier, url);
                return null;
            }
        }

        if (value is string id)
        {
            Logger?.LogTrace("Badge identifier '{Identifier}' not found in static map or URL is null/empty.", id);
        }
        else
        {
            Logger?.LogTrace("Input value is not a string identifier. Value: {Value}", value);
        }

        return null;
    }

    /// <summary>
    /// Converts a value back - Not implemented for this converter.
    /// </summary>
    /// <exception cref="NotImplementedException">Always thrown.</exception>
    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        Logger?.LogWarning("ConvertBack called but is not implemented.");
        throw new NotImplementedException("BadgeIdentifierToUrlConverter does not support ConvertBack.");
    }
}
