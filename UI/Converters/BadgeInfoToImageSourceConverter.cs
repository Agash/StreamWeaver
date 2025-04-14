using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;
using StreamWeaver.Core.Models.Events;
using StreamWeaver.Core.Services;

namespace StreamWeaver.UI.Converters;

/// <summary>
/// Converts a BadgeInfo object into a BitmapImage Source.
/// Prioritizes the ImageUrl within BadgeInfo if present.
/// Otherwise, attempts to look up the badge URL using IEmoteBadgeService based on the Identifier.
/// </summary>
public partial class BadgeInfoToImageSourceConverter : IValueConverter
{
    private static readonly Lazy<ILogger<BadgeInfoToImageSourceConverter>?> s_lazyLogger = new(
        App.GetService<ILogger<BadgeInfoToImageSourceConverter>>
    );
    private static readonly Lazy<IEmoteBadgeService?> s_lazyEmoteBadgeService = new(App.GetService<IEmoteBadgeService>);

    private static ILogger<BadgeInfoToImageSourceConverter>? Logger => s_lazyLogger.Value;
    private static IEmoteBadgeService? EmoteBadgeService => s_lazyEmoteBadgeService.Value;

    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not BadgeInfo badgeInfo)
        {
            Logger?.LogTrace("Input value is not a BadgeInfo object.");
            return null;
        }

        string? imageUrl = null;
        bool isSvg = false;

        if (!string.IsNullOrEmpty(badgeInfo.ImageUrl))
        {
            imageUrl = badgeInfo.ImageUrl;
            isSvg = imageUrl.EndsWith(".svg", StringComparison.OrdinalIgnoreCase);
            Logger?.LogTrace("Using ImageUrl from BadgeInfo for {Identifier}: {Url} (IsSvg={IsSvg})", badgeInfo.Identifier, imageUrl, isSvg);
        }
        else
        {
            if (EmoteBadgeService == null)
            {
                Logger?.LogError("Cannot lookup badge URL for {Identifier}: EmoteBadgeService is unavailable.", badgeInfo.Identifier);
                return null;
            }

            if (badgeInfo.Identifier.StartsWith("twitch/"))
            {
                string[] parts = badgeInfo.Identifier.Split('/');
                if (parts.Length == 3)
                {
                    imageUrl = EmoteBadgeService.GetTwitchBadgeUrl(parts[1], parts[2]);
                    if (imageUrl != null)
                        Logger?.LogTrace("Looked up Twitch badge URL for {Identifier}: {Url}", badgeInfo.Identifier, imageUrl);
                    else
                        Logger?.LogDebug("Badge URL lookup failed for Twitch identifier: {Identifier}", badgeInfo.Identifier);

                    isSvg = false;
                }
                else
                {
                    Logger?.LogWarning("Invalid Twitch badge identifier format for lookup: {Identifier}", badgeInfo.Identifier);
                }
            }
            else if (badgeInfo.Identifier.StartsWith("youtube/"))
            {
                Logger?.LogDebug(
                    "Service lookup for YouTube badge identifier '{Identifier}' not implemented (expected direct URL if available).",
                    badgeInfo.Identifier
                );
            }
            else
            {
                Logger?.LogWarning("Unknown platform prefix for badge identifier lookup: {Identifier}", badgeInfo.Identifier);
            }
        }

        if (!string.IsNullOrEmpty(imageUrl) && Uri.TryCreate(imageUrl, UriKind.Absolute, out Uri? imageUri))
        {
            try
            {
                if (isSvg)
                {
                    Logger?.LogTrace("Creating SvgImageSource for {Url}", imageUrl);
                    return new SvgImageSource(imageUri);
                }
                else
                {
                    Logger?.LogTrace("Creating BitmapImage for {Url}", imageUrl);
                    return new BitmapImage(imageUri);
                }
            }
            catch (Exception ex)
            {
                Logger?.LogError(
                    ex,
                    "Failed to create {SourceType} for badge {Identifier} from URL: {Url}",
                    isSvg ? "SvgImageSource" : "BitmapImage",
                    badgeInfo.Identifier,
                    imageUrl
                );
                return null;
            }
        }

        return null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}
