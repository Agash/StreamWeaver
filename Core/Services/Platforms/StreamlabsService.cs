using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using SocketIOClient;
using SocketIOClient.Transport;
using StreamWeaver.Core.Messaging;
using StreamWeaver.Core.Models.Events;
using StreamWeaver.Core.Models.Events.Messages;
using StreamWeaver.Core.Models.Settings;
using StreamWeaver.Core.Plugins;

namespace StreamWeaver.Core.Services.Platforms;

/// <summary>
/// Service responsible for connecting to the Streamlabs Socket API, receiving events,
/// parsing them into common event models, and distributing them via the Messenger and PluginService.
/// </summary>
public partial class StreamlabsService : ObservableObject, IStreamlabsClient, IDisposable
{
    private readonly IMessenger _messenger;
    private readonly PluginService _pluginService;
    private readonly ILogger<StreamlabsService> _logger;
    private SocketIOClient.SocketIO? _client;
    private string? _socketToken;
    private bool _isDisposed = false;

    /// <summary>
    /// Initializes a new instance of the <see cref="StreamlabsService"/> class.
    /// </summary>
    /// <param name="messenger">The application's messenger service.</param>
    /// <param name="pluginService">The service managing plugins and event routing.</param>
    /// <param name="logger">The logger instance.</param>
    /// <exception cref="ArgumentNullException">Thrown if messenger, pluginService, or logger is null.</exception>
    public StreamlabsService(IMessenger messenger, PluginService pluginService, ILogger<StreamlabsService> logger)
    {
        _messenger = messenger ?? throw new ArgumentNullException(nameof(messenger));
        _pluginService = pluginService ?? throw new ArgumentNullException(nameof(pluginService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _logger.LogInformation("Initialized.");
    }

    /// <summary>
    /// Gets or sets the current connection status to the Streamlabs Socket API.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConnected))]
    public partial ConnectionStatus Status { get; set; } = ConnectionStatus.Disconnected;

    /// <summary>
    /// Gets or sets a user-friendly message describing the current connection status or errors.
    /// </summary>
    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    /// <summary>
    /// Gets a value indicating whether the service is currently connected.
    /// </summary>
    public bool IsConnected => Status == ConnectionStatus.Connected;

    /// <summary>
    /// Attempts to connect to the Streamlabs Socket API using the provided socket token.
    /// </summary>
    /// <param name="socketToken">The API socket token obtained from Streamlabs.</param>
    /// <returns>
    /// A <see cref="Task"/> resulting in <c>true</c> if the connection attempt was initiated successfully
    /// (regardless of whether it connects immediately), and <c>false</c> if the attempt failed due to
    /// preconditions (e.g., missing token, already connected/connecting) or immediate errors.
    /// </returns>
    public async Task<bool> ConnectAsync(string socketToken)
    {
        if (Status is ConnectionStatus.Connected or ConnectionStatus.Connecting)
        {
            _logger.LogDebug("Connect requested but already {ConnectionStatus}.", Status);
            return Status == ConnectionStatus.Connected;
        }

        if (string.IsNullOrWhiteSpace(socketToken))
        {
            const string errorMsg = "Socket token is missing.";
            _logger.LogError("Connect failed - {ErrorMessage}", errorMsg);
            Status = ConnectionStatus.Error;
            StatusMessage = errorMsg;
            return false;
        }

        Status = ConnectionStatus.Connecting;
        StatusMessage = "Connecting...";
        _socketToken = socketToken;
        _logger.LogInformation("Attempting connection to Streamlabs Socket API...");

        await DisposeClientAsync();

        var uri = new Uri("https://sockets.streamlabs.com");

        _client = new SocketIOClient.SocketIO(
            uri,
            new SocketIOOptions
            {
                Query = new Dictionary<string, string> { { "token", _socketToken } },
                Transport = TransportProtocol.WebSocket,
                ConnectionTimeout = TimeSpan.FromSeconds(20),
            }
        );

        RegisterClientEvents();

        try
        {
            await _client.ConnectAsync();

            _logger.LogInformation("Connection attempt initiated.");
            return true;
        }
        catch (ConnectionException conEx)
        {
            _logger.LogError(conEx, "Connection failed during ConnectAsync: {ErrorMessage}", conEx.Message);
            Status = ConnectionStatus.Error;
            StatusMessage = $"Connection Failed: {conEx.Message}";
            await DisposeClientAsync();
            return false;
        }
        catch (TimeoutException timeEx)
        {
            _logger.LogError(timeEx, "Connection timed out during ConnectAsync.");
            Status = ConnectionStatus.Error;
            StatusMessage = "Connection Timed Out.";
            await DisposeClientAsync();
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during ConnectAsync: {ErrorMessage}", ex.Message);
            Status = ConnectionStatus.Error;
            StatusMessage = $"Connection Failed: {ex.Message}";
            await DisposeClientAsync();
            return false;
        }
    }

    /// <summary>
    /// Disconnects from the Streamlabs Socket API and cleans up resources.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task DisconnectAsync()
    {
        if (Status == ConnectionStatus.Disconnected && _client == null)
        {
            _logger.LogDebug("Disconnect requested but already disconnected.");
            return;
        }

        ConnectionStatus previousStatus = Status;
        _logger.LogInformation("Disconnecting from Streamlabs...");
        Status = ConnectionStatus.Disconnected;
        StatusMessage = "Disconnected.";

        await DisposeClientAsync();

        if (previousStatus != ConnectionStatus.Disconnected)
        {
            SystemMessageEvent systemEvent = new() { Platform = "Streamlabs", Message = "Disconnected from Streamlabs Events." };
            _messenger.Send(new NewEventMessage(systemEvent));
            _ = _pluginService.RouteEventToProcessorsAsync(systemEvent);
        }

        _logger.LogInformation("Disconnection process complete.");
    }

    /// <summary>
    /// Registers event handlers for the Socket.IO client instance.
    /// </summary>
    private void RegisterClientEvents()
    {
        if (_client == null)
            return;
        _logger.LogDebug("Registering Socket.IO client events...");
        _client.OnConnected += Client_OnConnected;
        _client.OnDisconnected += Client_OnDisconnected;
        _client.OnError += Client_OnError;
        _client.On("event", HandleStreamlabsEvent);
        _client.OnAny(Client_OnAny);
        _client.OnReconnectAttempt += Client_OnReconnectAttempt;
        _client.OnReconnected += Client_OnReconnected;
        _client.OnReconnectError += Client_OnReconnectError;
        _client.OnReconnectFailed += Client_OnReconnectFailed;
        _client.OnPing += Client_OnPing;
        _client.OnPong += Client_OnPong;
    }

    /// <summary>
    /// Unregisters event handlers from the Socket.IO client instance.
    /// </summary>
    private void UnregisterClientEvents()
    {
        if (_client == null)
            return;
        _logger.LogDebug("Unregistering Socket.IO client events...");

        try
        {
            _client.OnConnected -= Client_OnConnected;
            _client.OnDisconnected -= Client_OnDisconnected;
            _client.OnError -= Client_OnError;
            _client.Off("event");
            _client.OnAny(null);
            _client.OnReconnectAttempt -= Client_OnReconnectAttempt;
            _client.OnReconnected -= Client_OnReconnected;
            _client.OnReconnectError -= Client_OnReconnectError;
            _client.OnReconnectFailed -= Client_OnReconnectFailed;
            _client.OnPing -= Client_OnPing;
            _client.OnPong -= Client_OnPong;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Exception occurred during client event unregistration (ignoring).");
        }
    }

    /// <summary>
    /// Disposes the Socket.IO client instance, unregistering events and disconnecting if necessary.
    /// </summary>
    private async Task DisposeClientAsync()
    {
        if (_client == null)
            return;
        _logger.LogDebug("Disposing client instance...");

        UnregisterClientEvents();

        if (_client.Connected)
        {
            _logger.LogDebug("Client is connected, attempting explicit disconnect...");
            try
            {
                Task disconnectTask = _client.DisconnectAsync();
                if (await Task.WhenAny(disconnectTask, Task.Delay(TimeSpan.FromSeconds(5))) != disconnectTask)
                {
                    _logger.LogWarning("Explicit disconnect timed out after 5 seconds.");
                }

                _logger.LogDebug("Explicit disconnect completed or timed out.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error during explicit disconnect (ignoring): {ErrorMessage}", ex.Message);
            }
        }

        try
        {
            _logger.LogDebug("Calling client Dispose()...");
            _client.Dispose();
            _logger.LogDebug("Client Dispose() called.");
        }
        catch (ObjectDisposedException)
        {
            _logger.LogDebug("Client was already disposed.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during client dispose (ignoring): {ErrorMessage}", ex.Message);
        }

        _client = null;
        _socketToken = null;
        _logger.LogDebug("Client instance has been disposed and set to null.");
    }

    private void Client_OnConnected(object? sender, EventArgs e)
    {
        _logger.LogInformation("Successfully connected to Streamlabs Socket API!");
        Status = ConnectionStatus.Connected;
        StatusMessage = "Connected";
        SystemMessageEvent systemEvent = new() { Platform = "Streamlabs", Message = "Connected to Streamlabs Events." };

        _ = _pluginService.RouteEventToProcessorsAsync(systemEvent);
        _messenger.Send(new NewEventMessage(systemEvent));
    }

    private void Client_OnDisconnected(object? sender, string reason)
    {
        if (Status != ConnectionStatus.Disconnected)
        {
            _logger.LogWarning("Disconnected from Streamlabs. Reason: {Reason}", reason);

            bool isError = reason != "io client disconnect";
            Status = isError ? ConnectionStatus.Error : ConnectionStatus.Disconnected;
            StatusMessage = isError ? $"Disconnected: {reason}" : "Disconnected.";

            SystemMessageEvent systemEvent = new()
            {
                Platform = "Streamlabs",
                Message = $"Disconnected: {reason}",
                Level = isError ? SystemMessageLevel.Warning : SystemMessageLevel.Info,
            };

            _ = _pluginService.RouteEventToProcessorsAsync(systemEvent);
            _messenger.Send(new NewEventMessage(systemEvent));
        }
        else
        {
            _logger.LogDebug("OnDisconnected fired, but status was already Disconnected (likely manual). Reason: {Reason}", reason);
        }
    }

    private void Client_OnError(object? sender, string error)
    {
        _logger.LogError("Streamlabs Socket Error: {Error}", error);

        if (Status != ConnectionStatus.Error)
        {
            Status = ConnectionStatus.Error;
            StatusMessage = $"Error: {error}";
            SystemMessageEvent systemEvent = new()
            {
                Platform = "Streamlabs",
                Message = $"Error: {error}",
                Level = SystemMessageLevel.Error,
            };

            _ = _pluginService.RouteEventToProcessorsAsync(systemEvent);
            _messenger.Send(new NewEventMessage(systemEvent));
        }
    }

    private void Client_OnAny(string eventName, SocketIOResponse response)
    {
        if (eventName != "event")
        {
            _logger.LogTrace("Received unhandled Socket.IO event. Name: '{EventName}', Response: {Response}", eventName, response.ToString());
        }
    }

    private void Client_OnReconnectAttempt(object? sender, int attempt)
    {
        _logger.LogInformation("Attempting to reconnect to Streamlabs (Attempt #{Attempt})...", attempt);
        if (Status != ConnectionStatus.Connecting)
        {
            Status = ConnectionStatus.Connecting;
            StatusMessage = $"Reconnecting (Attempt {attempt})...";
        }
    }

    private void Client_OnReconnected(object? sender, int attempt)
    {
        _logger.LogInformation("Successfully reconnected to Streamlabs on attempt #{Attempt}!", attempt);
        Status = ConnectionStatus.Connected;
        StatusMessage = "Reconnected";
        SystemMessageEvent systemEvent = new() { Platform = "Streamlabs", Message = "Reconnected to Streamlabs Events." };

        _ = _pluginService.RouteEventToProcessorsAsync(systemEvent);
        _messenger.Send(new NewEventMessage(systemEvent));
    }

    private void Client_OnReconnectError(object? sender, Exception ex)
    {
        _logger.LogError(ex, "Error occurred during reconnect attempt: {ErrorMessage}", ex.Message);

        Status = ConnectionStatus.Error;
        StatusMessage = $"Reconnect Error: {ex.Message}";
    }

    private void Client_OnReconnectFailed(object? sender, EventArgs e)
    {
        _logger.LogError("Failed to reconnect to Streamlabs after multiple attempts.");
        Status = ConnectionStatus.Error;
        StatusMessage = "Reconnect Failed.";
        SystemMessageEvent systemEvent = new()
        {
            Platform = "Streamlabs",
            Message = "Failed to reconnect to Streamlabs.",
            Level = SystemMessageLevel.Error,
        };

        _ = _pluginService.RouteEventToProcessorsAsync(systemEvent);
        _messenger.Send(new NewEventMessage(systemEvent));
    }

    private void Client_OnPing(object? sender, EventArgs e) => _logger.LogTrace("Ping sent");

    private void Client_OnPong(object? sender, TimeSpan span) => _logger.LogTrace("Pong received ({PongTime}ms)", span.TotalMilliseconds);

    /// <summary>
    /// Handles incoming 'event' messages from the Streamlabs Socket API.
    /// Parses the payload and converts known event types into common BaseEvent models.
    /// </summary>
    /// <param name="response">The Socket.IO response containing the event data.</param>
    private void HandleStreamlabsEvent(SocketIOResponse response)
    {
        _logger.LogTrace("Received 'event' message from Streamlabs.");
        try
        {
            JsonElement eventDataArray = response.GetValue<JsonElement>();
            if (eventDataArray.ValueKind != JsonValueKind.Array)
            {
                _logger.LogWarning(
                    "Received Streamlabs event payload that was not a JSON array. Kind: {PayloadKind}, Payload: {Payload}",
                    eventDataArray.ValueKind,
                    response.ToString()
                );
                return;
            }

            foreach (JsonElement element in eventDataArray.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object)
                {
                    _logger.LogWarning("Skipping non-object element within Streamlabs event array. Kind: {ElementKind}", element.ValueKind);
                    continue;
                }

                if (!element.TryGetProperty("type", out JsonElement typeProp) || typeProp.ValueKind != JsonValueKind.String)
                {
                    _logger.LogWarning(
                        "Streamlabs event object missing 'type' property or it's not a string. Payload: {Payload}",
                        element.GetRawText()
                    );
                    continue;
                }

                string? eventType = typeProp.GetString();

                JsonElement? messagePayload = null;
                if (element.TryGetProperty("message", out JsonElement msgProp))
                {
                    if (msgProp.ValueKind == JsonValueKind.Array && msgProp.GetArrayLength() > 0)
                    {
                        if (msgProp[0].ValueKind == JsonValueKind.Object)
                        {
                            messagePayload = msgProp[0];
                        }
                        else
                        {
                            _logger.LogWarning(
                                "Streamlabs event type '{EventType}' message array element was not an object. Kind: {ElementKind}",
                                eventType,
                                msgProp[0].ValueKind
                            );
                        }
                    }
                    else if (msgProp.ValueKind == JsonValueKind.Object)
                    {
                        messagePayload = msgProp;
                    }
                    else
                    {
                        _logger.LogWarning(
                            "Streamlabs event type '{EventType}' had unexpected 'message' property kind: {PropertyKind}",
                            eventType,
                            msgProp.ValueKind
                        );
                    }
                }
                else if (element.TryGetProperty("data", out JsonElement dataProp) && dataProp.ValueKind == JsonValueKind.Object)
                {
                    messagePayload = dataProp;
                    _logger.LogDebug("Streamlabs event type '{EventType}' - using 'data' property for payload.", eventType);
                }

                if (messagePayload.HasValue && messagePayload.Value.ValueKind == JsonValueKind.Object)
                {
                    _logger.LogDebug(
                        "Processing Streamlabs event. Type: '{EventType}', Payload: {PayloadText}",
                        eventType,
                        messagePayload.Value.GetRawText()
                    );
                    BaseEvent? commonEvent = ConvertToCommonEvent(eventType, messagePayload.Value);
                    if (commonEvent != null)
                    {
                        _ = _pluginService.RouteEventToProcessorsAsync(commonEvent).ConfigureAwait(false);
                        _messenger.Send(new NewEventMessage(commonEvent));
                        _logger.LogInformation("Processed and dispatched Streamlabs event: {EventType}", commonEvent.GetType().Name);
                    }
                }
                else
                {
                    _logger.LogWarning(
                        "Streamlabs event type '{EventType}' received without a standard message/data object payload. Element: {ElementText}",
                        eventType,
                        element.GetRawText()
                    );
                }
            }
        }
        catch (JsonException jsonEx)
        {
            _logger.LogError(jsonEx, "Failed to parse Streamlabs event JSON. Payload: {Payload}", response.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling Streamlabs event: {ErrorMessage}", ex.Message);
        }
    }

    /// <summary>
    /// Converts a raw Streamlabs event type and payload into a standardized BaseEvent model.
    /// </summary>
    /// <param name="type">The event type string from Streamlabs.</param>
    /// <param name="payload">The JSON payload object for the event.</param>
    /// <returns>A derived <see cref="BaseEvent"/> if the type is recognized and parsed, otherwise null.</returns>
    private BaseEvent? ConvertToCommonEvent(string? type, JsonElement payload)
    {
        switch (type?.ToLowerInvariant())
        {
            case "donation":
                return ParseDonation(payload);

            case "follow":
                return ParseFollow(payload);

            case "subscription":
            case "resub":
                return ParseSubscription(payload);

            case "host":
                return ParseHost(payload);

            case "raid":
                return ParseRaid(payload);

            case "bits":
                return ParseBits(payload);

            default:
                _logger.LogDebug("Unhandled Streamlabs event type '{EventType}'. Payload: {PayloadText}", type, payload.GetRawText());
                return null;
        }
    }

    /// <summary>
    /// Parses the JSON payload for a Streamlabs donation event.
    /// </summary>
    private static DonationEvent? ParseDonation(JsonElement payload)
    {
        try
        {
            string? name = payload.TryGetProperty("name", out JsonElement nameEl) ? nameEl.GetString() : "Anonymous";
            decimal amount =
                payload.TryGetProperty("amount", out JsonElement amountEl) && decimal.TryParse(amountEl.GetString(), out decimal parsedAmount)
                    ? parsedAmount
                    : 0M;
            string? currency = payload.TryGetProperty("currency", out JsonElement currEl) ? currEl.GetString() : "USD";
            string? message = payload.TryGetProperty("message", out JsonElement msgEl) ? msgEl.GetString() : null;
            string donationId =
                payload.TryGetProperty("donation_id", out JsonElement idEl) && idEl.ValueKind == JsonValueKind.Number ? idEl.GetInt64().ToString()
                : payload.TryGetProperty("_id", out JsonElement underscoreIdEl) ? underscoreIdEl.GetString() ?? Guid.NewGuid().ToString()
                : Guid.NewGuid().ToString();
            DateTime timestamp =
                payload.TryGetProperty("created_at", out JsonElement tsEl) && DateTime.TryParse(tsEl.GetString(), out DateTime ts)
                    ? ts.ToUniversalTime()
                    : DateTime.UtcNow;

            return new DonationEvent
            {
                Platform = "Streamlabs",
                Timestamp = timestamp,
                DonationId = donationId,
                UserId = null,
                Username = name,
                Amount = amount,
                Currency = currency,
                RawMessage = message,
                ParsedMessage = string.IsNullOrWhiteSpace(message) ? [] : [new TextSegment { Text = message }],
                Type = DonationType.Streamlabs,
            };
        }
        catch (Exception ex)
        {
            App.GetService<ILogger<StreamlabsService>>()
                .LogError(ex, "Failed to parse Streamlabs donation payload. Payload: {PayloadText}", payload.GetRawText());
            return null;
        }
    }

    /// <summary>
    /// Parses the JSON payload for a Streamlabs follow event.
    /// </summary>
    private static FollowEvent? ParseFollow(JsonElement payload)
    {
        try
        {
            string? name = payload.TryGetProperty("name", out JsonElement nameEl) ? nameEl.GetString() : "Someone";

            string? userId =
                payload.TryGetProperty("twitch_id", out JsonElement tIdEl) ? tIdEl.GetString()
                : payload.TryGetProperty("id", out JsonElement idEl) ? idEl.GetString()
                : null;
            DateTime timestamp =
                payload.TryGetProperty("created_at", out JsonElement tsEl) && DateTime.TryParse(tsEl.GetString(), out DateTime ts)
                    ? ts.ToUniversalTime()
                    : DateTime.UtcNow;

            return new FollowEvent
            {
                Platform = "Streamlabs",
                Timestamp = timestamp,
                UserId = userId,
                Username = name,
            };
        }
        catch (Exception ex)
        {
            App.GetService<ILogger<StreamlabsService>>()
                .LogError(ex, "Failed to parse Streamlabs follow payload. Payload: {PayloadText}", payload.GetRawText());
            return null;
        }
    }

    /// <summary>
    /// Parses the JSON payload for a Streamlabs subscription or resub event.
    /// </summary>
    private static SubscriptionEvent? ParseSubscription(JsonElement payload)
    {
        try
        {
            string? name = payload.TryGetProperty("name", out JsonElement nameEl) ? nameEl.GetString() : "Someone";
            bool isGift = payload.TryGetProperty("gifter", out JsonElement gifterEl) && !string.IsNullOrEmpty(gifterEl.GetString());
            string? gifterName = isGift ? gifterEl.GetString() : null;
            int months = payload.TryGetProperty("months", out JsonElement monthsEl) && monthsEl.TryGetInt32(out int m) ? m : 1;
            string? message = payload.TryGetProperty("message", out JsonElement msgEl) ? msgEl.GetString() : null;
            string plan = payload.TryGetProperty("sub_plan", out JsonElement planEl) ? planEl.GetString() ?? "Unknown" : "Unknown";
            string tier = MapSubPlan(plan);
            DateTime timestamp =
                payload.TryGetProperty("created_at", out JsonElement tsEl) && DateTime.TryParse(tsEl.GetString(), out DateTime ts)
                    ? ts.ToUniversalTime()
                    : DateTime.UtcNow;

            int? cumulativeMonths = payload.TryGetProperty("streak_months", out JsonElement streakEl) && streakEl.TryGetInt32(out int s) ? s : null;

            return new SubscriptionEvent
            {
                Platform = "Streamlabs",
                Timestamp = timestamp,
                UserId = null,
                Username = isGift ? gifterName : name,
                IsGift = isGift,
                RecipientUsername = isGift ? name : null,
                Months = months,
                Tier = tier,
                CumulativeMonths = cumulativeMonths ?? 0,
                Message = message,
            };
        }
        catch (Exception ex)
        {
            App.GetService<ILogger<StreamlabsService>>()
                .LogError(ex, "Failed to parse Streamlabs subscription payload. Payload: {PayloadText}", payload.GetRawText());
            return null;
        }
    }

    /// <summary>
    /// Parses the JSON payload for a Streamlabs bits event (cheer).
    /// </summary>
    private static DonationEvent? ParseBits(JsonElement payload)
    {
        try
        {
            string? name = payload.TryGetProperty("name", out JsonElement nameEl) ? nameEl.GetString() : "Anonymous";
            int bitsAmount = payload.TryGetProperty("amount", out JsonElement amountEl) && amountEl.TryGetInt32(out int b) ? b : 0;
            string? message = payload.TryGetProperty("message", out JsonElement msgEl) ? msgEl.GetString() : null;

            string eventId = payload.TryGetProperty("_id", out JsonElement idEl)
                ? idEl.GetString() ?? Guid.NewGuid().ToString()
                : Guid.NewGuid().ToString();
            DateTime timestamp =
                payload.TryGetProperty("created_at", out JsonElement tsEl) && DateTime.TryParse(tsEl.GetString(), out DateTime ts)
                    ? ts.ToUniversalTime()
                    : DateTime.UtcNow;

            return new DonationEvent
            {
                Platform = "Streamlabs",
                Timestamp = timestamp,
                DonationId = eventId,
                UserId = null,
                Username = name,
                Amount = bitsAmount,
                Currency = "Bits",
                RawMessage = message,
                ParsedMessage = string.IsNullOrWhiteSpace(message) ? [] : [new TextSegment { Text = message }],
                Type = DonationType.Bits,
            };
        }
        catch (Exception ex)
        {
            App.GetService<ILogger<StreamlabsService>>()
                .LogError(ex, "Failed to parse Streamlabs bits payload. Payload: {PayloadText}", payload.GetRawText());
            return null;
        }
    }

    /// <summary>
    /// Parses the JSON payload for a Streamlabs raid event.
    /// </summary>
    private static RaidEvent? ParseRaid(JsonElement payload)
    {
        try
        {
            string? name = payload.TryGetProperty("name", out JsonElement nameEl) ? nameEl.GetString() : "Someone";
            int viewerCount = payload.TryGetProperty("raiders", out JsonElement countEl) && countEl.TryGetInt32(out int c) ? c : 0;
            DateTime timestamp = DateTime.UtcNow;

            return new RaidEvent
            {
                Platform = "Streamlabs",
                Timestamp = timestamp,
                RaiderUsername = name,
                RaiderUserId = null,
                ViewerCount = viewerCount,
            };
        }
        catch (Exception ex)
        {
            App.GetService<ILogger<StreamlabsService>>()
                .LogError(ex, "Failed to parse Streamlabs raid payload. Payload: {PayloadText}", payload.GetRawText());
            return null;
        }
    }

    /// <summary>
    /// Parses the JSON payload for a Streamlabs host event.
    /// </summary>
    private static HostEvent? ParseHost(JsonElement payload)
    {
        try
        {
            string? name = payload.TryGetProperty("name", out JsonElement nameEl) ? nameEl.GetString() : "Someone";
            int viewerCount = payload.TryGetProperty("viewers", out JsonElement countEl) && countEl.TryGetInt32(out int c) ? c : 0;
            DateTime timestamp = DateTime.UtcNow;

            return new HostEvent
            {
                Platform = "Streamlabs",
                Timestamp = timestamp,
                HosterUsername = name,
                //HosterUserId = null,
                ViewerCount = viewerCount,
                IsAutoHost = false,
            };
        }
        catch (Exception ex)
        {
            App.GetService<ILogger<StreamlabsService>>()
                .LogError(ex, "Failed to parse Streamlabs host payload. Payload: {PayloadText}", payload.GetRawText());
            return null;
        }
    }

    /// <summary>
    /// Maps Streamlabs subscription plan identifiers to user-friendly tier names.
    /// </summary>
    /// <param name="plan">The subscription plan identifier from Streamlabs.</param>
    /// <returns>A user-friendly tier name (e.g., "Tier 1", "Twitch Prime").</returns>
    private static string MapSubPlan(string? plan) =>
        plan switch
        {
            "Prime" => "Twitch Prime",
            "1000" => "Tier 1",
            "2000" => "Tier 2",
            "3000" => "Tier 3",
            _ => !string.IsNullOrWhiteSpace(plan) ? $"Unknown ({plan})" : "Unknown Tier",
        };

    /// <summary>
    /// Releases the resources used by the <see cref="StreamlabsService"/>.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases the managed and unmanaged resources used by the <see cref="StreamlabsService"/>.
    /// </summary>
    /// <param name="disposing">True to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (_isDisposed)
            return;

        _logger.LogDebug("Dispose({Disposing}) called.", disposing);

        if (disposing)
        {
            try
            {
                _logger.LogDebug("Initiating async client disposal from Dispose method...");
                DisposeClientAsync().Wait(TimeSpan.FromSeconds(3));
                _logger.LogDebug("Async client disposal completed or timed out.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception during sync-over-async disposal of client.");
            }
        }

        _isDisposed = true;
        _logger.LogInformation("Service disposed.");
    }
}
