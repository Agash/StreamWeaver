// StreamWeaver.Core/Services/Settings/SettingsService.cs
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration; // Required for IConfigurationRoot
using Microsoft.Extensions.Logging;
using StreamWeaver.Core.Models.Settings;

namespace StreamWeaver.Core.Services.Settings;

public class SettingsService : ISettingsService
{
    private readonly string _settingsFilePath;
    private AppSettings _currentSettingsInstance; // Removed nullable, initialized in constructor
    private string? _lastSavedSettingsJson;
    private readonly ILogger<SettingsService> _logger;
    private readonly IConfigurationRoot _configurationRoot; // To trigger reloads

    private static readonly JsonSerializerOptions s_jsonSerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // Ensure all custom converters needed by AppSettings and its children are registered here
        // if you encounter serialization issues with complex types.
    };
    private static readonly SemaphoreSlim s_fileLock = new(1, 1);

    public event EventHandler? SettingsUpdated;

    // CurrentSettings getter now ensures _currentSettingsInstance is not null
    public AppSettings CurrentSettings => _currentSettingsInstance;

    public SettingsService(ILogger<SettingsService> logger, IConfigurationRoot configurationRoot)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configurationRoot = configurationRoot ?? throw new ArgumentNullException(nameof(configurationRoot));

        string appDataFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string appFolder = Path.Combine(appDataFolder, "StreamWeaver");
        _settingsFilePath = Path.Combine(appFolder, "user_settings.json"); // Ensure this is the user settings path

        _logger.LogInformation("SettingsService initialized. User settings file path: {SettingsFilePath}", _settingsFilePath);
        try
        {
            Directory.CreateDirectory(appFolder);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Failed to create settings directory: {DirectoryPath}", appFolder);
        }
        // Load settings immediately in the constructor
        _currentSettingsInstance = LoadSettingsInternal();
    }

    private AppSettings LoadSettingsInternal()
    {
        _logger.LogDebug("Acquiring lock for loading settings from {FilePath}...", _settingsFilePath);
        s_fileLock.Wait(); // Synchronous wait for constructor context
        _logger.LogTrace("Lock acquired for loading settings.");
        try
        {
            AppSettings loadedSettings;
            if (!File.Exists(_settingsFilePath))
            {
                _logger.LogInformation("User settings file not found at {SettingsFilePath}. Creating default settings.", _settingsFilePath);
                loadedSettings = new AppSettings(); // Create new default instance
                _lastSavedSettingsJson = JsonSerializer.Serialize(loadedSettings, s_jsonSerializerOptions);
                try
                {
                    File.WriteAllText(_settingsFilePath, _lastSavedSettingsJson);
                    _logger.LogInformation("Saved default user settings to file.");
                }
                catch (Exception writeEx)
                {
                    _logger.LogError(writeEx, "Failed to write initial default user settings file.");
                }
            }
            else
            {
                _logger.LogInformation("Loading user settings from {SettingsFilePath}...", _settingsFilePath);
                string json = File.ReadAllText(_settingsFilePath);
                try
                {
                    loadedSettings = JsonSerializer.Deserialize<AppSettings>(json, s_jsonSerializerOptions) ?? new AppSettings();
                    _lastSavedSettingsJson = json;
                    _logger.LogInformation("User settings deserialized successfully.");
                }
                catch (JsonException jsonEx)
                {
                    _logger.LogError(
                        jsonEx,
                        "Error deserializing user settings JSON from {SettingsFilePath}. Backing up old file and using default settings.",
                        _settingsFilePath
                    );
                    BackupCorruptedSettings(json);
                    loadedSettings = new AppSettings();
                    _lastSavedSettingsJson = JsonSerializer.Serialize(loadedSettings, s_jsonSerializerOptions);
                }
            }
            return loadedSettings;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Critical error loading user settings. Returning default settings.");
            var defaultSettings = new AppSettings();
            _lastSavedSettingsJson = JsonSerializer.Serialize(defaultSettings, s_jsonSerializerOptions);
            return defaultSettings;
        }
        finally
        {
            s_fileLock.Release();
            _logger.LogTrace("Lock released after loading settings.");
        }
    }

    public Task<AppSettings> LoadSettingsAsync()
    {
        // This method can now simply return the already loaded (or default) settings.
        // The constructor ensures _currentSettingsInstance is initialized.
        _logger.LogDebug("LoadSettingsAsync called, returning current instance.");
        return Task.FromResult(CurrentSettings);
    }

    public async Task SaveSettingsAsync(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!ReferenceEquals(settings, _currentSettingsInstance))
        {
            _logger.LogDebug("SaveSettingsAsync called with a different AppSettings instance. Updating internal _currentSettingsInstance to match.");
            _currentSettingsInstance = settings;
        }

        string currentSettingsJson;
        try
        {
            // Serialize the instance that the application (and UI) is actually using.
            currentSettingsJson = JsonSerializer.Serialize(_currentSettingsInstance, s_jsonSerializerOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to serialize settings for saving. Aborting save.");
            return;
        }

        if (string.Equals(currentSettingsJson, _lastSavedSettingsJson, StringComparison.Ordinal))
        {
            _logger.LogInformation("SaveSettingsAsync skipped: No changes detected since last save/load.");
            return;
        }

        _logger.LogDebug("Changes detected. Acquiring lock for saving user settings to {FilePath}...", _settingsFilePath);
        await s_fileLock.WaitAsync();
        _logger.LogTrace("Lock acquired for saving user settings.");
        try
        {
            _logger.LogInformation("Saving user settings changes to {SettingsFilePath}...", _settingsFilePath);
            await File.WriteAllTextAsync(_settingsFilePath, currentSettingsJson);
            _lastSavedSettingsJson = currentSettingsJson;
            _logger.LogInformation("User settings saved successfully.");

            _logger.LogDebug("Reloading IConfigurationRoot to apply changes and trigger IOptionsMonitor updates...");
            _configurationRoot.Reload();
            _logger.LogInformation("IConfigurationRoot reloaded.");

            OnSettingsUpdated();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving user settings to {SettingsFilePath}", _settingsFilePath);
            // Consider how to handle save failures - perhaps notify the user.
        }
        finally
        {
            s_fileLock.Release();
            _logger.LogTrace("Lock released after saving user settings.");
        }
    }

    private void BackupCorruptedSettings(string corruptedJson)
    {
        try
        {
            string backupFileName = $"user_settings.corrupted.{DateTime.Now:yyyyMMddHHmmss}.json";
            string backupFilePath = Path.Combine(Path.GetDirectoryName(_settingsFilePath)!, backupFileName);
            File.WriteAllText(backupFilePath, corruptedJson);
            _logger.LogInformation("Backed up corrupted user settings file to: {BackupFilePath}", backupFilePath);
        }
        catch (Exception backupEx)
        {
            _logger.LogError(backupEx, "Failed to back up corrupted user settings file.");
        }
    }

    protected virtual void OnSettingsUpdated()
    {
        SettingsUpdated?.Invoke(this, EventArgs.Empty);
        _logger.LogDebug("SettingsUpdated event invoked by SettingsService.");
    }
}
