using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using StreamWeaver.Core.Models.Settings;

namespace StreamWeaver.Core.Services.Settings;

/// <summary>
/// Service responsible for loading and saving application settings to a JSON file.
/// Ensures thread-safe file access, holds a single instance of settings in memory,
/// and only saves to disk when changes are detected.
/// </summary>
public class SettingsService : ISettingsService
{
    private readonly string _settingsFilePath;
    private AppSettings? _currentSettingsInstance; // The single in-memory instance
    private string? _lastSavedSettingsJson; // Store the JSON representation of the last saved/loaded state
    private readonly ILogger<SettingsService> _logger;

    private static readonly JsonSerializerOptions s_jsonSerializerOptions = new()
    {
        WriteIndented = true,
        // Ignore null values to make comparison slightly more robust if properties are added/removed
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
    private static readonly SemaphoreSlim s_fileLock = new(1, 1); // Use SemaphoreSlim for async locking

    public event EventHandler? SettingsUpdated;

    /// <summary>
    /// Gets the singleton instance of the application settings.
    /// Loads from disk on first access if necessary.
    /// </summary>
    public AppSettings CurrentSettings => _currentSettingsInstance ??= LoadSettingsInternal(); // Load synchronously on first access

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

        // Eager load settings on construction
        _currentSettingsInstance = LoadSettingsInternal();
    }

