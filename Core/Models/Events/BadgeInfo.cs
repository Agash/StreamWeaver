namespace StreamWeaver.Core.Models.Events;

/// <summary>
/// Represents information about a single chat badge.
/// </summary>
/// <param name="Identifier">A unique string identifying the badge (e.g., "twitch/moderator/1", "youtube/member/1").</param>
/// <param name="ImageUrl">An optional, directly available URL for the badge image. If null, lookup via service is needed.</param>
public record BadgeInfo(string Identifier, string? ImageUrl = null);
