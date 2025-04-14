using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using StreamWeaver.Core.Models.Events.Messages;

namespace StreamWeaver.UI.Converters;

/// <summary>
/// Converts a list of <see cref="MessageSegment"/> objects into a list of XAML <see cref="Block"/> elements
/// (specifically, a single <see cref="Paragraph"/> containing <see cref="Run"/> and <see cref="InlineUIContainer"/> elements)
/// suitable for display in a RichTextBlock.
/// </summary>
public partial class SegmentsToBlocksConverter : IValueConverter
{
    private static readonly Lazy<ILogger<SegmentsToBlocksConverter>?> s_lazyLogger = new(() =>
    {
        try
        {
            return App.GetService<ILogger<SegmentsToBlocksConverter>>();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SegmentsToBlocksConverter.StaticInit] Failed to get ILogger: {ex.Message}");
            return null;
        }
    });

    private static ILogger<SegmentsToBlocksConverter>? Logger => s_lazyLogger.Value;

    private const double EmoteHeight = 24;

    /// <summary>
    /// Converts a list of message segments into XAML blocks.
    /// </summary>
    /// <param name="value">A <see cref="List{MessageSegment}"/> to convert.</param>
    /// <param name="targetType">The type of the target property (expected to be IEnumerable<Block> or compatible).</param>
    /// <param name="parameter">An optional parameter (not used).</param>
    /// <param name="language">The language/culture (not used).</param>
    /// <returns>A <see cref="List{Block}"/> containing a single Paragraph representing the message, or an empty list/error block on failure.</returns>
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not List<MessageSegment> segments || segments.Count == 0)
        {
            Logger?.LogTrace("Input value is null, not a List<MessageSegment>, or is empty. Value: {Value}", value);

            return new List<Block>();
        }

        var paragraph = new Paragraph();

        try
        {
            foreach (MessageSegment segment in segments)
            {
                if (segment is TextSegment textSegment && !string.IsNullOrEmpty(textSegment.Text))
                {
                    paragraph.Inlines.Add(new Run { Text = textSegment.Text });
                }
                else if (segment is EmoteSegment emoteSegment && !string.IsNullOrEmpty(emoteSegment.ImageUrl))
                {
                    try
                    {
                        var image = new Image
                        {
                            Source = new BitmapImage(new Uri(emoteSegment.ImageUrl)),
                            Height = EmoteHeight,
                            Width = double.NaN,
                            Stretch = Stretch.Uniform,

                            VerticalAlignment = VerticalAlignment.Bottom,
                        };

                        ToolTipService.SetToolTip(image, new ToolTip { Content = emoteSegment.Name });

                        var inlineContainer = new InlineUIContainer { Child = image };
                        paragraph.Inlines.Add(inlineContainer);
                    }
                    catch (FormatException uriEx)
                    {
                        Logger?.LogError(
                            uriEx,
                            "Invalid ImageUrl format for emote '{EmoteName}'. URL: {ImageUrl}",
                            emoteSegment.Name,
                            emoteSegment.ImageUrl
                        );

                        paragraph.Inlines.Add(new Run { Text = $"[{emoteSegment.Name}?]" });
                    }
                    catch (Exception imgEx)
                    {
                        Logger?.LogError(
                            imgEx,
                            "Error creating/loading image for emote '{EmoteName}'. URL: {ImageUrl}",
                            emoteSegment.Name,
                            emoteSegment.ImageUrl
                        );

                        paragraph.Inlines.Add(new Run { Text = $"[{emoteSegment.Name}?]" });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Error processing message segments.");

            var errorParagraph = new Paragraph();
            errorParagraph.Inlines.Add(new Run { Text = "[Error displaying message content]" });
            return new List<Block> { errorParagraph };
        }

        return new List<Block> { paragraph };
    }

    /// <summary>
    /// Converts a value back - Not implemented for this converter.
    /// </summary>
    /// <exception cref="NotImplementedException">Always thrown.</exception>
    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        Logger?.LogWarning("ConvertBack called but is not implemented.");

        throw new NotImplementedException("SegmentsToBlocksConverter does not support ConvertBack.");
    }
}
