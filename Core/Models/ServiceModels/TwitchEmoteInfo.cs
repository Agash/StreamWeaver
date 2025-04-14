namespace StreamWeaver.Core.Models.ServiceModels;

// Represents information about a single Twitch emote.
public record TwitchEmoteInfo(
    string Id, // Emote ID from Twitch
    string Name, // Emote code (e.g., "Kappa")
    string ImageUrl1x, // URL for 1x size
    string ImageUrl2x, // URL for 2x size
    string ImageUrl4x, // URL for 4x size
    string Format, // e.g., "static", "animated"
    string Scale, // e.g., "1.0", "2.0", "3.0"
    string ThemeMode // e.g., "light", "dark"
)
{
    public string GetUrlForSize(string size = "1.0") =>
        size switch
        {
            "2.0" => ImageUrl2x,
            "3.0" => ImageUrl4x, // Twitch uses 4x for 3.0 scale
            _ => ImageUrl1x,
        };
}
