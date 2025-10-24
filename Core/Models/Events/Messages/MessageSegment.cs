using System.Text.Json.Serialization;

namespace StreamWeaver.Core.Models.Events.Messages;

/// <summary>
/// Base class for a segment of a parsed chat message.
/// </summary>
[JsonDerivedType(typeof(TextSegment), typeDiscriminator: "TextSegment")]
[JsonDerivedType(typeof(EmoteSegment), typeDiscriminator: "EmoteSegment")]
public abstract class MessageSegment { }
