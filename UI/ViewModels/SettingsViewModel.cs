using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.WinUI;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using StreamWeaver.Core.Messaging;
using StreamWeaver.Core.Models.Settings;
using StreamWeaver.Core.Plugins;
using StreamWeaver.Core.Services;
using StreamWeaver.Core.Services.Authentication;
using StreamWeaver.Core.Services.Platforms;
using StreamWeaver.Core.Services.Settings;
using StreamWeaver.Core.Services.Tts;
using StreamWeaver.Core.Services.Web;
using StreamWeaver.Modules.Goals;
using StreamWeaver.Modules.Subathon;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue;

namespace StreamWeaver.UI.ViewModels;

/// <summary>
/// Represents a navigable section within the settings view.
/// </summary>
public class SettingsSection
{
    /// <summary>Gets the display name of the settings section.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the Segoe MDL2 Assets glyph code for the section icon.</summary>
    public required string Glyph { get; init; }

    /// <summary>Gets the unique tag used to identify and navigate to the section.</summary>
    public required string Tag { get; init; }
}

/// <summary>
/// ViewModel for the main settings view (<see cref="StreamWeaver.UI.Views.SettingsView"/>).
/// Provides access to application settings, manages UI state related to settings,
/// and exposes commands for actions like saving settings, testing TTS, managing accounts, etc.
/// It interacts with various core services to load/save data and trigger actions.
/// </summary>
public partial class SettingsViewModel : ObservableObject, IRecipient<ConnectionsUpdatedMessage>, IDisposable
{
    private readonly ILogger<SettingsViewModel> _logger;
    private readonly ISettingsService _settingsService;
    private readonly ITtsService _ttsService;
    private readonly UnifiedEventService _unifiedEventService;
    private readonly IMessenger _messenger;
    private readonly IStreamlabsClient _streamlabsClient;
    private readonly ITokenStorageService _tokenStorage;
    private readonly ITwitchClient _twitchClient;
    private readonly IYouTubeClient _youTubeClient;
    private readonly PluginService _pluginService;
    private readonly DispatcherQueue _dispatcherQueue;
    private bool _isDisposed = false;

    /// <summary>
    /// Collection of available Windows TTS voices installed on the system.
    /// </summary>
    [ObservableProperty]
    public partial ObservableCollection<string> WindowsVoices { get; set; } = [];

    /// <summary>
    /// Collection of available KokoroSharp voices.
    /// </summary>
    [ObservableProperty]
    public partial ObservableCollection<string> KokoroVoices { get; set; } = [];

    /// <summary>
    /// Collection of available TTS Engine options for the UI.
    /// </summary>
    public ObservableCollection<string> TtsEngineOptions { get; } = [TtsSettings.WindowsEngine, TtsSettings.KokoroEngine];

    /// <summary>
    /// TTS Test Phrase that can be modified by the user.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TestTtsCommand))]
    public partial string? TtsTestPhrase { get; set; } = "This is a test of the text to speech engine."; 

    /// <summary>
    /// The calculated URL for the chat browser source overlay.
    /// </summary>
    [ObservableProperty]
    public partial string ChatOverlayUrl { get; set; } = string.Empty;

    /// <summary>
    /// The current connection status of the Streamlabs client.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDisableStreamlabs))]
    [NotifyCanExecuteChangedFor(nameof(DisableStreamlabsCommand))]
    public partial ConnectionStatus StreamlabsStatus { get; set; } = ConnectionStatus.Disconnected;

    /// <summary>
    /// The status message provided by the Streamlabs client (e.g., error details).
    /// </summary>
    [ObservableProperty]
    public partial string? StreamlabsStatusMessage { get; set; }

    /// <summary>
    /// The collection of sections available for navigation within the settings view.
    /// </summary>
    [ObservableProperty]
    public partial ObservableCollection<SettingsSection> SettingsSections { get; set; } = [];

    /// <summary>
    /// The currently selected settings section being displayed.
    /// </summary>
    [ObservableProperty]
    public partial SettingsSection? SelectedSection { get; set; }


    /// <summary>
    /// A read-only view of the plugins currently loaded by the PluginService.
    /// </summary>
    public ReadOnlyObservableCollection<IPlugin> LoadedPlugins { get; init; }

    /// <summary>Gets the current application settings instance from the service.</summary>
    public AppSettings CurrentSettings => _settingsService.CurrentSettings;

    /// <summary>Gets the API Credentials settings.</summary>
    public ApiCredentials Credentials => CurrentSettings.Credentials;

    /// <summary>Gets the Connection settings.</summary>
    public ConnectionSettings Connections => CurrentSettings.Connections;

    /// <summary>Gets the Text-to-Speech settings.</summary>
    public TtsSettings TtsSettings => CurrentSettings.TextToSpeech;

    /// <summary>Gets the Overlay settings.</summary>
    public OverlaySettings OverlaySettings => CurrentSettings.Overlays;

    /// <summary>Gets the Module settings.</summary>
    public ModuleSettings ModuleSettings => CurrentSettings.Modules;

    /// <summary>Gets the collection of configured Twitch accounts.</summary>
    public ObservableCollection<TwitchAccount> TwitchAccounts => Connections.TwitchAccounts;

    /// <summary>Gets the collection of configured YouTube accounts.</summary>
    public ObservableCollection<YouTubeAccount> YouTubeAccounts => Connections.YouTubeAccounts;

    /// <summary>Gets a value indicating whether Twitch API credentials appear to be configured.</summary>
    public bool IsTwitchConfigured => Credentials.IsTwitchConfigured;

    /// <summary>Gets a value indicating whether YouTube API credentials appear to be configured.</summary>
    public bool IsYouTubeConfigured => Credentials.IsYouTubeConfigured;

