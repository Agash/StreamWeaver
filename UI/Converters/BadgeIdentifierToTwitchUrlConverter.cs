using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;
using StreamWeaver.Core.Services;

namespace StreamWeaver.UI.Converters;

/// <summary>
/// Converts a Twitch badge identifier string (e.g., "twitch/subscriber/0")
/// into a BitmapImage by querying the <see cref="IEmoteBadgeService"/>.
/// Returns null if the identifier is invalid, the service is unavailable, the badge is not found,
/// or an error occurs.
/// </summary>
public partial class BadgeIdentifierToTwitchUrlConverter : IValueConverter
{
    private static readonly Lazy<ILogger<BadgeIdentifierToTwitchUrlConverter>?> s_lazyLogger = new(() =>
    {
        try
        {
            return App.GetService<ILogger<BadgeIdentifierToTwitchUrlConverter>>();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[BadgeConverter.StaticInit] Failed to get ILogger<BadgeIdentifierToTwitchUrlConverter>: {ex.Message}"
            );
            return null;
        }
    });

    private static readonly Lazy<IEmoteBadgeService?> s_lazyEmoteBadgeService = new(() =>
    {
        try
        {
            return App.GetService<IEmoteBadgeService>();
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to get IEmoteBadgeService during lazy initialization.");
            if (Logger == null)
            {
                System.Diagnostics.Debug.WriteLine($"[BadgeConverter.StaticInit] Failed to get IEmoteBadgeService: {ex.Message}");
            }

            return null;
        }
    });

    private static ILogger<BadgeIdentifierToTwitchUrlConverter>? Logger => s_lazyLogger.Value;

    private static IEmoteBadgeService? EmoteBadgeService => s_lazyEmoteBadgeService.Value;

    /// <summary>
    /// Converts a badge identifier string to a BitmapImage.
    /// </summary>
    /// <param name="value">The badge identifier string (e.g., "twitch/subscriber/0").</param>
    /// <param name="targetType">The type of the target property (expected to be ImageSource or compatible).</param>
    /// <param name="parameter">An optional parameter (not used).</param>
    /// <param name="language">The language/culture (not used).</param>
    /// <returns>A <see cref="BitmapImage"/> if conversion is successful; otherwise, null.</returns>
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not string identifier || string.IsNullOrWhiteSpace(identifier))
        {
            Logger?.LogDebug("Input value is null or not a valid string identifier. Value: {Value}", value);
            return null;
        }

        if (EmoteBadgeService == null)
        {
            Logger?.LogError("EmoteBadgeService is null, cannot process badge identifier: {Identifier}", identifier);
            return null;
        }

        if (identifier.StartsWith("twitch/", StringComparison.OrdinalIgnoreCase))
        {
            string[] parts = identifier.Split('/');
            if (parts.Length == 3)
            {
                string setId = parts[1];
                string versionId = parts[2];

                try
                {
                    string? url = EmoteBadgeService.GetTwitchBadgeUrl(setId, versionId);

                    if (!string.IsNullOrEmpty(url) && Uri.TryCreate(url, UriKind.Absolute, out Uri? imageUri))
                    {
                        Logger?.LogTrace("Found URL for badge {Identifier}: {Url}", identifier, url);

                        return new BitmapImage(imageUri);
                    }
                    else
                    {
                        Logger?.LogWarning(
                            "URL not found or invalid for Twitch badge identifier: {Identifier} (Set: {SetId}, Version: {VersionId}). Returned URL: '{Url}'",
                            identifier,
                            setId,
                            versionId,
                            url ?? "null"
                        );
                    }
                }
                catch (Exception ex)
                {
                    string? fetchedUrl = EmoteBadgeService.GetTwitchBadgeUrl(setId, versionId);
                    Logger?.LogError(
                        ex,
                        "Error processing or creating BitmapImage for badge {Identifier} (Set: {SetId}, Version: {VersionId}, URL: '{Url}')",
                        identifier,
                        setId,
                        versionId,
                        fetchedUrl ?? "N/A"
                    );
                }
            }
            else
            {
                Logger?.LogWarning("Invalid Twitch badge identifier format: {Identifier}. Expected 'twitch/set_id/version_id'.", identifier);
            }
        }
        else
        {
            Logger?.LogTrace("Identifier '{Identifier}' is not a Twitch badge identifier, skipping conversion.", identifier);
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
        throw new NotImplementedException("BadgeIdentifierToTwitchUrlConverter does not support ConvertBack.");
    }
}
