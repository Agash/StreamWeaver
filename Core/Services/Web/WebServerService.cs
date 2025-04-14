using System.Net.WebSockets;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StreamWeaver.Core.Services.Settings;

namespace StreamWeaver.Core.Services.Web;

/// <summary>
/// Manages the embedded Kestrel web server for serving overlay files and handling WebSocket connections.
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
    private string _webRootPath = string.Empty;
    private bool _isDisposed;

    /// <summary>
    /// Starts the web server asynchronously.
    /// Configures Kestrel, logging, services, and the request pipeline.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
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

        _webRootPath = GetWebRootPath();
        if (string.IsNullOrEmpty(_webRootPath) || !Directory.Exists(_webRootPath))
        {
            _logger.LogCritical("Web root directory not found at '{WebRootPath}'. Overlays will not work.", _webRootPath);
            _cts.Dispose();
            _cts = null;
            return;
        }

        _logger.LogInformation("Using web root path: {WebRootPath}", _webRootPath);
        _logger.LogInformation("Starting web server on port {Port}...", port);

        try
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder();

            builder.WebHost.UseKestrel(options =>
            {
                options.ListenLocalhost(port);
                _logger.LogInformation("Kestrel configured to listen on http://localhost:{Port}", port);
            });

            builder.Logging.ClearProviders();
#if DEBUG
            builder.Logging.AddDebug();
