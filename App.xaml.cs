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

    public App()
    {
        VelopackApp.Build()
            .WithFirstRun((v) => { /* First run logic here */ })
            .Run();

        Services = ConfigureServices();
        s_serviceProvider = Services;
        InitializeComponent();

        _ = CheckForUpdatesInBackgroundAsync();
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

        // --- Add YTLiveChat Services ---
        // Uses the IConfiguration registered above
        configuration.GetSection("YTLiveChat").Bind(new YTLiveChatOptions { DebugLogReceivedJsonItems = true });
        services.AddYTLiveChat(configuration);

        // Other Core
        services.AddSingleton<ITtsService, WindowsTtsService>();
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
        ILogger<App> logger = Services.GetRequiredService<ILogger<App>>();
        logger.LogInformation("OnLaunched Started.");

        ILogger<MainWindow> mainWindowLogger = Services.GetRequiredService<ILogger<MainWindow>>();
        MainWindowViewModel mainWindowViewModel = Services.GetRequiredService<MainWindowViewModel>();

        MainWindow = new MainWindow(mainWindowLogger, mainWindowViewModel);

        if (MainWindow.Content == null)
        {
            logger.LogDebug("MainWindow content is null, setting up basic Frame.");
            Frame rootFrame = new();
            MainWindow.Content = rootFrame;
        }

        MainWindow.Activate();
        logger.LogInformation("MainWindow Activated.");

        try
        {
            logger.LogInformation("Initializing core services...");
            ISettingsService settingsService = Services.GetRequiredService<ISettingsService>();
            logger.LogDebug("Loading settings...");
            await settingsService.LoadSettingsAsync();
            logger.LogDebug("Settings loaded.");

            IEmoteBadgeService emoteBadgeService = Services.GetRequiredService<IEmoteBadgeService>();
            logger.LogDebug("Loading global emote/badge data...");
            await emoteBadgeService.LoadGlobalTwitchDataAsync();
            logger.LogDebug("Global emote/badge data load attempted.");

            WebServerService webServer = Services.GetRequiredService<WebServerService>();
            logger.LogDebug("Starting web server...");
            _ = Task.Run(webServer.StartAsync);

            UnifiedEventService unifiedEventService = Services.GetRequiredService<UnifiedEventService>();
            logger.LogDebug("Initializing UnifiedEventService...");
            await unifiedEventService.InitializeAsync();
            logger.LogDebug("UnifiedEventService initialized.");

            logger.LogDebug("Initializing PluginService...");
            PluginService pluginService = Services.GetRequiredService<PluginService>();
            string baseDirectory = AppContext.BaseDirectory;
            string pluginsPath = Path.Combine(baseDirectory, "Plugins");
            logger.LogDebug("Determined Plugins directory path: {pluginsPath}", pluginsPath);
            await pluginService.LoadPluginsAsync(pluginsPath);
            logger.LogDebug("PluginService initialization complete.");
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "FATAL: Failed to initialize services or plugins on launch.");
            ContentDialog dialog = new()
            {
                Title = "Initialization Error",
                Content = $"Failed to start essential services or load plugins: {ex.Message}",
                CloseButtonText = "OK",
                XamlRoot = MainWindow?.Content?.XamlRoot,
            };
            if (dialog.XamlRoot != null)
                await dialog.ShowAsync();
            else
                logger.LogError("Could not show error dialog: MainWindow XamlRoot is null.");
        }

        logger.LogInformation("OnLaunched Finished.");
    }

    // Example background update check method
    private static async Task CheckForUpdatesInBackgroundAsync(TimeSpan? delay = null)
    {
        if (delay.HasValue)
        {
            await Task.Delay(delay.Value);
        }

        try
        {
            var source = new GithubSource("https://github.com/Agash/StreamWeaver", null, false);
            var manager = new UpdateManager(source);

            UpdateInfo? updateInfo = await manager.CheckForUpdatesAsync();
            if (updateInfo == null)
            {
                return; // No update available
            }


            // Download the update
            // Consider progress reporting if needed (manager.DownloadProgress)
            await manager.DownloadUpdatesAsync(updateInfo);


            // Apply the update silently on next restart
            manager.ApplyUpdatesAndRestart(updateInfo); // This will apply on *next* normal restart
                                                        // OR
                                                        // Show a notification to the user and let them restart
                                                        // ShowUpdateNotification(updateInfo); // Implement this method in your UI logic

            // Example: Apply and restart immediately (use with caution, inform the user!)
            // if (UserConfirmsRestart()) // Get user confirmation
            // {
            //      manager.ApplyUpdatesAndRestart(updateInfo);
            // }

        }
        catch (Exception ex)
        {
            // Handle exceptions appropriately (e.g., network errors)
            Console.WriteLine(ex.ToString());
        }
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
