using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StreamWeaver.Core.Plugins;
using StreamWeaver.Core.Services.Settings;

namespace StreamWeaver.Core.Services.Web;

/// <summary>
/// Manages the embedded Kestrel web server for serving overlay files (React app + Plugins)
/// and handling WebSocket connections.
/// </summary>
public partial class WebServerService(
    ILogger<WebServerService> logger,
    IServiceProvider serviceProvider,
    ISettingsService settingsService,
    WebSocketManager webSocketManager
    ) : IDisposable
{
    private readonly ILogger<WebServerService> _logger = logger;
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly ISettingsService _settingsService = settingsService;
    private readonly WebSocketManager _webSocketManager = webSocketManager;
    private WebApplication? _webApp;
    private CancellationTokenSource? _cts;
    private string _reactAppDistPath = string.Empty; // Path to React build output
    private string _pluginsWebPath = string.Empty; // Path to Web Plugins directory
    private List<WebPluginManifest> _discoveredWebPlugins = []; // Store discovered plugins
    private bool _isDisposed;

    // Property to expose discovered plugins (used by WebSocketManager)
    public IEnumerable<WebPluginManifest> DiscoveredWebPlugins => _discoveredWebPlugins;

    private static readonly JsonSerializerOptions s_manifestSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true // Allow trailing commas in manifest.json
    };

    public async Task StartAsync()
    {
        if (_webApp != null)
        {
            _logger.LogInformation("Start requested but web server is already running.");
            return;
        }

        _cts = new CancellationTokenSource();
        Models.Settings.AppSettings settings = await _settingsService.LoadSettingsAsync();
        int port = settings.Overlays.WebServerPort;

        // --- Determine Paths ---
        DetermineWebPaths(); // Find React app dist and Web Plugins paths

        if (string.IsNullOrEmpty(_reactAppDistPath) || !Directory.Exists(_reactAppDistPath))
        {
            _logger.LogCritical("React app distribution directory not found at '{ReactAppDistPath}'. Overlays will not work.", _reactAppDistPath);
            _cts.Dispose();
            _cts = null;
            return; // Cannot start without the main overlay app
        }

        // --- Discover Web Plugins ---
        _discoveredWebPlugins = DiscoverWebPlugins();

        _logger.LogInformation("Starting web server on port {Port}...", port);
        _logger.LogInformation("Serving React App from: {ReactAppDistPath}", _reactAppDistPath);
        _logger.LogInformation("Serving Web Plugins from base: {PluginsWebPath}", _pluginsWebPath);
        _logger.LogInformation("Discovered {PluginCount} valid web plugins.", _discoveredWebPlugins.Count);

        try
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder();
            builder.WebHost.UseKestrel(options =>
            {
                options.ListenLocalhost(port);
                _logger.LogInformation("Kestrel configured to listen on http://localhost:{Port}", port);
            });

            // Minimal logging for Kestrel itself
            builder.Logging.ClearProviders();
#if DEBUG
            builder.Logging.AddDebug();
#endif
            builder.Logging.SetMinimumLevel(LogLevel.Warning); // Reduce Kestrel noise

            // Register services needed by WebSocketManager or potentially other middleware
            builder.Services.AddSingleton(_webSocketManager);
            builder.Services.AddSingleton(_settingsService);
            builder.Services.AddSingleton(this); // Make WebServerService itself available if needed

            _webApp = builder.Build();

            // --- Configure Middleware ---
            _webApp.UseWebSockets();

            // WebSocket Endpoint
            _webApp.Map("/ws", HandleWebSocketConnection);

            // Serve Static Files for the main React App (from /Web/Overlay/dist)
            var reactAppProvider = new PhysicalFileProvider(_reactAppDistPath);
            _webApp.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = reactAppProvider,
                RequestPath = "" // Serve from root
            });
            _logger.LogInformation("Configured static files for React app from {Path}", _reactAppDistPath);

            // Serve Static Files for each discovered Web Plugin
            foreach (WebPluginManifest plugin in _discoveredWebPlugins)
            {
                if (!string.IsNullOrEmpty(plugin.DirectoryPath) && !string.IsNullOrEmpty(plugin.BasePath))
                {
                    var pluginProvider = new PhysicalFileProvider(plugin.DirectoryPath);
                    _webApp.UseStaticFiles(new StaticFileOptions
                    {
                        FileProvider = pluginProvider,
                        RequestPath = plugin.BasePath // Serve under /plugins/{pluginId}
                    });
                    _logger.LogInformation("Configured static files for plugin '{PluginName}' from {Path} at '{BasePath}'",
                        plugin.Name, plugin.DirectoryPath, plugin.BasePath);
                }
            }

            // --- Fallback Routing for SPA ---
            // Serve index.html for the root and common overlay paths to support client-side routing
            string indexPath = Path.Combine(_reactAppDistPath, "index.html");
            if (File.Exists(indexPath))
            {
                _webApp.MapGet("/", () => Results.File(indexPath, "text/html"));
                _webApp.MapGet("/chat", () => Results.File(indexPath, "text/html"));
                _webApp.MapGet("/subtimer", () => Results.File(indexPath, "text/html"));
                // Add other anticipated overlay routes here if needed
                _logger.LogInformation("Mapped SPA routes to serve index.html from {Path}", indexPath);
            }
            else
            {
                _logger.LogError("React app index.html not found at {Path}. SPA routing will fail.", indexPath);
                _webApp.MapGet("/", () => Results.NotFound("Overlay index.html not found."));
            }

            _logger.LogInformation("ASP.NET Core pipeline configured.");
            await _webApp.RunAsync(_cts.Token);
            _logger.LogInformation("Web host gracefully shut down.");
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Web host operation cancelled (likely due to StopAsync or Dispose).");
        }
        catch (IOException ioEx) when (ioEx.Message.Contains("address already in use", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogCritical(ioEx, "FATAL ERROR - Port {Port} is already in use. Please change the port in settings or close the conflicting application.", port);
            // Clean up partially started app if necessary
            if (_webApp != null) await _webApp.DisposeAsync();
            _webApp = null;
            _cts?.Dispose(); _cts = null;
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "FATAL ERROR starting web host.");
            if (_webApp != null) await _webApp.DisposeAsync();
            _webApp = null;
            _cts?.Dispose(); _cts = null;
        }
        finally
        {
            _logger.LogInformation("StartAsync final execution block reached.");
        }
    }

    private async Task HandleWebSocketConnection(HttpContext context)
    {
        if (context.WebSockets.IsWebSocketRequest)
        {
            _logger.LogInformation("WebSocket connection request received from {RemoteIpAddress}.", context.Connection.RemoteIpAddress);
            using WebSocket webSocket = await context.WebSockets.AcceptWebSocketAsync();
            // Pass discovered plugins to the manager when handling the connection
            await _webSocketManager.HandleNewSocketAsync(webSocket, _discoveredWebPlugins, _cts?.Token ?? CancellationToken.None);
        }
        else
        {
            _logger.LogWarning("Non-WebSocket request received at /ws endpoint from {RemoteIpAddress}.", context.Connection.RemoteIpAddress);
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
        }
    }

    private void DetermineWebPaths()
    {
        string baseDirectory = AppContext.BaseDirectory;
        // Attempt 1: Relative to Base Directory (Unpackaged Dev/Publish)
        _reactAppDistPath = Path.GetFullPath(Path.Combine(baseDirectory, "Web", "Overlay", "dist"));
        _pluginsWebPath = Path.GetFullPath(Path.Combine(baseDirectory, "Plugins", "Web"));

        _logger.LogDebug("Checking paths relative to Base Directory: {BaseDir}", baseDirectory);
        _logger.LogDebug("-> React Dist Path: {ReactPath}", _reactAppDistPath);
        _logger.LogDebug("-> Web Plugins Path: {PluginsPath}", _pluginsWebPath);

        // TODO: Add checks for Packaged App mode if needed, adjusting paths accordingly.
        // This might involve checking Windows.ApplicationModel.Package.Current.InstalledLocation.Path
        // Currently assuming unpackaged or structure relative to BaseDirectory.
        if (!Directory.Exists(_reactAppDistPath))
        {
            _logger.LogWarning("React 'dist' directory NOT found at primary location: {Path}", _reactAppDistPath);
            // Consider adding more fallback checks if necessary (e.g., relative to assembly)
        }

        if (!Directory.Exists(_pluginsWebPath))
        {
            _logger.LogInformation("Web Plugins directory does not exist at: {Path}. No web plugins will be loaded.", _pluginsWebPath);
            // Attempt to create it? Or just log? For now, just log.
            try { Directory.CreateDirectory(_pluginsWebPath); } catch (Exception ex) { _logger.LogWarning(ex, "Failed to create Web Plugins directory."); }
        }
    }

    private List<WebPluginManifest> DiscoverWebPlugins()
    {
        var discovered = new List<WebPluginManifest>();
        if (string.IsNullOrEmpty(_pluginsWebPath) || !Directory.Exists(_pluginsWebPath))
        {
            _logger.LogInformation("Skipping web plugin discovery - directory path is invalid or not found.");
            return discovered;
        }

        _logger.LogInformation("Discovering web plugins in: {PluginsWebPath}", _pluginsWebPath);
        try
        {
            foreach (string subDir in Directory.GetDirectories(_pluginsWebPath))
            {
                string manifestPath = Path.Combine(subDir, "manifest.json");
                if (!File.Exists(manifestPath))
                {
                    _logger.LogTrace("Skipping directory '{DirName}': manifest.json not found.", Path.GetFileName(subDir));
                    continue;
                }

                _logger.LogDebug("Found manifest: {ManifestPath}", manifestPath);
                try
                {
                    string manifestJson = File.ReadAllText(manifestPath);
                    WebPluginManifest? manifest = JsonSerializer.Deserialize<WebPluginManifest>(manifestJson, s_manifestSerializerOptions);

                    if (manifest != null && ValidateWebManifest(manifest, manifestPath))
                    {
                        manifest.DirectoryPath = subDir; // Store physical path
                        manifest.BasePath = $"/plugins/{manifest.Id}/"; // Calculate URL base path
                        discovered.Add(manifest);
                        _logger.LogInformation("Successfully loaded web plugin: {PluginName} (v{PluginVersion}) from {DirName}",
                            manifest.Name, manifest.Version, Path.GetFileName(subDir));
                    }
                }
                catch (JsonException jsonEx)
                {
                    _logger.LogError(jsonEx, "Error parsing manifest JSON {ManifestPath}", manifestPath);
                }
                catch (IOException ioEx)
                {
                    _logger.LogError(ioEx, "Error reading manifest file {ManifestPath}", manifestPath);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error processing web plugin manifest {ManifestPath}", manifestPath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error enumerating web plugin directories in {PluginsWebPath}", _pluginsWebPath);
        }

        return discovered;
    }

    private bool ValidateWebManifest(WebPluginManifest manifest, string manifestPath)
    {
        bool isValid = true;
        if (string.IsNullOrWhiteSpace(manifest.Id))
        {
            _logger.LogError("Invalid manifest {ManifestPath}: 'id' is missing or empty.", manifestPath);
            isValid = false;
        }
        if (string.IsNullOrWhiteSpace(manifest.Name))
        {
            _logger.LogError("Invalid manifest {ManifestPath}: 'name' is missing or empty.", manifestPath);
            isValid = false;
        }
        if (string.IsNullOrWhiteSpace(manifest.Version) || !Version.TryParse(manifest.Version, out _))
        {
            _logger.LogError("Invalid manifest {ManifestPath}: 'version' is missing or not a valid format.", manifestPath);
            isValid = false;
        }
        if (string.IsNullOrWhiteSpace(manifest.EntryScript))
        {
            _logger.LogError("Invalid manifest {ManifestPath}: 'entryScript' is missing or empty.", manifestPath);
            isValid = false;
        }
        // Optional fields don't need validation here unless they have format requirements

        return isValid;
    }

    public async Task StopAsync()
    {
        _logger.LogInformation("Stopping web server...");
        if (_cts == null || _webApp == null)
        {
            _logger.LogInformation("Stop requested but web server is not running or already stopping.");
            return;
        }

        try
        {
            if (!_cts.IsCancellationRequested)
            {
                _cts.Cancel();
            }
            // Allow some time for graceful shutdown
            await _webApp.StopAsync(TimeSpan.FromSeconds(5));
        }
        catch (ObjectDisposedException) { _logger.LogDebug("StopAsync ignored ObjectDisposedException (likely CTS already disposed)."); }
        catch (OperationCanceledException) { _logger.LogInformation("StopAsync cancellation occurred during web host stop."); }
        catch (Exception ex) { _logger.LogError(ex, "Error occurred during StopAsync."); }
        finally { _logger.LogInformation("StopAsync request processed."); }
    }

    public async void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _logger.LogInformation("Dispose called.");

        // Initiate stop but don't block indefinitely in Dispose
        try { await StopAsync(); } catch (Exception ex) { _logger.LogError(ex, "Exception during StopAsync call within Dispose."); }

        _cts?.Dispose();
        if (_webApp != null)
        {
            try { await _webApp.DisposeAsync(); } catch (Exception ex) { _logger.LogError(ex, "Exception disposing WebApplication within Dispose."); }
            _webApp = null;
        }
        _logger.LogInformation("Dispose finished.");
        GC.SuppressFinalize(this);
    }
}
