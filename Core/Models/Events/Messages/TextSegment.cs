namespace StreamWeaver.Core.Models.Events.Messages;

/// <summary>
/// Represents a plain text part of a message.
/// </summary>
public class TextSegment : MessageSegment
{
    public required string Text { get; set; }

    public override string ToString() => Text;
}
