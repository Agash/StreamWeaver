using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using StreamWeaver.Core.Models.Events.Messages;

namespace StreamWeaver.UI.Helpers;

/// <summary>
/// Provides attached properties and helper methods for populating a <see cref="RichTextBlock"/>
/// from a collection of <see cref="MessageSegment"/> objects.
/// </summary>
public class RichTextBlockHelper
{
    private static readonly Lazy<ILogger<RichTextBlockHelper>?> s_lazyLogger = new(() =>
    {
        try
        {
            return App.GetService<ILogger<RichTextBlockHelper>>();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RichTextBlockHelper.StaticInit] Failed to get ILogger: {ex.Message}");
            return null;
        }
    });

    private static ILogger<RichTextBlockHelper>? Logger => s_lazyLogger.Value;

    /// <summary>
    /// Defines the SegmentsSource attached dependency property.
    /// When set on a <see cref="RichTextBlock"/>, its content will be generated based on the provided segments.
    /// </summary>
    public static readonly DependencyProperty SegmentsSourceProperty = DependencyProperty.RegisterAttached(
        "SegmentsSource",
        typeof(IEnumerable<MessageSegment>),
        typeof(RichTextBlockHelper),
        new PropertyMetadata(null, OnSegmentsSourceChanged)
    );

    /// <summary>
    /// Gets the value of the SegmentsSource attached property for a specified DependencyObject.
    /// </summary>
    /// <param name="obj">The object from which the property value is read.</param>
    /// <returns>The collection of message segments currently assigned.</returns>
    public static IEnumerable<MessageSegment>? GetSegmentsSource(DependencyObject obj) =>
        (IEnumerable<MessageSegment>?)obj.GetValue(SegmentsSourceProperty);

    /// <summary>
    /// Sets the value of the SegmentsSource attached property for a specified DependencyObject.
    /// </summary>
    /// <param name="obj">The object on which the property value is set.</param>
    /// <param name="value">The collection of message segments to assign.</param>
    public static void SetSegmentsSource(DependencyObject obj, IEnumerable<MessageSegment>? value) => obj.SetValue(SegmentsSourceProperty, value);

    /// <summary>
    /// Callback method invoked when the SegmentsSource attached property value changes.
    /// </summary>
    /// <param name="d">The DependencyObject on which the property changed (expected to be a RichTextBlock).</param>
    /// <param name="e">Event data that describes the property change.</param>
    private static void OnSegmentsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not RichTextBlock richTextBlock)
        {
            Logger?.LogWarning(
                "SegmentsSource attached property used on a non-RichTextBlock element of type {ElementType}. Ignoring change.",
                d.GetType().Name
            );
            return;
        }

        var segments = e.NewValue as IEnumerable<MessageSegment>;

        GenerateRichTextBlockContent(richTextBlock, segments);
    }

    /// <summary>
    /// Clears and regenerates the content of a <see cref="RichTextBlock"/> based on a collection of <see cref="MessageSegment"/>.
    /// </summary>
    /// <param name="richTextBlock">The RichTextBlock to populate.</param>
    /// <param name="segments">The collection of segments representing the message content.</param>
    private static void GenerateRichTextBlockContent(RichTextBlock richTextBlock, IEnumerable<MessageSegment>? segments)
    {
        richTextBlock.Blocks.Clear();

        if (segments == null)
        {
            Logger?.LogTrace("Segments collection is null. RichTextBlock cleared.");
            return;
        }

        var paragraph = new Paragraph();
        bool paragraphHasContent = false;

        try
        {
            foreach (MessageSegment segment in segments)
            {
                if (segment is TextSegment textSegment && !string.IsNullOrEmpty(textSegment.Text))
                {
                    paragraph.Inlines.Add(new Run { Text = textSegment.Text });
                    paragraphHasContent = true;
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

                            VerticalAlignment = VerticalAlignment.Center,

                            Margin = new Thickness(1, 0, 1, -8),
                        };

                        ToolTipService.SetToolTip(image, new ToolTip { Content = emoteSegment.Name });

                        var inlineContainer = new InlineUIContainer { Child = image };
                        paragraph.Inlines.Add(inlineContainer);
                        paragraphHasContent = true;
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
                        paragraphHasContent = true;
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
                        paragraphHasContent = true;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Error processing segments collection.");

            paragraph.Inlines.Clear();

            paragraph.Inlines.Add(new Run { Text = "[Error displaying message content]" });
            paragraphHasContent = true;
        }

        if (paragraphHasContent)
        {
            richTextBlock.Blocks.Add(paragraph);
        }
    }
}
