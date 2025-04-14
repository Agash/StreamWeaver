using System.Text.Json;
using Microsoft.Extensions.Logging;
using StreamWeaver.Core.Models.Settings;

namespace StreamWeaver.Core.Services.Settings;

/// <summary>
/// Service responsible for loading and saving application settings to a JSON file.
/// Ensures thread-safe file access and handles potential deserialization issues.
/// </summary>
public class SettingsService : ISettingsService
{
    private readonly string _settingsFilePath;
    private AppSettings? _currentSettings;
    private readonly ILogger<SettingsService> _logger;

    private static readonly JsonSerializerOptions s_jsonSerializerOptions = new() { WriteIndented = true };
    private static readonly SemaphoreSlim s_fileLock = new(1, 1);

    public event EventHandler? SettingsUpdated;

    public AppSettings CurrentSettings => _currentSettings ??= new AppSettings();

    public SettingsService(ILogger<SettingsService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        string appDataFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string appFolder = Path.Combine(appDataFolder, "StreamWeaver");
        _settingsFilePath = Path.Combine(appFolder, "settings.json");

        _logger.LogInformation("Settings file path configured: {SettingsFilePath}", _settingsFilePath);

        try
        {
            Directory.CreateDirectory(appFolder);
            _logger.LogDebug("Ensured settings directory exists: {DirectoryPath}", appFolder);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Failed to create settings directory: {DirectoryPath}. Settings persistence will fail.", appFolder);
        }
    }

    public async Task<AppSettings> LoadSettingsAsync()
    {
        _logger.LogDebug("Attempting to acquire lock for loading settings...");
        await s_fileLock.WaitAsync();
        _logger.LogTrace("Lock acquired for loading settings.");
        try
        {
            AppSettings loadedSettings;
            if (!File.Exists(_settingsFilePath))
            {
                _logger.LogInformation("Settings file not found at {SettingsFilePath}. Creating default settings.", _settingsFilePath);
                loadedSettings = new AppSettings();
            }
            else
            {
                _logger.LogInformation("Loading settings from {SettingsFilePath}...", _settingsFilePath);
                string json = await File.ReadAllTextAsync(_settingsFilePath);
                try
                {
                    loadedSettings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                    _logger.LogInformation("Settings deserialized successfully.");
                }
                catch (JsonException jsonEx)
                {
                    _logger.LogError(jsonEx, "Error deserializing settings JSON from {SettingsFilePath}. Using default settings.", _settingsFilePath);
                    loadedSettings = new AppSettings();
                }
            }

            loadedSettings.Credentials ??= new();
            loadedSettings.Connections ??= new();
            loadedSettings.Connections.TwitchAccounts ??= [];
            loadedSettings.Connections.YouTubeAccounts ??= [];
            loadedSettings.TextToSpeech ??= new();
            loadedSettings.Overlays ??= new();
            loadedSettings.Overlays.Chat ??= new();
            loadedSettings.Modules ??= new();
            loadedSettings.Modules.Subathon ??= new();
            loadedSettings.Modules.Goals ??= new();

            _currentSettings = loadedSettings;
            return _currentSettings;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error loading settings from {SettingsFilePath}: {ErrorMessage}. Returning default settings.",
                _settingsFilePath,
                ex.Message
            );
            _currentSettings = new AppSettings();
            return _currentSettings;
        }
        finally
        {
            s_fileLock.Release();
            _logger.LogTrace("Lock released after loading settings.");
        }
    }

    public async Task SaveSettingsAsync(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        settings.Credentials ??= new();
        settings.Connections ??= new();
        settings.Connections.TwitchAccounts ??= [];
        settings.Connections.YouTubeAccounts ??= [];
        settings.TextToSpeech ??= new();
        settings.Overlays ??= new();
        settings.Overlays.Chat ??= new();
        settings.Modules ??= new();
        settings.Modules.Subathon ??= new();
        settings.Modules.Goals ??= new();

        _logger.LogDebug("Attempting to acquire lock for saving settings...");
        await s_fileLock.WaitAsync();
        _logger.LogTrace("Lock acquired for saving settings.");
        try
        {
            _logger.LogInformation("Saving settings to {SettingsFilePath}...", _settingsFilePath);
            string json = JsonSerializer.Serialize(settings, s_jsonSerializerOptions);
            await File.WriteAllTextAsync(_settingsFilePath, json);

            _currentSettings = settings;

            _logger.LogInformation("Settings saved successfully.");
            SettingsUpdated?.Invoke(this, EventArgs.Empty);
            _logger.LogDebug("SettingsUpdated event invoked.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving settings to {SettingsFilePath}: {ErrorMessage}", _settingsFilePath, ex.Message);
        }
        finally
        {
            s_fileLock.Release();
            _logger.LogTrace("Lock released after saving settings.");
        }
    }
}
