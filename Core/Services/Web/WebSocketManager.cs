using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Microsoft.Extensions.Logging;
using StreamWeaver.Core.Messaging;
using StreamWeaver.Core.Models.Settings;
using StreamWeaver.Core.Services.Settings;

namespace StreamWeaver.Core.Services.Web;

/// <summary>
/// Message indicating that the overlay settings have been updated.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="OverlaySettingsUpdateMessage"/> class.
/// </remarks>
/// <param name="value">The updated overlay settings.</param>
public class OverlaySettingsUpdateMessage(OverlaySettings value) : ValueChangedMessage<OverlaySettings>(value);

/// <summary>
/// Manages active WebSocket connections for overlays, handling message broadcasting and connection lifecycle.
/// </summary>
public partial class WebSocketManager : IRecipient<NewEventMessage>, IRecipient<OverlaySettingsUpdateMessage>, IDisposable
{
    private readonly ILogger<WebSocketManager> _logger;
    private readonly ConcurrentDictionary<Guid, WebSocket> _sockets = new();
    private readonly IMessenger _messenger;
    private readonly ISettingsService _settingsService;
    private bool _isDisposed;
    private static readonly JsonSerializerOptions s_jsonSerializerOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>
    /// Initializes a new instance of the <see cref="WebSocketManager"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="messenger">The messenger for receiving application events.</param>
    /// <param name="settingsService">The service for accessing current settings.</param>
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
    /// Handles the full lifecycle of a new WebSocket connection: adds the socket, sends initial settings,
    /// listens for messages/closure, and cleans up on disconnect or error.
    /// </summary>
    /// <param name="socket">The newly accepted WebSocket connection.</param>
    /// <param name="appShutdownToken">A token that signals application shutdown, used to gracefully close connections.</param>
    /// <returns>A task representing the asynchronous handling of the socket.</returns>
    public async Task HandleNewSocketAsync(WebSocket socket, CancellationToken appShutdownToken)
    {
        Guid socketId = Guid.NewGuid();
        if (!_sockets.TryAdd(socketId, socket))
        {
            _logger.LogWarning("Failed to add socket {SocketId} to collection. Aborting connection.", socketId);
            try
            {
                socket.Abort();
                socket.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Exception during socket abort/dispose for failed add {SocketId}.", socketId);
            }

            return;
        }

        _logger.LogInformation("Socket connected: {SocketId}", socketId);

        using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(appShutdownToken);
        CancellationToken cancellationToken = linkedCts.Token;

        try
        {
            _logger.LogDebug("Sending initial overlay settings to socket {SocketId}...", socketId);
            OverlaySettings initialSettings = _settingsService.CurrentSettings.Overlays; // Get a snapshot of current settings
            await SendSettingsUpdateAsync(socket, initialSettings, cancellationToken);

            // --- Start Receiving Loop ---
            byte[] buffer = new byte[1024 * 4];
            WebSocketReceiveResult result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
            while (!result.CloseStatus.HasValue && !cancellationToken.IsCancellationRequested)
            {
                if (result.MessageType == WebSocketMessageType.Text)
                {
                    string receivedJson = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    _logger.LogDebug("Socket {SocketId} received text message: {Message}", socketId, receivedJson);
                    // TODO: Implement processing logic if overlays need to send messages back (e.g., ping/pong, specific requests).
                }
                else if (result.MessageType == WebSocketMessageType.Binary)
                {
                    // Typically ignore binary messages unless a specific protocol requires them.
                    _logger.LogDebug("Socket {SocketId} received binary message ({Count} bytes, ignored).", socketId, result.Count);
                }

                // Continue listening for the next message or close notification.
                result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
            }

            // --- Handle Closure ---
            if (result.CloseStatus.HasValue)
            {
                // Client initiated the close handshake. Log the reason and acknowledge.
                _logger.LogInformation(
                    "Socket {SocketId} initiated close: Status {Status}, Description '{Description}'",
                    socketId,
                    result.CloseStatus.Value,
                    result.CloseStatusDescription
                );

                // Gracefully close the connection from the server side. Use CancellationToken.None for the close handshake.
                await socket.CloseAsync(result.CloseStatus.Value, result.CloseStatusDescription, CancellationToken.None);
            }
            else if (cancellationToken.IsCancellationRequested)
            {
                // Loop was exited due to cancellation (app shutdown or external signal).
                _logger.LogInformation("Socket {SocketId} handling cancelled externally (likely app shutdown). Closing socket.", socketId);
                // Attempt to close gracefully if the socket state allows.
                if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Server shutting down", CancellationToken.None);
                }
            }
        }
        catch (WebSocketException wsEx) when (wsEx.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
        {
            // Catch common exception when client disconnects abruptly (e.g., browser closed).
            _logger.LogInformation("Socket {SocketId} connection closed prematurely.", socketId);
        }
        catch (OperationCanceledException)
        {
            // Catch cancellation specifically triggered by the linkedCts token.
            _logger.LogInformation("Socket {SocketId} handling task cancelled.", socketId);
            // Attempt graceful close if possible.
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Server shutting down", CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            // Catch any other unexpected errors during socket handling.
            _logger.LogError(ex, "Error handling socket {SocketId}. State: {State}", socketId, socket.State);
            // Attempt to close the socket with an error status if it's still openable.
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                try
                {
                    // Indicate server error during close handshake.
                    await socket.CloseOutputAsync(WebSocketCloseStatus.InternalServerError, "Server error", CancellationToken.None);
                }
                catch (Exception closeEx)
                {
                    // Log secondary error during forced closure attempt.
                    _logger.LogError(closeEx, "Further error attempting to close socket {SocketId} after previous error.", socketId);
                }
            }
        }
        finally
        {
            // --- Cleanup ---
            // This block ensures the socket is always removed from the collection and disposed,
            // regardless of how the handling loop exited.
            RemoveSocket(socketId, socket);
            // The 'using' statement for linkedCts handles its disposal automatically.
        }
    }

    /// <summary>
    /// Removes a specified socket from the tracking dictionary and disposes it.
    /// </summary>
    /// <param name="socketId">The unique identifier of the socket to remove.</param>
    /// <param name="socket">The WebSocket instance to remove and dispose.</param>
    private void RemoveSocket(Guid socketId, WebSocket socket)
    {
        // Attempt to remove the socket from the concurrent dictionary.
        if (_sockets.TryRemove(socketId, out _))
        {
            _logger.LogInformation("Socket removed: {SocketId}", socketId);
        }
        else
        {
            // Log if removal was attempted but the socket wasn't found (might indicate double removal attempt).
            _logger.LogDebug("Attempted to remove socket {SocketId}, but it was not found (likely already removed).", socketId);
        }

        // Dispose the WebSocket object to release its resources.
        try
        {
            socket?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error disposing socket {SocketId}.", socketId);
        }
    }

    /// <summary>
    /// Receives <see cref="NewEventMessage"/> from the application's messenger.
    /// Serializes the event and broadcasts it to all connected WebSocket clients.
    /// </summary>
    /// <param name="message">The message containing the event data.</param>
    public void Receive(NewEventMessage message) =>
        // Use fire-and-forget Task pattern for broadcasting. Errors handled within BroadcastJsonAsync.
        _ = BroadcastJsonAsync(SerializeToJson(message.Value));

    /// <summary>
    /// Receives <see cref="OverlaySettingsUpdateMessage"/> when overlay settings change.
    /// Serializes the settings update and broadcasts it to all connected clients.
    /// </summary>
    /// <param name="message">The message containing the updated overlay settings.</param>
    public void Receive(OverlaySettingsUpdateMessage message)
    {
        _logger.LogDebug("Received OverlaySettingsUpdateMessage.");
        // Wrap the settings in a standardized payload structure for the client.
        var settingsPayload = new { type = "settingsUpdate", payload = message.Value };
        _ = BroadcastJsonAsync(SerializeToJson(settingsPayload));
    }

    /// <summary>
    /// Serializes an object to a JSON string using the shared serializer options.
    /// Ensures that the actual derived type is used for serialization, especially for event polymorphism.
    /// </summary>
    /// <param name="data">The object to serialize.</param>
    /// <returns>A JSON string representation of the object.</returns>
    private static string SerializeToJson(object data)
    {
        // Get the runtime type of the object to ensure correct serialization of derived types.
        Type typeToSerialize = data.GetType();
        return JsonSerializer.Serialize(data, typeToSerialize, s_jsonSerializerOptions);
    }

    /// <summary>
    /// Broadcasts a pre-serialized JSON string to all currently connected and open WebSocket clients.
    /// Handles potential errors during sending and cleans up disconnected sockets.
    /// </summary>
    /// <param name="jsonPayload">The JSON string to broadcast.</param>
    /// <returns>A task representing the asynchronous broadcast operation.</returns>
    private async Task BroadcastJsonAsync(string jsonPayload)
    {
        if (_isDisposed || _sockets.IsEmpty)
        {
            _logger.LogTrace("Broadcast requested, but manager is disposed or no clients are connected.");
            return;
        }

        byte[] messageBytes = Encoding.UTF8.GetBytes(jsonPayload);
        ArraySegment<byte> messageBuffer = new(messageBytes);
        _logger.LogTrace(
            "Broadcasting JSON ({Length} bytes) to {Count} clients: {PayloadPreview}...",
            messageBytes.Length,
            _sockets.Count,
            jsonPayload[..Math.Min(jsonPayload.Length, 150)]
        );

        List<Task> sendTasks = [];
        // Use a ConcurrentBag for thread-safe collection of sockets needing removal.
        ConcurrentBag<Guid> socketsToRemove = [];
        foreach (KeyValuePair<Guid, WebSocket> pair in _sockets)
        {
            Guid socketId = pair.Key;
            WebSocket socket = pair.Value;

            if (socket.State == WebSocketState.Open)
            {
                sendTasks.Add(
                    Task.Run(async () =>
                    {
                        try
                        {
                            // Send the message. Consider adding a timeout via CancellationToken if sends can hang.
                            await socket.SendAsync(messageBuffer, WebSocketMessageType.Text, true, CancellationToken.None);
                        }
                        catch (Exception ex) // Catch broadly including WebSocketException, ObjectDisposedException
                        {
                            _logger.LogWarning(
                                ex,
                                "Error sending message to socket {SocketId} (State: {State}). Marking for removal.",
                                socketId,
                                socket.State
                            );
                            socketsToRemove.Add(socketId);
                        }
                    })
                );
            }
            else
            {
                _logger.LogDebug("Socket {SocketId} is not open (State: {State}). Marking for removal during broadcast.", socketId, socket.State);
                socketsToRemove.Add(socketId);
            }
        }

        try
        {
            await Task.WhenAll(sendTasks);
        }
        catch (Exception ex)
        {
            // This catch block is unlikely to be hit if individual tasks handle their exceptions,
            // but log just in case Task.WhenAll itself throws.
            _logger.LogError(ex, "Unexpected exception during Task.WhenAll for broadcast sends.");
        }

        // --- Cleanup Failed/Closed Sockets ---
        // Process sockets marked for removal.
        if (!socketsToRemove.IsEmpty)
        {
            // Use Distinct() in case a socket was marked multiple times.
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
    /// Sends the overlay settings specifically to a single WebSocket client.
    /// Used primarily for sending initial settings upon connection.
    /// </summary>
    /// <param name="socket">The target WebSocket.</param>
    /// <param name="settings">The overlay settings to send.</param>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    /// <returns>A task representing the asynchronous send operation.</returns>
    private static async Task SendSettingsUpdateAsync(WebSocket socket, OverlaySettings settings, CancellationToken cancellationToken)
    {
        ILogger? logger = null; // Cannot easily access instance logger in static method
        try
        {
            logger = App.GetService<ILogger<WebSocketManager>>();
            var settingsPayload = new { type = "settingsUpdate", payload = settings };
            string jsonPayload = SerializeToJson(settingsPayload);
            byte[] messageBytes = Encoding.UTF8.GetBytes(jsonPayload);
            ArraySegment<byte> messageBuffer = new(messageBytes);

            if (socket.State == WebSocketState.Open)
            {
                await socket.SendAsync(messageBuffer, WebSocketMessageType.Text, true, cancellationToken);
                logger?.LogDebug("Sent initial overlay settings to socket.");
            }
            else
            {
                logger?.LogWarning("Could not send initial settings, socket state was {State}.", socket.State);
            }
        }
        catch (OperationCanceledException)
        {
            logger?.LogInformation("Sending initial settings was cancelled.");
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error sending initial settings.");
        }
    }

    /// <summary>
    /// Cleans up resources used by the WebSocketManager.
    /// Unregisters from the messenger and clears the socket collection.
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed)
            return;
        _isDisposed = true;

        _logger.LogInformation("Disposing...");
        _messenger.UnregisterAll(this);
        _sockets.Clear();

        _logger.LogInformation("Dispose finished.");
        GC.SuppressFinalize(this);
    }
}
