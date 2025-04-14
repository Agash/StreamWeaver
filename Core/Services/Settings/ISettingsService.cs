using StreamWeaver.Core.Models.Settings;

namespace StreamWeaver.Core.Services.Settings;

public interface ISettingsService
{
    Task<AppSettings> LoadSettingsAsync();
    Task SaveSettingsAsync(AppSettings settings);
    AppSettings CurrentSettings { get; }
    event EventHandler SettingsUpdated;
}
