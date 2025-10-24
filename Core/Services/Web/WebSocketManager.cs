using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Microsoft.Extensions.Logging;
using StreamWeaver.Core.Messaging;
using StreamWeaver.Core.Models.Events;
using StreamWeaver.Core.Models.Settings;
using StreamWeaver.Core.Plugins;
using StreamWeaver.Core.Services.Settings;

namespace StreamWeaver.Core.Services.Web;

/// <summary>
/// Message indicating that the overlay settings have been updated.
/// </summary>
/// <param name="value">The updated overlay settings.</param>
public class OverlaySettingsUpdateMessage(OverlaySettings value) : ValueChangedMessage<OverlaySettings>(value);

/// <summary>
/// Manages active WebSocket connections for overlays, handling message broadcasting and connection lifecycle.
/// Sends initial configuration including settings and discovered web plugins. Relies on <c>[JsonDerivedType]</c>
/// attributes on base event/segment classes for polymorphic serialization.
/// </summary>
public partial class WebSocketManager : IRecipient<NewEventMessage>, IRecipient<OverlaySettingsUpdateMessage>, IDisposable
{
    private readonly ILogger<WebSocketManager> _logger;
    private readonly ConcurrentDictionary<Guid, WebSocket> _sockets = new();
    private readonly IMessenger _messenger;
    private readonly ISettingsService _settingsService;
    private bool _isDisposed;

    /// <summary>
    /// JSON serialization options configured for web overlay communication.
    /// Handles camel casing and reference cycles. Polymorphism is handled via attributes on the types themselves.
    /// </summary>
    private static readonly JsonSerializerOptions s_jsonSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="WebSocketManager"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="messenger">The application messenger.</param>
    /// <param name="settingsService">The settings service.</param>
    public WebSocketManager(ILogger<WebSocketManager> logger, IMessenger messenger, ISettingsService settingsService)
    {
        _logger = logger;
        _messenger = messenger;
        _settingsService = settingsService;
        _messenger.Register<NewEventMessage>(this);
        _messenger.Register<OverlaySettingsUpdateMessage>(this);
        _logger.LogInformation("Initialized and registered for NewEventMessage and OverlaySettingsUpdateMessage.");
    }

