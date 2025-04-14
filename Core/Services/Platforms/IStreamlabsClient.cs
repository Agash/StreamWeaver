using StreamWeaver.Core.Models.Settings;

namespace StreamWeaver.Core.Services.Platforms;

public interface IStreamlabsClient
{
    ConnectionStatus Status { get; }
    string? StatusMessage { get; }

    Task<bool> ConnectAsync(string socketToken);
    Task DisconnectAsync();
}