    /// <summary>
    /// Loads settings from the file synchronously. Intended for internal use (constructor, first access).
    /// </summary>
    private AppSettings LoadSettingsInternal()
    {
        _logger.LogDebug("Acquiring lock for loading settings...");
        s_fileLock.Wait(); // Synchronous wait for initial load or direct call
        _logger.LogTrace("Lock acquired for loading settings.");
        try
        {
            AppSettings loadedSettings;
            if (!File.Exists(_settingsFilePath))
            {
                _logger.LogInformation("Settings file not found at {SettingsFilePath}. Creating default settings.", _settingsFilePath);
                loadedSettings = new();
                // Serialize the default settings to initialize _lastSavedSettingsJson
                _lastSavedSettingsJson = JsonSerializer.Serialize(loadedSettings, s_jsonSerializerOptions);
                // Attempt to save the defaults immediately
                try
                {
                    File.WriteAllText(_settingsFilePath, _lastSavedSettingsJson);
                    _logger.LogInformation("Saved default settings to file.");
                }
                catch (Exception writeEx)
                {
                    _logger.LogError(writeEx, "Failed to write initial default settings file.");
                }
            }
            else
            {
                _logger.LogInformation("Loading settings from {SettingsFilePath}...", _settingsFilePath);
                string json = File.ReadAllText(_settingsFilePath);
                try
                {
                    // Deserialize into a new object first
                    loadedSettings = JsonSerializer.Deserialize<AppSettings>(json) ?? new();
                    _lastSavedSettingsJson = json; // Store the raw JSON we just loaded for comparison
                    _logger.LogInformation("Settings deserialized successfully.");
                }
                catch (JsonException jsonEx)
                {
                    _logger.LogError(jsonEx, "Error deserializing settings JSON from {SettingsFilePath}. Backing up old file and using default settings.", _settingsFilePath);
                    BackupCorruptedSettings(json); // Attempt to backup corrupted file
                    loadedSettings = new();
                    _lastSavedSettingsJson = JsonSerializer.Serialize(loadedSettings, s_jsonSerializerOptions); // Serialize defaults for comparison state
                }
            }

            return loadedSettings;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Critical error loading settings from {SettingsFilePath}: {ErrorMessage}. Returning default settings.",
                _settingsFilePath,
                ex.Message
            );
            AppSettings defaultSettings = new();
            _lastSavedSettingsJson = JsonSerializer.Serialize(defaultSettings, s_jsonSerializerOptions);
            return defaultSettings;
        }
        finally
        {
            s_fileLock.Release();
            _logger.LogTrace("Lock released after loading settings.");
        }
    }

    /// <summary>
    /// Returns the current in-memory settings instance asynchronously.
    /// The actual loading happens synchronously on first access or during construction.
    /// </summary>
    public Task<AppSettings> LoadSettingsAsync() =>
        // Simply return the instance that was loaded/created during construction/first access.
        Task.FromResult(CurrentSettings);

    /// <summary>
    /// Saves the provided settings object to the JSON file, but *only if changes are detected*.
    /// </summary>
    /// <param name="settings">The settings object to save (should be the CurrentSettings instance).</param>
    public async Task SaveSettingsAsync(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        // Optional: Add a check to ensure the instance being saved is the singleton instance.
        if (!ReferenceEquals(settings, _currentSettingsInstance))
        {
            _logger.LogWarning("SaveSettingsAsync called with an instance potentially different from the singleton CurrentSettings. This might indicate unexpected state management. Saving the provided instance.");
            // If this happens, update the singleton instance reference
            _currentSettingsInstance = settings;
        }

        string currentSettingsJson;
        try
        {
            // Serialize the current state for comparison
            currentSettingsJson = JsonSerializer.Serialize(settings, s_jsonSerializerOptions);
        }
        catch (JsonException jsonEx)
        {
            _logger.LogError(jsonEx, "Failed to serialize current settings before saving comparison. Aborting save.");
            return; // Can't compare or save if serialization fails
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error serializing current settings before saving comparison. Aborting save.");
            return;
        }


        // Compare the current JSON state with the last known saved/loaded JSON state
        if (string.Equals(currentSettingsJson, _lastSavedSettingsJson, StringComparison.Ordinal))
        {
            _logger.LogDebug("SaveSettingsAsync skipped: No changes detected since last save/load.");
            return; // No changes, no need to save
        }

        _logger.LogDebug("Changes detected. Attempting to acquire lock for saving settings...");
        await s_fileLock.WaitAsync(); // Asynchronous wait for file access
        _logger.LogTrace("Lock acquired for saving settings.");
        try
        {
            _logger.LogInformation("Saving settings changes to {SettingsFilePath}...", _settingsFilePath);
            await File.WriteAllTextAsync(_settingsFilePath, currentSettingsJson);
            _lastSavedSettingsJson = currentSettingsJson; // Update the last saved state ONLY after successful write
            _logger.LogInformation("Settings saved successfully.");

            // Raise the event *after* successfully saving
            OnSettingsUpdated();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving settings to {SettingsFilePath}: {ErrorMessage}", _settingsFilePath, ex.Message);
            // Consider what to do on save failure. Revert _lastSavedSettingsJson?
            // For now, _lastSavedSettingsJson remains the old value, so next save will try again if changes persist.
        }
        finally
        {
            s_fileLock.Release();
            _logger.LogTrace("Lock released after saving settings.");
        }
    }

    /// <summary>
    /// Attempts to back up a settings file presumed to be corrupted.
    /// </summary>
    private void BackupCorruptedSettings(string corruptedJson)
    {
        try
        {
            string backupFileName = $"settings.corrupted.{DateTime.Now:yyyyMMddHHmmss}.json";
            string backupFilePath = Path.Combine(Path.GetDirectoryName(_settingsFilePath)!, backupFileName);
            File.WriteAllText(backupFilePath, corruptedJson);
            _logger.LogInformation("Backed up corrupted settings file to: {BackupFilePath}", backupFilePath);
        }
        catch (Exception backupEx)
        {
            _logger.LogError(backupEx, "Failed to back up corrupted settings file.");
        }
    }

    /// <summary>
    /// Raises the SettingsUpdated event.
    /// </summary>
    protected virtual void OnSettingsUpdated()
    {
        SettingsUpdated?.Invoke(this, EventArgs.Empty);
        _logger.LogDebug("SettingsUpdated event invoked.");
    }
}
