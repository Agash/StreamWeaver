using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using StreamWeaver.Core.Models.Events;
using StreamWeaver.Core.Models.Events.Messages;
using StreamWeaver.UI.Converters;
using Windows.UI.Text;

namespace StreamWeaver.UI.Helpers;

/// <summary>
/// Helper class to populate a RichTextBlock based on a ChatMessageEvent object.
/// Builds a Paragraph containing all necessary inline elements (timestamp, icons, badges, name, message).
/// </summary>
public class RichTextBlockEventHelper
{
    private static readonly Lazy<ILogger<RichTextBlockEventHelper>?> s_lazyLogger = new(App.GetService<ILogger<RichTextBlockEventHelper>>);
    private static readonly Lazy<PlatformToBrushConverter> s_platformBrushConverter = new();
    private static readonly Lazy<StringToBrushConverter> s_stringBrushConverter = new();
    private static readonly Lazy<BadgeInfoToImageSourceConverter> s_badgeConverter = new();
    private static readonly Lazy<DateTimeFormatConverter> s_timeConverter = new();

    private static ILogger<RichTextBlockEventHelper>? Logger => s_lazyLogger.Value;

    // Define Brushes
    private static readonly SolidColorBrush s_ownerBackgroundBrush = new(Microsoft.UI.Colors.Gold); // Yellow background
    private static readonly SolidColorBrush s_ownerForegroundBrush = new(Microsoft.UI.Colors.Black); // Black text/icon
    private static readonly SolidColorBrush s_defaultUsernameFallbackBrush = (SolidColorBrush)
        Application.Current.Resources["SystemControlForegroundBaseMediumBrush"];

    public static readonly DependencyProperty ChatEventSourceProperty = DependencyProperty.RegisterAttached(
        "ChatEventSource",
        typeof(ChatMessageEvent),
        typeof(RichTextBlockEventHelper),
        new PropertyMetadata(null, OnChatEventSourceChanged)
    );

    public static ChatMessageEvent GetChatEventSource(DependencyObject obj) => (ChatMessageEvent)obj.GetValue(ChatEventSourceProperty);

    public static void SetChatEventSource(DependencyObject obj, ChatMessageEvent value) => obj.SetValue(ChatEventSourceProperty, value);

