namespace StreamWeaver.Core.Models.PlatformData;

public class UserInfo
{
    public string Platform { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string LoginName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? ProfileImageUrl { get; set; }
}