    /// <summary>Gets a value indicating whether a Streamlabs token ID is stored in settings.</summary>
    public bool IsStreamlabsTokenSetup => !string.IsNullOrEmpty(Connections.StreamlabsTokenId);

    /// <summary>Gets a value indicating whether the Streamlabs connection can currently be disabled.</summary>
    public bool CanDisableStreamlabs => Connections.EnableStreamlabs || StreamlabsStatus != ConnectionStatus.Disconnected;

    // Forwarding properties for TTS settings
    public string SelectedEngine
    {
        get => TtsSettings.SelectedEngine;
        set
        {
            if (TtsSettings.SelectedEngine != value)
            {
                TtsSettings.SelectedEngine = value;
                OnPropertyChanged(); // Notify UI of change
                OnPropertyChanged(nameof(TtsSettings)); // Notify dependent elements
                SelectedEngineChanged(); // Call handler
            }
        }
    }

    public string? SelectedWindowsVoice
    {
        get => TtsSettings.SelectedWindowsVoice;
        set
        {
            if (TtsSettings.SelectedWindowsVoice != value)
            {
                TtsSettings.SelectedWindowsVoice = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TtsSettings));
                // Apply voice change immediately if Windows engine is active
                if (SelectedEngine == TtsSettings.WindowsEngine && value != null)
                {
                    _ttsService.SetWindowsVoice(value);
                }
            }
        }
    }

    public string? SelectedKokoroVoice
    {
        get => TtsSettings.SelectedKokoroVoice;
        set
        {
            if (TtsSettings.SelectedKokoroVoice != value)
            {
                TtsSettings.SelectedKokoroVoice = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TtsSettings));
                // Apply voice change immediately if Kokoro engine is active
                if (SelectedEngine == TtsSettings.KokoroEngine && value != null)
                {
                    _ttsService.SetKokoroVoice(value);
                }
            }
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsViewModel"/> class.
    /// Sets up navigation, loads initial state, and subscribes to necessary events.
    /// </summary>
    public SettingsViewModel(
        ILogger<SettingsViewModel> logger,
        ISettingsService settingsService,
        ITtsService ttsService,
        UnifiedEventService unifiedEventService,
        IMessenger messenger,
        IStreamlabsClient streamlabsClient,
        ITokenStorageService tokenStorageService,
        ITwitchClient twitchClient,
        IYouTubeClient youTubeClient,
        PluginService pluginService,
        DispatcherQueue dispatcherQueue
    )
    {
        _logger = logger;
        _settingsService = settingsService;
        _ttsService = ttsService;
        _unifiedEventService = unifiedEventService;
        _messenger = messenger;
        _streamlabsClient = streamlabsClient;
        _tokenStorage = tokenStorageService;
        _twitchClient = twitchClient;
        _youTubeClient = youTubeClient;
        _pluginService = pluginService;
        _dispatcherQueue = dispatcherQueue;

        _logger.LogInformation("Initializing SettingsViewModel.");
        LoadedPlugins = new ReadOnlyObservableCollection<IPlugin>(_pluginService.LoadedPlugins);

        SettingsSections.Add(
            new SettingsSection
            {
                Name = "Credentials",
                Glyph = "\uE72E",
                Tag = "Credentials",
            }
        );
        SettingsSections.Add(
            new SettingsSection
            {
                Name = "Accounts",
                Glyph = "\uE77B",
                Tag = "Accounts",
            }
        );
        SettingsSections.Add(
            new SettingsSection
            {
                Name = "Overlays",
                Glyph = "\uE7F4",
                Tag = "Overlays",
            }
        );
        SettingsSections.Add(
            new SettingsSection
            {
                Name = "Text-to-Speech",
                Glyph = "\uE767",
                Tag = "TTS",
            }
        );
        SettingsSections.Add(
            new SettingsSection
            {
                Name = "Modules",
                Glyph = "\uE737",
                Tag = "Modules",
            }
        );
        SettingsSections.Add(
            new SettingsSection
            {
                Name = "Plugins",
                Glyph = "\uE70E",
                Tag = "Plugins",
            }
        );

        SelectedSection = SettingsSections.FirstOrDefault();

        NotifyAllPropertiesChanged(); // Ensure initial state reflects loaded settings
        LoadInitialTtsVoices(); // Load voices for the initially selected engine
        UpdateStreamlabsStatus();
        HookPropertyListeners();
        UpdateOverlayUrl();

        _settingsService.SettingsUpdated += SettingsService_SettingsUpdated;
        _messenger.Register(this);

        _logger.LogDebug("Subscribed to SettingsService.SettingsUpdated event.");
    }

    /// <summary>
    /// Handler for the SettingsService.SettingsUpdated event. Refreshes ViewModel state.
    /// Ensures UI updates happen on the correct thread.
    /// </summary>
    private async void SettingsService_SettingsUpdated(object? sender, EventArgs e)
    {
        _logger.LogInformation("SettingsService_SettingsUpdated event received. Refreshing ViewModel state.");
        await _dispatcherQueue.EnqueueAsync(() =>
        {
            if (_isDisposed)
                return;
            NotifyAllPropertiesChanged();
            RefreshAccountStatuses();
        });
    }

    /// <summary>
    /// Receives the ConnectionsUpdatedMessage to trigger a refresh of account statuses.
    /// </summary>
    /// <param name="message">The received message.</param>
    public async void Receive(ConnectionsUpdatedMessage message)
    {
        _logger.LogInformation("ConnectionsUpdatedMessage received. Refreshing account statuses.");
        await _dispatcherQueue.EnqueueAsync(() =>
        {
            if (!_isDisposed)
                RefreshAccountStatuses();
        });
    }

    /// <summary>
    /// Iterates through Twitch and YouTube accounts and updates their status properties
    /// based on the current state reported by the respective client services.
    /// Ensures execution on the UI thread.
    /// </summary>
    private void RefreshAccountStatuses()
    {
        if (_isDisposed)
            return;
        _logger.LogDebug("Refreshing Twitch and YouTube account statuses...");
        if (Connections?.TwitchAccounts != null)
        {
            foreach (TwitchAccount acc in Connections.TwitchAccounts)
            {
                if (!string.IsNullOrEmpty(acc.UserId))
                {
                    ConnectionStatus currentStatus = _twitchClient.GetStatus(acc.UserId);
                    string? currentMessage = _twitchClient.GetStatusMessage(acc.UserId);
                    if (acc.Status != currentStatus || acc.StatusMessage != currentMessage)
                    {
                        acc.Status = currentStatus;
                        acc.StatusMessage = currentMessage;
                        _logger.LogTrace(
                            "Updated Twitch account {Username} status to {Status} ('{Message}')",
                            acc.Username,
                            currentStatus,
                            currentMessage
                        );
                    }
                }
            }
        }
        if (Connections?.YouTubeAccounts != null)
        {
            foreach (YouTubeAccount acc in Connections.YouTubeAccounts)
            {
                if (!string.IsNullOrEmpty(acc.ChannelId))
                {
                    ConnectionStatus currentStatus = _youTubeClient.GetStatus(acc.ChannelId);
                    string? currentMessage = _youTubeClient.GetStatusMessage(acc.ChannelId);
                    if (acc.Status != currentStatus || acc.StatusMessage != currentMessage)
                    {
                        acc.Status = currentStatus;
                        acc.StatusMessage = currentMessage;
                        _logger.LogTrace(
                            "Updated YouTube account {ChannelName} status to {Status} ('{Message}')",
                            acc.ChannelName,
                            currentStatus,
                            currentMessage
                        );
                    }
                }
            }
        }
        _logger.LogDebug("Account status refresh complete.");
    }

    /// <summary>
    /// Notifies PropertyChanged for all properties directly or indirectly derived from CurrentSettings.
    /// Used to refresh the UI when the underlying settings object changes.
    /// </summary>
    private void NotifyAllPropertiesChanged()
    {
        _logger.LogTrace("Notifying all derived properties changed.");
        OnPropertyChanged(nameof(CurrentSettings));
        OnPropertyChanged(nameof(Credentials));
        OnPropertyChanged(nameof(Connections));
        OnPropertyChanged(nameof(TtsSettings));
        OnPropertyChanged(nameof(SelectedEngine));
        OnPropertyChanged(nameof(SelectedWindowsVoice));
        OnPropertyChanged(nameof(SelectedKokoroVoice));
        OnPropertyChanged(nameof(OverlaySettings));
        OnPropertyChanged(nameof(ModuleSettings));
        OnPropertyChanged(nameof(TwitchAccounts));
        OnPropertyChanged(nameof(YouTubeAccounts));
        OnPropertyChanged(nameof(IsTwitchConfigured));
        OnPropertyChanged(nameof(IsYouTubeConfigured));
        OnPropertyChanged(nameof(IsStreamlabsTokenSetup));
        OnPropertyChanged(nameof(CanDisableStreamlabs));
        UpdateOverlayUrl();
        NotifyCommandCanExecuteChanged();
    }

    /// <summary>
    /// Hooks PropertyChanged listeners to nested observable objects within CurrentSettings
    /// and to the Streamlabs client for status updates.
    /// </summary>
    private void HookPropertyListeners()
    {
        _logger.LogTrace("Hooking property listeners.");
        UnhookPropertyListeners(); // Ensure no double-hooking

        if (Credentials != null)
            Credentials.PropertyChanged += NestedSetting_PropertyChanged;
        if (Connections != null)
            Connections.PropertyChanged += NestedSetting_PropertyChanged;
        if (TtsSettings != null)
            TtsSettings.PropertyChanged += NestedSetting_PropertyChanged;
        if (OverlaySettings != null)
            OverlaySettings.PropertyChanged += NestedSetting_PropertyChanged;
        if (OverlaySettings?.Chat != null)
            OverlaySettings.Chat.PropertyChanged += NestedSetting_PropertyChanged;
        if (ModuleSettings != null)
            ModuleSettings.PropertyChanged += NestedSetting_PropertyChanged;
        if (ModuleSettings?.Subathon != null)
            ModuleSettings.Subathon.PropertyChanged += NestedSetting_PropertyChanged;
        if (ModuleSettings?.Goals != null)
            ModuleSettings.Goals.PropertyChanged += NestedSetting_PropertyChanged;

        if (_streamlabsClient is INotifyPropertyChanged slNotifier)
        {
            slNotifier.PropertyChanged += StreamlabsClient_PropertyChanged;
        }
    }

    /// <summary>
    /// Unhooks previously attached PropertyChanged listeners.
    /// </summary>
    private void UnhookPropertyListeners()
    {
        _logger.LogTrace("Unhooking property listeners.");
        if (Credentials != null)
            Credentials.PropertyChanged -= NestedSetting_PropertyChanged;
        if (Connections != null)
            Connections.PropertyChanged -= NestedSetting_PropertyChanged;
        if (TtsSettings != null)
            TtsSettings.PropertyChanged -= NestedSetting_PropertyChanged;
        if (OverlaySettings != null)
            OverlaySettings.PropertyChanged -= NestedSetting_PropertyChanged;
        if (OverlaySettings?.Chat != null)
            OverlaySettings.Chat.PropertyChanged -= NestedSetting_PropertyChanged;
        if (ModuleSettings != null)
            ModuleSettings.PropertyChanged -= NestedSetting_PropertyChanged;
        if (ModuleSettings?.Subathon != null)
            ModuleSettings.Subathon.PropertyChanged -= NestedSetting_PropertyChanged;
        if (ModuleSettings?.Goals != null)
            ModuleSettings.Goals.PropertyChanged -= NestedSetting_PropertyChanged;

        if (_streamlabsClient is INotifyPropertyChanged slNotifier)
        {
            slNotifier.PropertyChanged -= StreamlabsClient_PropertyChanged;
        }
    }

    /// <summary>
    /// Generic handler for changes within nested settings objects.
    /// Used to trigger updates on dependent properties or command states.
    /// </summary>
    private void NestedSetting_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isDisposed)
            return;
        _logger.LogTrace(
            "NestedSetting_PropertyChanged: Sender={SenderType}, Property={PropertyName}",
            sender?.GetType().Name ?? "null",
            e.PropertyName
        );

        switch (sender)
        {
            case ApiCredentials:
                OnPropertyChanged(nameof(IsTwitchConfigured));
                OnPropertyChanged(nameof(IsYouTubeConfigured));
                ConnectTwitchAccountCommand.NotifyCanExecuteChanged();
                ConnectYouTubeAccountCommand.NotifyCanExecuteChanged();
                break;
            case ConnectionSettings:
                if (e.PropertyName is nameof(ConnectionSettings.EnableStreamlabs) or nameof(ConnectionSettings.StreamlabsTokenId))
                {
                    OnPropertyChanged(nameof(IsStreamlabsTokenSetup));
                    DisableStreamlabsCommand.NotifyCanExecuteChanged();
                }
                break;
            case OverlaySettings _:
                if (e.PropertyName == nameof(OverlaySettings.WebServerPort))
                    UpdateOverlayUrl();
                // Send update for *any* change in OverlaySettings for now
                _messenger.Send(new OverlaySettingsUpdateMessage(OverlaySettings));
                break;
            case ChatOverlaySettings:
                _logger.LogDebug("ChatOverlaySettings changed ({PropertyName}), sending update message.", e.PropertyName);
                _messenger.Send(new OverlaySettingsUpdateMessage(OverlaySettings));
                break;
            case TtsSettings ttsSettings:
                if (e.PropertyName == nameof(TtsSettings.Enabled))
                {
                    TestTtsCommand.NotifyCanExecuteChanged();
                    // Trigger voice loading if TTS was just enabled and voices aren't loaded yet
                    if (ttsSettings.Enabled && WindowsVoices.Count == 0 && KokoroVoices.Count == 0)
                    {
                        LoadInitialTtsVoices();
                    }
                }
                // Update SelectedEngine/Voice bindings in ViewModel when model changes (if needed)
                else if (e.PropertyName == nameof(TtsSettings.SelectedEngine))
                {
                    OnPropertyChanged(nameof(SelectedEngine)); // Update VM property bound to UI
                    SelectedEngineChanged(); // Trigger voice loading etc.
                }
                else if (e.PropertyName == nameof(TtsSettings.SelectedWindowsVoice))
                {
                    OnPropertyChanged(nameof(SelectedWindowsVoice)); // Update VM property bound to UI
                    if (SelectedEngine == TtsSettings.WindowsEngine)
                        ApplyVoiceSetting();
                }
                else if (e.PropertyName == nameof(TtsSettings.SelectedKokoroVoice))
                {
                    OnPropertyChanged(nameof(SelectedKokoroVoice)); // Update VM property bound to UI
                    if (SelectedEngine == TtsSettings.KokoroEngine)
                        ApplyVoiceSetting();
                }
                // Apply Rate/Volume changes immediately via the composite service
                else if (e.PropertyName == nameof(TtsSettings.Rate))
                {
                    _ttsService.SetRate(ttsSettings.Rate);
                }
                else if (e.PropertyName == nameof(TtsSettings.Volume))
                {
                    _ttsService.SetVolume(ttsSettings.Volume);
                }
                break;
            case ModuleSettings _:
                // Handle changes within ModuleSettings if needed
                break;
            case SubathonSettings:
                // Handle changes within SubathonSettings if needed
                break;
            case GoalSettings:
                // Handle changes within GoalSettings if needed
                break;
            default:
                _logger.LogTrace("Unhandled sender type in NestedSetting_PropertyChanged: {SenderType}", sender?.GetType().Name ?? "null");
                break;
        }
    }

    /// <summary>
    /// Handles PropertyChanged events from the Streamlabs client service.
    /// Updates the local status properties on the UI thread.
    /// </summary>
    private async void StreamlabsClient_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isDisposed)
            return;
        if (e.PropertyName is nameof(IStreamlabsClient.Status) or nameof(IStreamlabsClient.StatusMessage))
        {
            _logger.LogTrace("StreamlabsClient_PropertyChanged: {PropertyName}. Queueing status update.", e.PropertyName);
            await _dispatcherQueue.EnqueueAsync(() =>
            {
                if (!_isDisposed)
                    UpdateStreamlabsStatus();
            });
        }
    }

    /// <summary>
    /// Updates local ViewModel properties bound to the UI for Streamlabs status.
    /// Ensures execution on the UI thread.
    /// </summary>
    private void UpdateStreamlabsStatus()
    {
        StreamlabsStatus = _streamlabsClient.Status;
        StreamlabsStatusMessage = _streamlabsClient.StatusMessage;
        _logger.LogDebug("Updated Streamlabs Status UI: {Status}, Message: '{Message}'", StreamlabsStatus, StreamlabsStatusMessage);
    }

    /// <summary>
    /// Explicitly notifies CanExecuteChanged for all relevant commands.
    /// </summary>
    private void NotifyCommandCanExecuteChanged()
    {
        _logger.LogTrace("Notifying CanExecuteChanged for commands.");
        ConnectTwitchAccountCommand.NotifyCanExecuteChanged();
        ConnectYouTubeAccountCommand.NotifyCanExecuteChanged();
        DisableStreamlabsCommand.NotifyCanExecuteChanged();
        ConnectAllCommand.NotifyCanExecuteChanged();
        DisconnectAllCommand.NotifyCanExecuteChanged();
        SaveSettingsCommand.NotifyCanExecuteChanged();
        TestTtsCommand.NotifyCanExecuteChanged();
        CopyOverlayUrlCommand.NotifyCanExecuteChanged();
        SetupStreamlabsTokenCommand.NotifyCanExecuteChanged();
        // Add SampleVoiceCommand if it exists
    }

    /// <summary>
    /// Loads the appropriate voices based on the currently selected engine.
    /// Uses fire-and-forget for background loading.
    /// </summary>
    private void LoadInitialTtsVoices()
    {
        // Load voices based on the engine selected in the settings
        if (SelectedEngine == TtsSettings.KokoroEngine)
        {
            // Fire-and-forget background loading (Suppress CS4014)
            _ = LoadKokoroVoicesAsync();
        }
        else // Default to Windows
        {
            // Fire-and-forget background loading (Suppress CS4014)
            _ = LoadWindowsVoicesAsync();
        }
    }

    /// <summary>
    /// Handles the change of the selected TTS engine, loading appropriate voices.
    /// </summary>
    private async void SelectedEngineChanged()
    {
        _logger.LogInformation("Selected TTS Engine changed to: {Engine}", SelectedEngine);
        // The CompositeTtsService handles applying the correct engine internally.
        // We just need to load the voices for the UI.
        if (SelectedEngine == TtsSettings.KokoroEngine)
        {
            await LoadKokoroVoicesAsync();
        }
        else // Default to Windows
        {
            await LoadWindowsVoicesAsync();
        }
        // Apply the voice setting for the *newly* selected engine
        ApplyVoiceSetting();
    }

    /// <summary>
    /// Applies the currently selected voice (Windows or Kokoro) to the TTS service.
    /// </summary>
    private void ApplyVoiceSetting()
    {
        if (_isDisposed)
            return;
        try
        {
            if (SelectedEngine == TtsSettings.KokoroEngine && !string.IsNullOrEmpty(SelectedKokoroVoice))
            {
                _ttsService.SetKokoroVoice(SelectedKokoroVoice); // Composite service routes this
            }
            else if (SelectedEngine == TtsSettings.WindowsEngine && !string.IsNullOrEmpty(SelectedWindowsVoice))
            {
                _ttsService.SetWindowsVoice(SelectedWindowsVoice); // Composite service routes this
            }
            else
            {
                _logger.LogDebug("No specific voice selected for engine {Engine} or voice is null.", SelectedEngine);
                // Optionally reset to default? The composite service might handle this.
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying selected voice setting for engine {Engine}.", SelectedEngine);
        }
    }

    /// <summary>
    /// Loads available Windows TTS voices from the TTS service.
    /// </summary>
    private async Task LoadWindowsVoicesAsync()
    {
        if (_isDisposed)
            return;
        _logger.LogInformation("Loading Windows TTS voices via Composite Service...");
        try
        {
            // Use the composite service method
            IEnumerable<string> installedVoices = await _ttsService.GetInstalledWindowsVoicesAsync();
            await _dispatcherQueue.EnqueueAsync(() =>
            {
                if (_isDisposed)
                    return;
                WindowsVoices.Clear();
                string? currentSelected = SelectedWindowsVoice;
                if (installedVoices.Any())
                {
                    foreach (string voice in installedVoices)
                        WindowsVoices.Add(voice);

                    bool selectionStillValid = WindowsVoices.Contains(currentSelected ?? "");
                    _logger.LogDebug("Loaded {Count} Windows TTS voices.", WindowsVoices.Count);
                    if (!selectionStillValid && WindowsVoices.Any())
                    {
                        // Update the TtsSettings model property directly
                        TtsSettings.SelectedWindowsVoice = WindowsVoices.FirstOrDefault();
                        // Notify the UI that the underlying setting changed
                        OnPropertyChanged(nameof(SelectedWindowsVoice));
                    }
                }
                else
                {
                    _logger.LogWarning("No installed Windows TTS voices found.");
                    TtsSettings.SelectedWindowsVoice = null;
                    OnPropertyChanged(nameof(SelectedWindowsVoice));
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading Windows TTS voices.");
            await _dispatcherQueue.EnqueueAsync(() =>
            {
                if (_isDisposed)
                    return;
                WindowsVoices.Clear();
                TtsSettings.SelectedWindowsVoice = null;
                OnPropertyChanged(nameof(SelectedWindowsVoice));
            });
        }
    }

    /// <summary>
    /// Loads available Kokoro TTS voices from the TTS service.
    /// </summary>
    private async Task LoadKokoroVoicesAsync()
    {
        if (_isDisposed)
            return;
        _logger.LogInformation("Loading Kokoro TTS voices via Composite Service...");
        try
        {
            // Use the composite service method
            IEnumerable<string> installedVoices = await _ttsService.GetInstalledKokoroVoicesAsync();
            await _dispatcherQueue.EnqueueAsync(() =>
            {
                if (_isDisposed)
                    return;
                KokoroVoices.Clear();
                string? currentSelected = SelectedKokoroVoice;
                if (installedVoices.Any())
                {
                    foreach (string voice in installedVoices)
                        KokoroVoices.Add(voice);
                    bool selectionStillValid = KokoroVoices.Contains(currentSelected ?? "");
                    _logger.LogDebug("Loaded {Count} Kokoro TTS voices.", KokoroVoices.Count);
                    if (!selectionStillValid && KokoroVoices.Any())
                    {
                        TtsSettings.SelectedKokoroVoice = KokoroVoices.FirstOrDefault();
                        OnPropertyChanged(nameof(SelectedKokoroVoice));
                    }
                }
                else
                {
                    _logger.LogWarning("No installed Kokoro TTS voices found (or loading not implemented).");
                    TtsSettings.SelectedKokoroVoice = null;
                    OnPropertyChanged(nameof(SelectedKokoroVoice));
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading Kokoro TTS voices.");
            await _dispatcherQueue.EnqueueAsync(() =>
            {
                if (_isDisposed)
                    return;
                KokoroVoices.Clear();
                TtsSettings.SelectedKokoroVoice = null;
                OnPropertyChanged(nameof(SelectedKokoroVoice));
            });
        }
    }

    /// <summary>
    /// Command to test the Text-to-Speech configuration using the currently selected engine, voice,
    /// and the text provided in the TtsTestPhrase property.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanTestTts))]
    private async Task TestTtsAsync()
    {
        // Use MemberNotNullWhen guard for cleaner null checks
        [MemberNotNullWhen(true, nameof(TtsTestPhrase))]
        bool HasTestPhrase() => !string.IsNullOrWhiteSpace(TtsTestPhrase);

        if (!TtsSettings.Enabled)
        {
            _logger.LogInformation("Test TTS requested but TTS is currently disabled.");
            return;
        }
        if (!HasTestPhrase())
        {
            _logger.LogInformation("Test TTS requested but no test phrase entered.");
            return;
        }

        string voiceToUse = (SelectedEngine == TtsSettings.KokoroEngine ? SelectedKokoroVoice : SelectedWindowsVoice) ?? "Default";
        _logger.LogInformation(
            "Testing TTS with Engine: {Engine}, Voice: '{SelectedVoice}', Rate: {Rate}, Volume: {Volume}, Phrase: '{Phrase}'",
            SelectedEngine,
            voiceToUse,
            TtsSettings.Rate,
            TtsSettings.Volume,
            TtsTestPhrase
        );

        // Speak the custom text from the TtsTestPhrase property
        await _ttsService.SpeakAsync(TtsTestPhrase);
    }

    private bool CanTestTts() => TtsSettings.Enabled;

    /// <summary>
    /// Updates the ChatOverlayUrl property based on the current WebServerPort setting.
    /// </summary>
    private void UpdateOverlayUrl()
    {
        ChatOverlayUrl = $"http://localhost:{CurrentSettings.Overlays.WebServerPort}/chat";
        _logger.LogDebug("Chat Overlay URL updated: {Url}", ChatOverlayUrl);
        CopyOverlayUrlCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Command to save the current application settings.
    /// </summary>
    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        _logger.LogInformation("Saving settings...");
        await _settingsService.SaveSettingsAsync(CurrentSettings);
        _messenger.Send(new OverlaySettingsUpdateMessage(CurrentSettings.Overlays));
    }

    /// <summary>
    /// Command to open a URL in the default web browser.
    /// </summary>
    /// <param name="url">The URL string to open.</param>
    [RelayCommand]
    private static async Task OpenUrlAsync(string? url)
    {
        ILogger<SettingsViewModel> logger = App.GetService<ILogger<SettingsViewModel>>();
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            logger?.LogWarning("Invalid URL format provided to OpenUrlAsync: {Url}", url);
            return;
        }
        try
        {
            logger?.LogInformation("Opening URL: {Url}", url);
            await Launcher.LaunchUriAsync(uri);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error opening URL {Url}", url);
        }
    }

    /// <summary>
    /// Command to copy the specified overlay URL to the clipboard.
    /// </summary>
    /// <param name="overlayType">String identifying the overlay type (e.g., "chat").</param>
    [RelayCommand]
    private void CopyOverlayUrl(string? overlayType)
    {
        string urlToCopy = overlayType?.ToLowerInvariant() switch
        {
            "chat" => ChatOverlayUrl,
            _ => string.Empty,
        };
        if (string.IsNullOrEmpty(urlToCopy))
        {
            _logger.LogWarning("CopyOverlayUrl called with invalid/unsupported type or URL is empty: {OverlayType}", overlayType ?? "null");
            return;
        }
        try
        {
            DataPackage dataPackage = new() { RequestedOperation = DataPackageOperation.Copy };
            dataPackage.SetText(urlToCopy);
            Clipboard.SetContent(dataPackage);
            _logger.LogInformation("Copied overlay URL to clipboard: {Url}", urlToCopy);
            // TODO: Show brief confirmation to user (e.g., via TeachingTip or InfoBar)
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to copy URL to clipboard: {Url}", urlToCopy);
            // TODO: Show error to user
        }
    }

    /// <summary>
    /// Command to initiate the Twitch OAuth login flow.
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsTwitchConfigured))]
    private async Task ConnectTwitchAccountAsync()
    {
        _logger.LogInformation("ConnectTwitchAccount command executed.");
        // TODO: Show busy indicator
        try
        {
            await _unifiedEventService.TriggerTwitchLoginAsync();
            _logger.LogInformation("Twitch login flow initiated via UnifiedEventService.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initiating Twitch login flow.");
            // TODO: Show error to user
        }
        finally
        {
            // TODO: Hide busy indicator
        }
    }

    /// <summary>
    /// Command to remove a specific Twitch account (logs out, deletes tokens, removes from settings).
    /// </summary>
    [RelayCommand]
    private async Task RemoveTwitchAccountAsync(TwitchAccount? accountToRemove)
    {
        if (accountToRemove?.UserId == null)
            return;
        string userId = accountToRemove.UserId;
        string username = accountToRemove.Username;

        _logger.LogInformation("Removing Twitch account: {Username} ({UserId})", username, userId);
        // TODO: Show busy indicator
        try
        {
            // Logout/disconnect and remove tokens first
            await _unifiedEventService.LogoutTwitchAccountAsync(userId);

            // Remove from settings collection on UI thread
            bool removed = false;
            await _dispatcherQueue.EnqueueAsync(() =>
            {
                removed = CurrentSettings.Connections.TwitchAccounts.Remove(accountToRemove);
                if (removed)
                {
                    _logger.LogInformation("Removed {Username} from settings collection.", username);
                }
                else
                {
                    _logger.LogWarning("Failed to remove {Username} from settings collection (already removed?).", username);
                }
            });

            // Save settings if removal was successful
            if (removed)
            {
                await SaveSettingsAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing Twitch {Username}", username);
            // TODO: Show error to user
        }
        finally
        {
            // TODO: Hide busy indicator
            // Ensure UI updates regardless of success/failure
            _messenger.Send(new ConnectionsUpdatedMessage());
        }
    }

    /// <summary>
    /// Command to initiate the YouTube OAuth login flow.
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsYouTubeConfigured))]
    private async Task ConnectYouTubeAccountAsync()
    {
        _logger.LogInformation("ConnectYouTubeAccount command executed.");
        // TODO: Show busy indicator
        try
        {
            await _unifiedEventService.TriggerYouTubeLoginAsync();
            _logger.LogInformation("YouTube login flow initiated via UnifiedEventService.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initiating YouTube login flow.");
            // TODO: Show error to user
        }
        finally
        {
            // TODO: Hide busy indicator
        }
    }

    /// <summary>
    /// Command to remove a specific YouTube account (logs out, deletes tokens, removes from settings).
    /// </summary>
    [RelayCommand]
    private async Task RemoveYouTubeAccountAsync(YouTubeAccount? accountToRemove)
    {
        if (accountToRemove?.ChannelId == null)
            return;
        string channelId = accountToRemove.ChannelId;
        string channelName = accountToRemove.ChannelName;

        _logger.LogInformation("Removing YouTube account: {ChannelName} ({ChannelId})", channelName, channelId);
        // TODO: Show busy indicator
        try
        {
            // Logout/disconnect and remove tokens first
            await _unifiedEventService.LogoutYouTubeAccountAsync(channelId);

            // Remove from settings collection on UI thread
            bool removed = false;
            await _dispatcherQueue.EnqueueAsync(() =>
            {
                removed = CurrentSettings.Connections.YouTubeAccounts.Remove(accountToRemove);
                if (removed)
                {
                    _logger.LogInformation("Removed {ChannelName} from settings collection.", channelName);
                }
                else
                {
                    _logger.LogWarning("Failed to remove {ChannelName} from settings collection (already removed?).", channelName);
                }
            });

            // Save settings if removal was successful
            if (removed)
            {
                await SaveSettingsAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing YouTube {ChannelName}", channelName);
            // TODO: Show error to user
        }
        finally
        {
            // TODO: Hide busy indicator
            // Ensure UI updates regardless of success/failure
            _messenger.Send(new ConnectionsUpdatedMessage());
        }
    }

    /// <summary>
    /// Command to attempt connection for all accounts marked for auto-connect.
    /// </summary>
    [RelayCommand]
    private async Task ConnectAllAsync()
    {
        _logger.LogInformation("ConnectAll command executed.");
        // TODO: Show busy indicator
        await _unifiedEventService.ConnectAllConfiguredAsync();
        // TODO: Hide busy indicator
    }

    /// <summary>
    /// Command to disconnect all currently connected accounts.
    /// </summary>
    [RelayCommand]
    private async Task DisconnectAllAsync()
    {
        _logger.LogInformation("DisconnectAll command executed.");
        // TODO: Show busy indicator
        await _unifiedEventService.DisconnectAllAsync();
        // TODO: Hide busy indicator
    }

    /// <summary>
    /// Handles the Toggled event from the Streamlabs Enable ToggleSwitch, updating settings.
    /// </summary>
    /// <param name="enable">The new state from the toggle switch.</param>
    [RelayCommand]
    public async Task ToggleStreamlabsEnableAsync(bool enable)
    {
        if (_isDisposed)
            return;
        _logger.LogInformation("Toggle Streamlabs Enable changed to: {IsEnabled}", enable);
        if (CurrentSettings.Connections.EnableStreamlabs != enable)
        {
            CurrentSettings.Connections.EnableStreamlabs = enable;
            await SaveSettingsAsync(); // Save the setting change

            // Trigger connect/disconnect based on the new state
            if (enable)
            {
                await _unifiedEventService.ConnectStreamlabsAsync();
            }
            else
            {
                // Use Disable which also clears token ID etc.
                await _unifiedEventService.DisableStreamlabsAsync();
            }
        }
    }

    /// <summary>
    /// Command to show a dialog for setting up or changing the Streamlabs Socket API Token.
    /// </summary>
    [RelayCommand]
    private async Task SetupStreamlabsTokenAsync()
    {
        _logger.LogInformation("SetupStreamlabsToken command executed.");
        XamlRoot? xamlRoot = App.MainWindow?.Content?.XamlRoot;
        if (xamlRoot == null)
        {
            _logger.LogError("Cannot show Streamlabs token dialog: XamlRoot is null.");
            return;
        }

        TextBox tokenInputBox = new() { PlaceholderText = "Paste your Socket API token here..." };
        ContentDialog inputDialog = new()
        {
            Title = "Streamlabs Socket API Token",
            Content = tokenInputBox,
            PrimaryButtonText = "Save & Enable",
            SecondaryButtonText = "Save Only",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot,
        };

        ContentDialogResult result = await inputDialog.ShowAsync();
        string? tokenInput = tokenInputBox.Text;

        bool enableNow = result == ContentDialogResult.Primary;
        bool saveOnly = result == ContentDialogResult.Secondary;

        if ((enableNow || saveOnly) && !string.IsNullOrWhiteSpace(tokenInput))
        {
            // Use a consistent key or generate one if needed
            string streamlabsStorageKey = CurrentSettings.Connections.StreamlabsTokenId ?? $"streamlabs_socket_{Guid.NewGuid()}";

            try
            {
                await _tokenStorage.SaveTokensAsync(streamlabsStorageKey, tokenInput, null);

                bool settingsChanged = false;
                if (CurrentSettings.Connections.StreamlabsTokenId != streamlabsStorageKey)
                {
                    CurrentSettings.Connections.StreamlabsTokenId = streamlabsStorageKey;
                    settingsChanged = true;
                }
                if (enableNow && !CurrentSettings.Connections.EnableStreamlabs)
                {
                    CurrentSettings.Connections.EnableStreamlabs = true;
                    settingsChanged = true;
                }

                if (settingsChanged)
                {
                    await SaveSettingsAsync(); // Save settings if token ID or enable state changed
                }

                // If enabling now, attempt connection
                if (enableNow)
                {
                    await _unifiedEventService.ConnectStreamlabsAsync();
                }

                NotifyAllPropertiesChanged(); // Refresh UI state
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save Streamlabs token/settings.");
                // TODO: Show error to user
            }
        }
        else if (result == ContentDialogResult.None)
        {
            _logger.LogInformation("Streamlabs token setup cancelled.");
        }
        else
        {
            _logger.LogWarning("Streamlabs token setup skipped: No token entered.");
        }
    }

    /// <summary>
    /// Command to disable Streamlabs integration.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanDisableStreamlabs))]
    private async Task DisableStreamlabsAsync()
    {
        _logger.LogInformation("DisableStreamlabs command executed.");
        await _unifiedEventService.DisableStreamlabsAsync();
        NotifyAllPropertiesChanged(); // Refresh UI state
    }

    /// <summary>
    /// Handles the toggled state change for an account's connection switch.
    /// Updates the AutoConnect setting and initiates connect/disconnect actions.
    /// </summary>
    /// <param name="account">The account object (TwitchAccount or YouTubeAccount).</param>
    /// <param name="connect">True if the toggle was turned ON, false if turned OFF.</param>
    public async Task HandleAccountToggleAsync(object account, bool connect)
    {
        if (_isDisposed)
            return;

        bool settingsChanged = false;
        string? accountId = null;
        string? platform = null;
        string? displayName = null;

        if (account is TwitchAccount twitchAcc)
        {
            accountId = twitchAcc.UserId;
            platform = "Twitch";
            displayName = twitchAcc.Username;
            if (twitchAcc.AutoConnect != connect)
            {
                twitchAcc.AutoConnect = connect;
                settingsChanged = true;
                _logger.LogDebug("Updated AutoConnect for Twitch account {Username} to {AutoConnect}", displayName, connect);
            }
        }
        else if (account is YouTubeAccount ytAcc)
        {
            accountId = ytAcc.ChannelId;
            platform = "YouTube";
            displayName = ytAcc.ChannelName;
            if (ytAcc.AutoConnect != connect)
            {
                ytAcc.AutoConnect = connect;
                settingsChanged = true;
                _logger.LogDebug("Updated AutoConnect for YouTube account {ChannelName} to {AutoConnect}", displayName, connect);
            }
        }

        if (accountId == null || platform == null)
        {
            _logger.LogWarning("HandleAccountToggleAsync called with invalid account type: {AccountType}", account?.GetType().Name);
            return;
        }

        // TODO: Add busy indicator management
        try
        {
            Task? actionTask = null;
            if (connect)
            {
                _logger.LogInformation(
                    "Toggle ON: Attempting to connect {Platform} account {DisplayName} ({AccountId})",
                    platform,
                    displayName,
                    accountId
                );
                actionTask = platform switch
                {
                    "Twitch" => _unifiedEventService.ConnectTwitchAccountAsync((TwitchAccount)account),
                    "YouTube" => _unifiedEventService.ConnectYouTubeAccountAsync((YouTubeAccount)account),
                    _ => Task.CompletedTask,
                };
            }
            else
            {
                _logger.LogInformation(
                    "Toggle OFF: Disconnecting {Platform} client for {DisplayName} ({AccountId})",
                    platform,
                    displayName,
                    accountId
                );
                actionTask = platform switch
                {
                    "Twitch" => _twitchClient.DisconnectAsync(accountId), // Use client directly for disconnect
                    "YouTube" => _youTubeClient.DisconnectAsync(accountId), // Use client directly for disconnect
                    _ => Task.CompletedTask,
                };
            }

            if (actionTask != null)
            {
                await actionTask;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during toggle connect/disconnect action for {Platform} account {DisplayName}", platform, displayName);
        }
        finally
        {
            // TODO: Hide busy indicator
            if (settingsChanged)
            {
                await SaveSettingsAsync();
            }
        }
    }

    /// <summary>
    /// Cleans up resources used by the ViewModel.
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed)
            return;
        _isDisposed = true;
        _logger.LogInformation("Disposing SettingsViewModel...");
        _settingsService.SettingsUpdated -= SettingsService_SettingsUpdated;
        _messenger.Unregister<ConnectionsUpdatedMessage>(this);
        UnhookPropertyListeners();
        _logger.LogInformation("SettingsViewModel Dispose finished.");
        GC.SuppressFinalize(this);
    }
}