    /// <summary>
    /// Handles the full lifecycle of a new WebSocket connection: adds the socket, sends initial configuration,
    /// listens for messages/closure, and cleans up on disconnect or error.
    /// </summary>
    /// <param name="socket">The newly accepted WebSocket connection.</param>
    /// <param name="discoveredPlugins">The list of discovered web plugins to send to the client.</param>
    /// <param name="appShutdownToken">A token that signals application shutdown.</param>
    /// <returns>A task representing the asynchronous handling of the socket.</returns>
    public async Task HandleNewSocketAsync(WebSocket socket, IEnumerable<WebPluginManifest> discoveredPlugins, CancellationToken appShutdownToken)
    {
        Guid socketId = Guid.NewGuid();
        if (!_sockets.TryAdd(socketId, socket))
        {
            _logger.LogWarning("Failed to add socket {SocketId} to collection. Aborting connection.", socketId);
            try { socket.Abort(); socket.Dispose(); } catch (Exception ex) { _logger.LogWarning(ex, "Exception during socket abort/dispose for failed add {SocketId}.", socketId); }
            return;
        }
        _logger.LogInformation("Socket connected: {SocketId}", socketId);

        // Link the socket's lifetime cancellation to the application shutdown token.
        using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(appShutdownToken);
        CancellationToken cancellationToken = linkedCts.Token;

        try
        {
            await SendInitialConfigurationAsync(socket, discoveredPlugins, cancellationToken);

            byte[] buffer = new byte[1024 * 4];
            WebSocketReceiveResult result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

            while (!result.CloseStatus.HasValue && !cancellationToken.IsCancellationRequested)
            {
                // Process incoming messages if needed (currently just logs)
                if (result.MessageType == WebSocketMessageType.Text)
                {
                    string receivedJson = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    _logger.LogDebug("Socket {SocketId} received text message: {Message}", socketId, receivedJson);
                }
                else if (result.MessageType == WebSocketMessageType.Binary)
                {
                    _logger.LogDebug("Socket {SocketId} received binary message ({Count} bytes, ignored).", socketId, result.Count);
                }
                result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
            }

            // Handle socket closure initiated by client or server cancellation.
            if (result.CloseStatus.HasValue)
            {
                _logger.LogInformation("Socket {SocketId} initiated close: Status {Status}, Description '{Description}'", socketId, result.CloseStatus.Value, result.CloseStatusDescription);
                if (socket.State != WebSocketState.Closed && socket.State != WebSocketState.Aborted)
                {
                    await socket.CloseAsync(result.CloseStatus.Value, result.CloseStatusDescription, CancellationToken.None);
                }
            }
            else if (cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation("Socket {SocketId} handling cancelled externally. Closing socket.", socketId);
                if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Server shutting down", CancellationToken.None);
                }
            }
        }
        catch (WebSocketException wsEx) when (wsEx.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
        {
            _logger.LogInformation("Socket {SocketId} connection closed prematurely.", socketId);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Socket {SocketId} handling task cancelled.", socketId);
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                try { await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Server shutting down", CancellationToken.None); } catch { /* Ignore errors during cancellation close */ }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling socket {SocketId}. State: {State}", socketId, socket.State);
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                try { await socket.CloseOutputAsync(WebSocketCloseStatus.InternalServerError, "Server error", CancellationToken.None); } catch (Exception closeEx) { _logger.LogError(closeEx, "Further error attempting to close socket {SocketId} after previous error.", socketId); }
            }
        }
        finally
        {
            RemoveSocket(socketId, socket);
        }
    }

    /// <summary>
    /// Removes a WebSocket connection from the manager and disposes it.
    /// </summary>
    /// <param name="socketId">The unique identifier of the socket.</param>
    /// <param name="socket">The WebSocket instance to remove and dispose.</param>
    private void RemoveSocket(Guid socketId, WebSocket socket)
    {
        if (_sockets.TryRemove(socketId, out _))
        {
            _logger.LogInformation("Socket removed: {SocketId}", socketId);
        }
        else
        {
            _logger.LogDebug("Attempted to remove socket {SocketId}, but it was not found (likely already removed).", socketId);
        }
        try { socket?.Dispose(); } catch (Exception ex) { _logger.LogWarning(ex, "Error disposing socket {SocketId}.", socketId); }
    }

    /// <summary>
    /// Sends the initial configuration (settings + plugins) to a newly connected WebSocket client.
    /// </summary>
    /// <param name="socket">The target WebSocket.</param>
    /// <param name="plugins">The list of discovered web plugins.</param>
    /// <param name="cancellationToken">Cancellation token for the send operation.</param>
    private async Task SendInitialConfigurationAsync(WebSocket socket, IEnumerable<WebPluginManifest> plugins, CancellationToken cancellationToken)
    {
        try
        {
            OverlaySettings currentSettings = _settingsService.CurrentSettings.Overlays;
            var pluginInfoList = plugins.Select(p => new
            {
                p.Id,
                p.Name,
                p.Version,
                p.Author,
                p.Description,
                p.EntryScript,
                p.EntryStyle,
                p.ProvidesComponents,
                p.RegistersWebComponents,
                p.BasePath
            }).ToList();

            var initPayload = new { type = "init", payload = new { settings = currentSettings, plugins = pluginInfoList } };
            string jsonPayload = JsonSerializer.Serialize(initPayload, s_jsonSerializerOptions);
            byte[] messageBytes = Encoding.UTF8.GetBytes(jsonPayload);
            ArraySegment<byte> messageBuffer = new(messageBytes);

            if (socket.State == WebSocketState.Open)
            {
                await socket.SendAsync(messageBuffer, WebSocketMessageType.Text, true, cancellationToken);
                _logger.LogDebug("Sent initial configuration ({PayloadSize} bytes) to socket.", messageBytes.Length);
            }
            else
            {
                _logger.LogWarning("Could not send initial configuration, socket state was {State}.", socket.State);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Sending initial configuration was cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending initial configuration.");
        }
    }

    /// <summary>
    /// Receives a new event message from the application's messenger.
    /// Serializes the event (with derived type info via attributes) and broadcasts it to connected clients.
    /// </summary>
    /// <param name="message">The incoming event message.</param>
    public void Receive(NewEventMessage message)
    {
        if (_isDisposed) return;

        BaseEvent eventPayload = message.Value;
        var messageWrapper = new { type = "event", payload = eventPayload };
        string json = JsonSerializer.Serialize(messageWrapper, s_jsonSerializerOptions);

        _ = BroadcastJsonAsync(json);
    }

    /// <summary>
    /// Receives an overlay settings update message from the application's messenger.
    /// Serializes the updated settings and broadcasts them to connected clients.
    /// </summary>
    /// <param name="message">The incoming settings update message.</param>
    public void Receive(OverlaySettingsUpdateMessage message)
    {
        if (_isDisposed) return;
        _logger.LogDebug("Received OverlaySettingsUpdateMessage. Broadcasting...");
        var messagePayload = new { type = "settings", payload = message.Value };
        string json = JsonSerializer.Serialize(messagePayload, s_jsonSerializerOptions);
        _ = BroadcastJsonAsync(json);
    }

    /// <summary>
    /// Broadcasts a JSON payload string to all currently connected WebSocket clients.
    /// Handles potential send errors and removes disconnected clients.
    /// </summary>
    /// <param name="jsonPayload">The JSON string to broadcast.</param>
    private async Task BroadcastJsonAsync(string jsonPayload)
    {
        if (_isDisposed || _sockets.IsEmpty) return;

        byte[] messageBytes = Encoding.UTF8.GetBytes(jsonPayload);
        ArraySegment<byte> messageBuffer = new(messageBytes);
        _logger.LogTrace("Broadcasting JSON ({Length} bytes) to {Count} clients: {PayloadPreview}...", messageBytes.Length, _sockets.Count, jsonPayload[..Math.Min(jsonPayload.Length, 150)]);

        List<Task> sendTasks = [];
        ConcurrentBag<Guid> socketsToRemove = [];

        foreach (KeyValuePair<Guid, WebSocket> pair in _sockets)
        {
            Guid socketId = pair.Key;
            WebSocket socket = pair.Value;
            if (socket.State == WebSocketState.Open)
            {
                // Add the send operation as a task to run concurrently.
                sendTasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        await socket.SendAsync(messageBuffer, WebSocketMessageType.Text, true, CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error sending message to socket {SocketId} (State: {State}). Marking for removal.", socketId, socket.State);
                        socketsToRemove.Add(socketId);
                    }
                }));
            }
            else
            {
                _logger.LogDebug("Socket {SocketId} is not open (State: {State}). Marking for removal during broadcast.", socketId, socket.State);
                socketsToRemove.Add(socketId);
            }
        }

        try
        {
            // Wait for all send tasks to complete, with a timeout.
            await Task.WhenAll(sendTasks).WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("Timeout waiting for some WebSocket send operations to complete during broadcast.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected exception during Task.WhenAll for broadcast sends.");
        }

        // Clean up sockets that failed to send or were already closed.
        if (!socketsToRemove.IsEmpty)
        {
            IEnumerable<Guid> distinctIdsToRemove = socketsToRemove.Distinct();
            _logger.LogInformation("Removing {Count} disconnected/failed sockets after broadcast.", distinctIdsToRemove.Count());
            foreach (Guid idToRemove in distinctIdsToRemove)
            {
                if (_sockets.TryGetValue(idToRemove, out WebSocket? socketToRemove))
                {
                    RemoveSocket(idToRemove, socketToRemove);
                }
            }
        }
    }

    /// <summary>
    /// Disposes the WebSocketManager, unregistering from messages and closing all active connections.
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _logger.LogInformation("Disposing WebSocketManager...");
        _messenger.UnregisterAll(this);

        List<Task> closeTasks = [];
        foreach (Guid socketId in _sockets.Keys.ToList())
        {
            if (_sockets.TryRemove(socketId, out WebSocket? socket))
            {
                if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
                {
                    closeTasks.Add(Task.Run(async () => {
                        try
                        {
                            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Server shutting down", CancellationToken.None);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Exception closing socket {SocketId} during dispose.", socketId);
                        }
                        finally
                        {
                            try { socket.Dispose(); } catch { /* Ensure disposal */ }
                        }
                    }));
                }
                else
                {
                    try { socket.Dispose(); } catch { /* Dispose directly if not open */ }
                }
            }
        }

        try { Task.WhenAll(closeTasks).Wait(TimeSpan.FromSeconds(2)); } catch { /* Ignore exceptions during dispose cleanup */ }
        _sockets.Clear();
        _logger.LogInformation("WebSocketManager Dispose finished.");
        GC.SuppressFinalize(this);
    }
}