    private static void OnChatEventSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not RichTextBlock richTextBlock)
        {
            Logger?.LogWarning("ChatEventSource attached property used on a non-RichTextBlock element.");
            return;
        }

        // Ensure execution on the UI thread
        DispatcherQueue dispatcherQueue = richTextBlock.DispatcherQueue ?? DispatcherQueue.GetForCurrentThread();
        if (dispatcherQueue == null)
        {
            Logger?.LogError("Could not get DispatcherQueue for RichTextBlock. Cannot update content.");
            return;
        }

        bool enqueued = dispatcherQueue.TryEnqueue(() =>
        {
            richTextBlock.Blocks.Clear(); // Clear previous content
            if (e.NewValue is ChatMessageEvent chatEvent)
            {
                // Build the Paragraph containing all inlines
                Paragraph? paragraph = BuildParagraph(chatEvent);
                if (paragraph != null)
                {
                    try
                    {
                        richTextBlock.Blocks.Add(paragraph);
                    }
                    catch (Exception ex) // Catch potential errors adding the block
                    {
                        Logger?.LogError(ex, "Error adding Paragraph to RichTextBlock for Event ID: {EventId}", chatEvent.Id);
                        richTextBlock.Blocks.Clear();
                        Paragraph errorParagraph = new();
                        errorParagraph.Inlines.Add(new Run { Text = "[Display Error]", Foreground = new SolidColorBrush(Microsoft.UI.Colors.Red) });
                        richTextBlock.Blocks.Add(errorParagraph);
                    }
                }
            }
        });
        if (!enqueued)
        {
            Logger?.LogError("Failed to enqueue RichTextBlock update.");
        }
    }

    // Builds the single Paragraph containing all inline elements
    private static Paragraph? BuildParagraph(ChatMessageEvent chatEvent)
    {
        Logger?.LogTrace(
            "BuildParagraph START for Event ID: {EventId}, IsOwner={IsOwner}, Username={Username}",
            chatEvent?.Id,
            chatEvent?.IsOwner,
            chatEvent?.Username
        );
        Paragraph paragraph = new() { LineStackingStrategy = LineStackingStrategy.BlockLineHeight };
        InlineCollection inlines = paragraph.Inlines;

        // Determine the effective foreground color for icons/badges based on owner status
        Brush fallbackUsernameBrush =
            (Brush?)s_stringBrushConverter.Value.Convert(chatEvent?.UsernameColor, typeof(Brush), s_defaultUsernameFallbackBrush, "")
            ?? s_defaultUsernameFallbackBrush;

        Brush effectiveBadgeColor = (chatEvent?.IsOwner ?? false) ? s_ownerForegroundBrush : fallbackUsernameBrush;

        try
        {
            // 1. Timestamp
            var timestampRun = new Run
            {
                Text = (string)s_timeConverter.Value.Convert(chatEvent?.Timestamp, typeof(string), "HH:mm", ""),
                FontSize = 10,
                Foreground = (SolidColorBrush)Application.Current.Resources["SystemControlPageTextBaseMediumBrush"],
            };
            inlines.Add(timestampRun);
            inlines.Add(new Run { Text = " " });

            // 2. Platform Icon
            var platformIcon = new FontIcon
            {
                FontFamily = (FontFamily)Application.Current.Resources["SymbolThemeFontFamily"],
                Glyph = GetPlatformGlyph(chatEvent?.Platform),
                FontSize = 14,
                Foreground = (Brush)s_platformBrushConverter.Value.Convert(chatEvent?.Platform, typeof(Brush), null!, ""),
                Margin = new Thickness(0, 0, 4, -2),
            };
            ToolTipService.SetToolTip(platformIcon, new ToolTip { Content = chatEvent?.Platform });
            var platformIconContainer = new InlineUIContainer { Child = platformIcon };
            inlines.Add(platformIconContainer);

            // 3. Username (Handle Owner Styling with Border)
            if (chatEvent?.IsOwner ?? false)
            {
                var ownerUsernameTextBlock = new TextBlock
                {
                    Text = chatEvent.Username,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = s_ownerForegroundBrush,
                };
                var ownerUsernameBorder = new Border
                {
                    Background = s_ownerBackgroundBrush,
                    CornerRadius = new CornerRadius(2),
                    Padding = new Thickness(3, 0, 3, 1),
                    Child = ownerUsernameTextBlock,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, -2),
                };
                var ownerUsernameContainer = new InlineUIContainer { Child = ownerUsernameBorder };
                Logger?.LogTrace("Adding Owner Username Container...");
                inlines.Add(ownerUsernameContainer);
            }
            else
            {
                // Add standard username Run using the calculated brush
                var usernameRun = new Run
                {
                    Text = chatEvent?.Username,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = fallbackUsernameBrush,
                };
                Logger?.LogTrace("Adding Standard Username Run with calculated color.");
                inlines.Add(usernameRun);
            }

            // 4. Badges
            if (chatEvent?.Badges != null && chatEvent.Badges.Count > 0)
            {
                inlines.Add(new Run { Text = " " }); // Space after Username

                for (int i = 0; i < chatEvent.Badges.Count; i++)
                {
                    BadgeInfo badgeInfo = chatEvent.Badges[i];
                    FrameworkElement? badgeElement = null;

                    Logger?.LogTrace(
                        "Processing badge {Index}: {Identifier} (HasURL={HasUrl})",
                        i,
                        badgeInfo.Identifier,
                        !string.IsNullOrEmpty(badgeInfo.ImageUrl)
                    );

                    // --- Attempt to get ImageSource using the converter first ---
                    ImageSource? badgeImageSource = (ImageSource?)s_badgeConverter.Value.Convert(badgeInfo, typeof(ImageSource), null!, "");

                    if (badgeImageSource != null)
                    {
                        // --- Use Image or SvgImageSource from Converter ---
                        if (badgeImageSource is SvgImageSource svgSource)
                        {
                            // Use a standard Image control to display the SvgImageSource
                            badgeElement = new Image
                            {
                                Source = svgSource,
                                Height = 18,
                                Width = 18,
                                VerticalAlignment = VerticalAlignment.Center,
                                Margin = new Thickness(0, 0, i < chatEvent.Badges.Count - 1 ? 2 : 3, -2),
                            };
                            Logger?.LogTrace("--> Using SvgImageSource for {Identifier} from converter.", badgeInfo.Identifier);
                        }
                        else // Assume BitmapImage or other non-SVG source
                        {
                            badgeElement = new Image
                            {
                                Source = badgeImageSource,
                                Height = 18,
                                Width = 18,
                                VerticalAlignment = VerticalAlignment.Center,
                                Margin = new Thickness(0, 0, i < chatEvent.Badges.Count - 1 ? 2 : 3, -2),
                            };
                            Logger?.LogTrace("--> Using BitmapImage for {Identifier} from converter.", badgeInfo.Identifier);
                        }
                    }
                    else
                    {
                        // --- Converter failed or returned null, try fallback to standard FontIcon glyphs ---
                        string glyph = GetStandardYouTubeBadgeGlyph(badgeInfo.Identifier); // Check only YT standard glyphs
                        if (!string.IsNullOrEmpty(glyph))
                        {
                            badgeElement = new FontIcon
                            {
                                FontFamily = (FontFamily)Application.Current.Resources["SymbolThemeFontFamily"],
                                Glyph = glyph,
                                FontSize = 16,
                                Foreground =
                                    badgeInfo.Identifier == "youtube/verified/1"
                                        ? (SolidColorBrush)Application.Current.Resources["SystemControlPageTextBaseMediumBrush"]
                                        : effectiveBadgeColor, // Apply color calculated based on owner status
                                Margin = new Thickness(0, 0, i < chatEvent.Badges.Count - 1 ? 2 : 3, -2),
                                VerticalAlignment = VerticalAlignment.Center,
                            };
                            Logger?.LogTrace(
                                "--> Converter failed/null for {Identifier}, using FontIcon fallback (Glyph: {Glyph}).",
                                badgeInfo.Identifier,
                                glyph
                            );
                        }
                        else
                        {
                            Logger?.LogWarning(
                                "--> Could not resolve badge '{Identifier}' via converter or standard glyph fallback. Skipping badge.",
                                badgeInfo.Identifier
                            );
                        }
                    }

                    if (badgeElement != null)
                    {
                        ToolTipService.SetToolTip(badgeElement, new ToolTip { Content = badgeInfo.Identifier });
                        inlines.Add(new InlineUIContainer { Child = badgeElement });
                    }
                }
            }

            // 5. Colon or Space
            if (!chatEvent?.IsActionMessage ?? false)
            {
                inlines.Add(new Run { Text = ": " });
            }
            else
            {
                inlines.Add(new Run { Text = " " });
            }

            // 6. Message Segments
            if (chatEvent?.ParsedMessage != null)
            {
                if (chatEvent.IsActionMessage)
                {
                    var span = new Span { FontStyle = FontStyle.Italic };
                    PopulateInlinesFromSegments(span.Inlines, chatEvent.ParsedMessage);
                    inlines.Add(span);
                }
                else
                {
                    PopulateInlinesFromSegments(inlines, chatEvent.ParsedMessage);
                }
            }

            Logger?.LogTrace("BuildParagraph END for Event ID: {EventId}. Inline count: {Count}", chatEvent?.Id, inlines.Count);
            return paragraph;
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Error building Paragraph for ChatMessageEvent (ID: {EventId})", chatEvent?.Id);
            Paragraph errorParagraph = new();
            errorParagraph.Inlines.Add(new Run { Text = "[Error building message]", Foreground = new SolidColorBrush(Microsoft.UI.Colors.Red) });
            return errorParagraph;
        }
    }

    // Populates an InlineCollection from MessageSegments (Handles Text and Emotes)
    private static void PopulateInlinesFromSegments(InlineCollection targetInlines, List<MessageSegment> segments)
    {
        for (int i = 0; i < segments.Count; i++)
        {
            MessageSegment segment = segments[i];
            if (segment is TextSegment textSegment && !string.IsNullOrEmpty(textSegment.Text))
            {
                targetInlines.Add(new Run { Text = textSegment.Text });
            }
            else if (segment is EmoteSegment emoteSegment && !string.IsNullOrEmpty(emoteSegment.ImageUrl))
            {
                try
                {
                    var image = new Image
                    {
                        Source = new BitmapImage(new Uri(emoteSegment.ImageUrl)),
                        Height = 24,
                        Width = double.NaN,
                        Stretch = Stretch.Uniform,
                        VerticalAlignment = VerticalAlignment.Bottom,
                        Margin = new Thickness(1, 0, 1, -5),
                    };
                    ToolTipService.SetToolTip(image, new ToolTip { Content = emoteSegment.Name });
                    targetInlines.Add(new InlineUIContainer { Child = image });
                }
                catch (Exception imgEx)
                {
                    Logger?.LogError(imgEx, "Error creating image for emote {EmoteName}", emoteSegment.Name);
                    targetInlines.Add(new Run { Text = $"[{emoteSegment.Name}?]" });
                }
            }
            else
            {
                Logger?.LogWarning(
                    "Unknown or empty segment type encountered during inline population: {SegmentType}",
                    segment?.GetType().Name ?? "null"
                );
            }
        }
    }

    // Helper method to get glyph for *standard* YouTube badges (only used as fallback)
    private static string GetStandardYouTubeBadgeGlyph(string identifier) =>
        identifier switch
        {
            "youtube/owner/1" => "\uE736", // Crown (King)
            "youtube/moderator/1" => "\uE90F", // Wrench (Settings)
            "youtube/verified/1" => "\uE73E", // CheckMark (Simple, non-encircled)
            _ => "", // No glyph for unknown or non-standard YT badges (like member)
        };

    // Helper method to get platform icon glyph
    private static string GetPlatformGlyph(string? platform) =>
        platform?.ToLowerInvariant() switch
        {
            // Consider more specific platform icons if available
            "twitch" => "\uE90A", // Placeholder - Maybe find a dedicated Twitch icon? (None standard in MDL2/Fluent)
            "youtube" => "\uE786", // Placeholder - YouTube glyph
            _ => "\uE783", // BlockContact/Unknown (IncidentTriangle E7BA might also work for unknown)
        };
}