#endif
            builder.Services.AddSingleton(_webSocketManager);
            builder.Services.AddSingleton(_settingsService);

            _webApp = builder.Build();
            _webApp.UseWebSockets();
            _webApp.Map(
                "/ws",
                async context =>
                {
                    if (context.WebSockets.IsWebSocketRequest)
                    {
                        _logger.LogInformation("WebSocket connection request received from {RemoteIpAddress}.", context.Connection.RemoteIpAddress);
                        using WebSocket webSocket = await context.WebSockets.AcceptWebSocketAsync();
                        await _webSocketManager.HandleNewSocketAsync(webSocket, _cts.Token);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "Non-WebSocket request received at /ws endpoint from {RemoteIpAddress}.",
                            context.Connection.RemoteIpAddress
                        );
                        context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    }
                }
            );

            PhysicalFileProvider fileProvider = new(_webRootPath);
            _webApp.UseStaticFiles(new StaticFileOptions { FileProvider = fileProvider, RequestPath = "" });
            _logger.LogInformation("Static file serving enabled from {WebRootPath}", _webRootPath);

            MapOverlayFile(_webApp, "/chat", "chat.html");
            MapOverlayFile(_webApp, "/subtimer", "subtimer.html");

            _webApp.MapGet("/", () => Results.Redirect("/chat"));
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
            _logger.LogCritical(
                ioEx,
                "FATAL ERROR - Port {Port} is already in use. Please change the port in settings or close the conflicting application.",
                port
            );

            if (_webApp != null)
                await _webApp.DisposeAsync();
            _webApp = null;
            _cts?.Dispose();
            _cts = null;
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "FATAL ERROR starting web host.");
            if (_webApp != null)
                await _webApp.DisposeAsync();
            _webApp = null;
            _cts?.Dispose();
            _cts = null;
        }
        finally
        {
            _logger.LogInformation("StartAsync final execution block reached.");
        }
    }

    /// <summary>
    /// Determines the absolute path to the 'Web/Overlay' directory, checking packaged and unpackaged locations.
    /// </summary>
    /// <returns>The absolute path to the web root, or empty string if not found.</returns>
    private string GetWebRootPath()
    {
        string path;

        // Check for Packaged App Scenario (MSIX)
        try
        {
            // Use reflection to avoid hard dependency on Windows SDK types if possible,
            // though a direct check might be cleaner if the project targets Windows specifically.
            Type? packageType = Type.GetType("Windows.ApplicationModel.Package, System.Runtime.WindowsRuntime");
            if (packageType != null)
            {
                object? currentPackage = packageType.GetProperty("Current")?.GetValue(null);
                if (currentPackage != null)
                {
                    dynamic? installedLocation = packageType.GetProperty("InstalledLocation")?.GetValue(currentPackage);
                    if (installedLocation != null)
                    {
                        string installPath = installedLocation.Path;
                        path = Path.Combine(installPath, "Web", "Overlay");
                        _logger.LogDebug("Detected packaged app mode. Install path: {InstallPath}", installPath);
                        if (Directory.Exists(path))
                        {
                            _logger.LogDebug("Found web root in packaged location: {Path}", path);
                            return path;
                        }

                        _logger.LogWarning("Packaged app overlay path not found: {Path}. Trying alternative locations.", path);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error checking for packaged app mode. This might be normal in unpackaged scenarios.");
        }

        // Check Unpackaged Scenario (Relative to Base Directory)
        string baseDirectory = AppContext.BaseDirectory;
        path = Path.Combine(baseDirectory, "Web", "Overlay");
        _logger.LogDebug("Checking unpackaged location relative to base directory: {BaseDirectory}", baseDirectory);
        if (Directory.Exists(path))
        {
            _logger.LogDebug("Found web root in unpackaged base directory location: {Path}", path);
            return path;
        }

        // Check Unpackaged Scenario (Relative to Executing Assembly) - Fallback
        _logger.LogWarning("Unpackaged overlay path not found relative to base directory: {Path}. Trying execution assembly directory.", path);
        string? assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        if (!string.IsNullOrEmpty(assemblyDir))
        {
            path = Path.Combine(assemblyDir, "Web", "Overlay");
            _logger.LogDebug("Checking unpackaged location relative to assembly directory: {AssemblyDirectory}", assemblyDir);
            if (Directory.Exists(path))
            {
                _logger.LogDebug("Found web root in unpackaged assembly directory location: {Path}", path);
                return path;
            }

            _logger.LogWarning("Unpackaged overlay path not found relative to assembly directory: {Path}", path);
        }

        // If none found
        _logger.LogError("Web root directory 'Web/Overlay' could not be found in standard packaged or unpackaged locations.");
        return string.Empty;
    }

    /// <summary>
    /// Maps a specific route to an HTML overlay file.
    /// </summary>
    /// <param name="app">The application's endpoint route builder.</param>
    /// <param name="route">The URL path (e.g., "/chat").</param>
    /// <param name="fileName">The corresponding HTML file name (e.g., "chat.html").</param>
    private void MapOverlayFile(IEndpointRouteBuilder app, string route, string fileName)
    {
        string filePath = Path.Combine(_webRootPath, fileName);
        if (File.Exists(filePath))
        {
            app.MapGet(
                route,
                async context =>
                {
                    context.Response.ContentType = GetContentType(fileName);
                    await Results.File(filePath, contentType: context.Response.ContentType).ExecuteAsync(context);
                }
            );
            _logger.LogInformation("Mapped route '{Route}' to file '{FilePath}'", route, filePath);
        }
        else
        {
            _logger.LogWarning("Overlay file not found for route '{Route}'. Expected at: {FilePath}", route, filePath);
            app.MapGet(route, () => Results.NotFound($"Overlay file '{fileName}' not found."));
        }
    }

    /// <summary>
    /// Determines the MIME content type based on file extension.
    /// </summary>
    /// <param name="path">The file path or name.</param>
    /// <returns>The corresponding MIME content type string.</returns>
    private static string GetContentType(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".html" or ".htm" => "text/html",
            ".css" => "text/css",
            ".js" => "application/javascript",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".svg" => "image/svg+xml",
            ".webp" => "image/webp",
            ".woff2" => "font/woff2",
            ".woff" => "font/woff",
            _ => "application/octet-stream",
        };

    /// <summary>
    /// Stops the web server asynchronously.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
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

            await _webApp.StopAsync(TimeSpan.FromSeconds(5));
        }
        catch (ObjectDisposedException)
        {
            _logger.LogDebug("StopAsync ignored ObjectDisposedException (likely CTS already disposed).");
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("StopAsync cancellation occurred during web host stop.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred during StopAsync.");
        }
        finally
        {
            _logger.LogInformation("StopAsync request processed.");
        }
    }

    /// <summary>
    /// Disposes resources used by the WebServerService.
    /// Consider implementing IAsyncDisposable for proper async cleanup.
    /// </summary>
    public async void Dispose() // Note: async void is discouraged; implement IAsyncDisposable instead.
    {
        if (_isDisposed)
            return;
        _isDisposed = true;

        _logger.LogInformation("Dispose called.");
        // Ensure stop is requested and awaited (best effort within sync Dispose limitations)
        // Using .Wait() or .Result here can lead to deadlocks in some contexts.
        // This highlights why IAsyncDisposable is preferred.
        try
        {
            await StopAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during StopAsync call within Dispose. Cleanup might be incomplete.");
        }

        _cts?.Dispose();

        if (_webApp != null)
        {
            try
            {
                await _webApp.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception disposing WebApplication within Dispose.");
            }

            _webApp = null;
        }

        _logger.LogInformation("Dispose finished.");
        GC.SuppressFinalize(this);
    }
}
