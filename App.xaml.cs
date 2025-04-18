using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using StreamWeaver.Core.Plugins;
using StreamWeaver.Core.Services;
using StreamWeaver.Core.Services.Authentication;
using StreamWeaver.Core.Services.Logging;
using StreamWeaver.Core.Services.Platforms;
using StreamWeaver.Core.Services.Settings;
using StreamWeaver.Core.Services.Tts;
using StreamWeaver.Core.Services.Web;
using StreamWeaver.Modules.Goals;
using StreamWeaver.Modules.Subathon;
using StreamWeaver.UI.ViewModels;
using TTSTextNormalization.DependencyInjection;
using TTSTextNormalization.Rules;
using Velopack;
using Velopack.Sources;
using YTLiveChat.Contracts;

namespace StreamWeaver;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    public static Window? MainWindow { get; private set; }
    public IServiceProvider Services { get; }
    private static IServiceProvider? s_serviceProvider;
    private static ILogger<App>? s_logger;

    // Velopack Update State
    private static UpdateManager? s_updateManager;
    private static UpdateInfo? s_pendingUpdateInfo;
    private static bool s_updateDownloadInitiated = false;
    private static bool s_updateAppliedOrDeferred = false;
    private static readonly Lock s_updateLock = new();

    public App()
    {
        VelopackApp.Build()
            .OnFirstRun((v) => { /* First run logic here */ })
            .Run();

        Services = ConfigureServices();
        s_serviceProvider = Services;
        s_logger = Services.GetRequiredService<ILogger<App>>();
        InitializeComponent();

        // Start checking for updates in the background shortly after launch
        // Delay slightly to allow the main window to initialize visually
        _ = CheckForUpdatesInBackgroundAsync(TimeSpan.FromSeconds(5));
    }

    public static T GetService<T>()
        where T : class =>
        s_serviceProvider == null
            ? throw new InvalidOperationException("Service provider is not initialized.")
            : s_serviceProvider.GetRequiredService<T>();

    private static ServiceProvider ConfigureServices()
    {
        ServiceCollection services = [];

        // YTLiveChat needs IConfiguration to read its options, even if none are set.
        // We'll provide a minimal in-memory configuration source.
        IConfigurationRoot configuration = new ConfigurationBuilder()
        // .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true) // If using JSON file later on
        .Build();

        services.AddSingleton<IConfiguration>(configuration);

        services.AddSingleton(
            DispatcherQueue.GetForCurrentThread()
                ?? throw new InvalidOperationException("Cannot get DispatcherQueue on non-UI thread during service configuration.")
        );

        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.AddObservableLogger();
#if DEBUG
            builder.SetMinimumLevel(LogLevel.Debug);
#else
            builder.SetMinimumLevel(LogLevel.Information);
#endif
            builder.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
            builder.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Information);
            builder.AddFilter("Grpc.Net.Client", LogLevel.Warning);
            builder.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);
            builder.AddFilter("System.Net.Http.HttpClient.YouTubeClient.ClientHandler", LogLevel.Warning);
            builder.AddFilter("System.Net.Http.HttpClient.YouTubeClient.LogicalHandler", LogLevel.Warning);
        });

        services.AddSingleton<LogViewerService>();

        // --- TTS Text Normalization Configuration ---
        services.Configure<AbbreviationRuleOptions>(options =>
        {
            // var customMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { { "sw", "Stream Weaver" } };
            // options.CustomAbbreviations = customMap.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
            // options.ReplaceDefaultAbbreviations = false; // Merge with defaults
        });
        services.Configure<UrlRuleOptions>(options => { options.PlaceholderText = " link "; });
        services.Configure<EmojiRuleOptions>(options => { options.Suffix = " emoji"; }); // e.g., "thumbs up emoji"


        // Add the normalization pipeline with desired rules and order
        services.AddTextNormalization(builder =>
        {
            builder.AddBasicSanitizationRule();
            builder.AddUrlNormalizationRule();
            builder.AddEmojiRule();
            builder.AddCurrencyRule();
            builder.AddAbbreviationNormalizationRule();
            builder.AddNumberNormalizationRule();
            builder.AddExcessivePunctuationRule();
            builder.AddLetterRepetitionRule();
            builder.AddWhitespaceNormalizationRule();
        });

        // Core Services
        services.AddSingleton<IMessenger>(WeakReferenceMessenger.Default);
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<ITokenStorageService, TokenStorageService>();
        services.AddSingleton<IEmoteBadgeService, EmoteBadgeService>();

        // Platform Auth
        services.AddSingleton<TwitchAuthService>();
        services.AddSingleton<YouTubeAuthService>();

        // Platform API Services
        services.AddSingleton<TwitchApiService>();

        // Platform Clients
        services.AddSingleton<ITwitchClient, TwitchChatService>();
        services.AddSingleton<IYouTubeClient, YouTubeService>();
        services.AddSingleton<IStreamlabsClient, StreamlabsService>();

        // YTLiveChat Services
        configuration.GetSection("YTLiveChat").Bind(new YTLiveChatOptions { DebugLogReceivedJsonItems = true });
        services.AddYTLiveChat(configuration);

        // TTS Services
        services.AddSingleton<TtsFormattingService>(); // Register the formatting service

        // Register engine-specific services directly
        services.AddSingleton<WindowsTtsService>();
        services.AddSingleton<KokoroTtsService>();

        // Register *all* engine-specific services as IEnumerable<IEngineSpecificTtsService>
        // The CompositeTtsService will inject this IEnumerable.
        services.AddSingleton<IEngineSpecificTtsService, WindowsTtsService>(sp => sp.GetRequiredService<WindowsTtsService>());
        services.AddSingleton<IEngineSpecificTtsService, KokoroTtsService>(sp => sp.GetRequiredService<KokoroTtsService>());

        // Register CompositeTtsService as the main ITtsService implementation
        services.AddSingleton<ITtsService, CompositeTtsService>();

        // Other Core (Keep as is)
        services.AddSingleton<UnifiedEventService>();

        // Web Server
        services.AddSingleton<WebSocketManager>();
        services.AddSingleton<WebServerService>();

        // Plugin System
        services.AddSingleton<PluginService>();

        // UI ViewModels
        services.AddSingleton<MainChatViewModel>();
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<ConnectAccountViewModel>();
        services.AddSingleton<LogsViewModel>();

        // Modules
        services.AddSingleton<SubathonService>();
        services.AddSingleton<GoalService>();

        return services.BuildServiceProvider();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        s_logger?.LogInformation("OnLaunched Started.");

        ILogger<MainWindow> mainWindowLogger = Services.GetRequiredService<ILogger<MainWindow>>();
        MainWindowViewModel mainWindowViewModel = Services.GetRequiredService<MainWindowViewModel>();

        MainWindow = new MainWindow(mainWindowLogger, mainWindowViewModel);

        if (MainWindow.Content == null)
        {
            s_logger?.LogDebug("MainWindow content is null, setting up basic Frame.");
            Frame rootFrame = new();
            MainWindow.Content = rootFrame;
        }

        // Ensure the MainWindow is created and XamlRoot is available before potential dialogs
        MainWindow.Activate();
        s_logger?.LogInformation("MainWindow Activated.");

        try
        {
            s_logger?.LogInformation("Initializing core services...");
            ISettingsService settingsService = Services.GetRequiredService<ISettingsService>();
            s_logger?.LogDebug("Loading settings...");
            await settingsService.LoadSettingsAsync();
            s_logger?.LogDebug("Settings loaded.");

            IEmoteBadgeService emoteBadgeService = Services.GetRequiredService<IEmoteBadgeService>();
            s_logger?.LogDebug("Loading global emote/badge data...");
            await emoteBadgeService.LoadGlobalTwitchDataAsync();
            s_logger?.LogDebug("Global emote/badge data load attempted.");

            WebServerService webServer = Services.GetRequiredService<WebServerService>();
            s_logger?.LogDebug("Starting web server...");
            _ = Task.Run(webServer.StartAsync); // Fire and forget

            UnifiedEventService unifiedEventService = Services.GetRequiredService<UnifiedEventService>();
            s_logger?.LogDebug("Initializing UnifiedEventService...");
            await unifiedEventService.InitializeAsync();
            s_logger?.LogDebug("UnifiedEventService initialized.");

            s_logger?.LogDebug("Initializing PluginService...");
            PluginService pluginService = Services.GetRequiredService<PluginService>();
            string baseDirectory = AppContext.BaseDirectory;
            string pluginsPath = Path.Combine(baseDirectory, "Plugins");
            s_logger?.LogDebug("Determined Plugins directory path: {pluginsPath}", pluginsPath);
            await pluginService.LoadPluginsAsync(pluginsPath);
            s_logger?.LogDebug("PluginService initialization complete.");
        }
        catch (Exception ex)
        {
            s_logger?.LogCritical(ex, "FATAL: Failed to initialize services or plugins on launch.");
            // Ensure MainWindow and its XamlRoot are available before showing the dialog
            if (MainWindow?.Content?.XamlRoot != null)
            {
                ContentDialog dialog = new()
                {
                    Title = "Initialization Error",
                    Content = $"Failed to start essential services or load plugins: {ex.Message}\n\nSee logs for more details.",
                    CloseButtonText = "OK",
                    XamlRoot = MainWindow.Content.XamlRoot,
                };
                await dialog.ShowAsync();
            }
            else
            {
                s_logger?.LogError("Could not show initialization error dialog: MainWindow or XamlRoot is null.");
            }
        }

        s_logger?.LogInformation("OnLaunched Finished.");
    }

    private static async Task CheckForUpdatesInBackgroundAsync(TimeSpan? delay = null)
    {
        if (s_logger == null) return;

        if (delay.HasValue)
        {
            s_logger.LogInformation("Delaying update check for {DelaySeconds} seconds.", delay.Value.TotalSeconds);
            await Task.Delay(delay.Value);
        }

        lock (s_updateLock)
        {
            if (s_updateDownloadInitiated || s_updateAppliedOrDeferred)
            {
                s_logger.LogInformation("Update check skipped: Download already in progress or update decision made.");
                return;
            }
        }

        s_logger.LogInformation("Starting background update check...");

        try
        {
            var source = new GithubSource("https://github.com/Agash/StreamWeaver", null, false);
            s_updateManager = new UpdateManager(source);

            s_logger.LogDebug("Checking for updates...");
            UpdateInfo? updateInfo = await s_updateManager.CheckForUpdatesAsync();

            if (updateInfo == null)
            {
                s_logger.LogInformation("No update available.");
                return;
            }

            s_logger.LogInformation("Update found: v{Version}. Current version: v{CurrentVersion}",
                updateInfo.TargetFullRelease.Version, s_updateManager.CurrentVersion);

            lock (s_updateLock)
            {
                // Double-check inside lock in case another check completed concurrently
                if (s_updateDownloadInitiated || s_updateAppliedOrDeferred) return;

                s_pendingUpdateInfo = updateInfo;
                s_updateDownloadInitiated = true; // Mark that we are STARTING the download process
            }

            s_logger.LogInformation("Initiating background download for update v{Version}.", updateInfo.TargetFullRelease.Version);

            // Start download in a truly background thread
            _ = Task.Run(() => DownloadUpdateAsync(updateInfo));

        }
#if DEBUG
        catch (Exception ex) when (ex.Message == "Cannot perform this operation in an application which is not installed.")
        {
            s_logger.LogDebug(ex, "Update check failed because unpackaged execution (DEBUG mode)");
            lock (s_updateLock)
            {
                s_updateDownloadInitiated = false;
                s_pendingUpdateInfo = null;
            }
        }
#endif
        catch (Exception ex)
        {
            s_logger.LogError(ex, "Update check failed.");
            // Reset flags if the check itself fails
            lock (s_updateLock)
            {
                s_updateDownloadInitiated = false;
                s_pendingUpdateInfo = null;
            }
        }
    }

    // Runs in a background thread (called via Task.Run)
    private static async Task DownloadUpdateAsync(UpdateInfo updateInfo)
    {
        if (s_updateManager == null) return; // Should not happen if check succeeded, but safety first

        try
        {
            s_logger?.LogInformation("Background download started for v{Version}.", updateInfo.TargetFullRelease.Version);

            // Optional: Progress Reporting
            // var progress = new Progress<int>(percent => s_logger?.LogDebug("Download progress: {Percent}%", percent));
            // await s_updateManager.DownloadUpdatesAsync(updateInfo, progress);
            await s_updateManager.DownloadUpdatesAsync(updateInfo);

            s_logger?.LogInformation("Update v{Version} downloaded successfully.", updateInfo.TargetFullRelease.Version);

            // Download complete, now schedule the prompt on the UI thread
            DispatcherQueue dispatcherQueue = GetService<DispatcherQueue>(); // Assuming GetService is thread-safe or called appropriately
            if (dispatcherQueue == null)
            {
                s_logger?.LogError("Cannot show update prompt: DispatcherQueue is null after download.");
                // If we can't get the dispatcher, we can't prompt. Reset state.
                lock (s_updateLock)
                {
                    s_updateDownloadInitiated = false;
                    s_pendingUpdateInfo = null;
                }

                return;
            }

            dispatcherQueue.TryEnqueue(() =>
            {
                // Ensure the app hasn't been closed or the update deferred while downloading
                lock (s_updateLock)
                {
                    if (!s_updateAppliedOrDeferred)
                    {
                        _ = ShowUpdatePromptAsync(updateInfo); // Fire and forget the UI task
                    }
                    else
                    {
                        s_logger?.LogInformation("Update prompt skipped: Update was applied or deferred during download.");
                    }
                }
            });
        }
        catch (Exception ex)
        {
            s_logger?.LogError(ex, "Update download failed for v{Version}.", updateInfo.TargetFullRelease.Version);
            // Reset flags on download failure so the check might run again later
            lock (s_updateLock)
            {
                s_updateDownloadInitiated = false;
                s_pendingUpdateInfo = null;
            }

            // Optional: Notify user of download failure on UI thread
            DispatcherQueue dispatcherQueue = GetService<DispatcherQueue>();
            if (dispatcherQueue != null && MainWindow?.Content?.XamlRoot != null)
            {
                dispatcherQueue.TryEnqueue(async () =>
                {
                    ContentDialog errorDialog = new()
                    {
                        Title = "Update Download Failed",
                        Content = $"Could not download the latest update. Please check your internet connection or logs for details.\nError: {ex.Message}",
                        CloseButtonText = "OK",
                        XamlRoot = MainWindow.Content.XamlRoot
                    };
                    await errorDialog.ShowAsync();
                });
            }
        }
    }


    // Runs EXCLUSIVELY on the UI thread (called via DispatcherQueue.TryEnqueue)
    private static async Task ShowUpdatePromptAsync(UpdateInfo updateInfo)
    {
        // Double check conditions required for UI interaction
        if (s_updateManager == null || MainWindow?.Content?.XamlRoot == null)
        {
            s_logger?.LogError("Cannot show update prompt: UpdateManager or MainWindow/XamlRoot is null.");
            // Reset state if we can't even show the prompt
            lock (s_updateLock)
            {
                s_updateDownloadInitiated = false; // Allow re-check/download
                s_pendingUpdateInfo = null;
                // Don't set s_updateAppliedOrDeferred as no decision was made
            }

            return;
        }

        // Check if a decision was made *while* this was being queued
        lock (s_updateLock)
        {
            if (s_updateAppliedOrDeferred)
            {
                s_logger?.LogInformation("Update prompt skipped: Update decision already made.");
                return;
            }
        }


        s_logger?.LogInformation("Showing update prompt to user.");

        ContentDialog updateDialog = new()
        {
            Title = "Update Available",
            Content = $"A new version (v{updateInfo.TargetFullRelease.Version}) is ready. Install it now?",
            PrimaryButtonText = "Install Now & Restart",
            SecondaryButtonText = "Install on Exit",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = MainWindow.Content.XamlRoot
        };

        ContentDialogResult result = await updateDialog.ShowAsync();

        lock (s_updateLock)
        {
            // Mark that a decision has been made *now*, regardless of outcome
            s_updateAppliedOrDeferred = true;
            // s_updateDownloadInitiated remains true, as download *was* initiated/completed

            switch (result)
            {
                case ContentDialogResult.Primary: // Install Now & Restart
                    s_logger?.LogInformation("User chose to install update now.");
                    try
                    {
                        // This call attempts to exit the app immediately
                        s_updateManager.ApplyUpdatesAndRestart(updateInfo);
                        // If ApplyUpdatesAndRestart fails, it throws, and we hit the catch block.
                        // If it succeeds, the app exits, code below doesn't run.
                    }
                    catch (Exception ex)
                    {
                        s_logger?.LogError(ex, "Failed to apply updates and restart.");
                        s_updateAppliedOrDeferred = false; // Reset decision state on failure
                        // No need to reset s_updateDownloadInitiated, download is still done
                        ShowErrorDialog("Update Installation Failed", $"Could not install the update. Error: {ex.Message}");
                    }

                    break;

                case ContentDialogResult.Secondary: // Install on Exit
                    s_logger?.LogInformation("User chose to install update on exit.");
                    try
                    {
                        s_updateManager.WaitExitThenApplyUpdates(updateInfo);
                        s_logger?.LogInformation("Update scheduled to be applied on application exit.");
                        // Optionally show non-modal confirmation
                    }
                    catch (Exception ex)
                    {
                        s_logger?.LogError(ex, "Failed to schedule update for application exit.");
                        s_updateAppliedOrDeferred = false; // Reset decision state on failure
                        ShowErrorDialog("Update Scheduling Failed", $"Could not schedule the update. Error: {ex.Message}");
                    }

                    break;

                case ContentDialogResult.None: // Dialog dismissed without button press (e.g., Esc)
                default: // Treat "Later" or dismissal the same: Do nothing now
                    s_logger?.LogInformation("User chose to install update later or closed the dialog.");
                    // Update remains downloaded. Velopack will likely find it again on next launch.
                    // We keep s_updateAppliedOrDeferred = true for this session to avoid re-prompting immediately.
                    // On next app start, s_updateAppliedOrDeferred will be false, allowing a new check/prompt.
                    break;
            }
        }
    }

    // Helper to show error dialogs on UI thread (must be called from UI thread or marshalled)
    private static async void ShowErrorDialog(string title, string content)
    {
        if (MainWindow?.Content?.XamlRoot == null)
        {
            s_logger?.LogError("Cannot show error dialog '{Title}': MainWindow/XamlRoot is null.", title);
            return;
        }

        ContentDialog errorDialog = new()
        {
            Title = title,
            Content = content,
            CloseButtonText = "OK",
            XamlRoot = MainWindow.Content.XamlRoot
        };
        await errorDialog.ShowAsync();
    }
}

// Extension method for cleaner registration
public static class ObservableLoggerExtensions
{
    public static ILoggingBuilder AddObservableLogger(this ILoggingBuilder builder)
    {
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ILoggerProvider, ObservableLoggerProvider>());
        return builder;
    }
}
