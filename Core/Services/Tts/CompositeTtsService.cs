using System.Collections.Concurrent;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using StreamWeaver.Core.Messaging;
using StreamWeaver.Core.Models.Events;
using StreamWeaver.Core.Models.Settings;
using StreamWeaver.Core.Services.Settings;

namespace StreamWeaver.Core.Services.Tts;

/// <summary>
/// Composite TTS service that orchestrates TTS operations based on settings,
/// delegating to the appropriate engine-specific service. Handles event processing
/// and queuing (basic queuing implemented here).
/// </summary>
public sealed partial class CompositeTtsService : ITtsService, IRecipient<NewEventMessage>, IDisposable
{
    private readonly ILogger<CompositeTtsService> _logger;
    private readonly ISettingsService _settingsService;
    private readonly IMessenger _messenger;
    private readonly TtsFormattingService _formattingService;
    private readonly Dictionary<string, IEngineSpecificTtsService> _ttsEngines;
    private readonly ConcurrentQueue<string> _speechQueue = new();
    private readonly SemaphoreSlim _speakLock = new(1, 1); // Lock for speaking one at a time
    private Task? _processingTask;
    private CancellationTokenSource? _cts;
    private bool _isDisposed = false;

    public CompositeTtsService(
        ILogger<CompositeTtsService> logger,
        ISettingsService settingsService,
        IMessenger messenger,
        TtsFormattingService formattingService,
        IEnumerable<IEngineSpecificTtsService> ttsEngines // Inject all registered engines
    )
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _messenger = messenger ?? throw new ArgumentNullException(nameof(messenger));
        _formattingService = formattingService ?? throw new ArgumentNullException(nameof(formattingService));

        // Create a dictionary for quick lookup by EngineId
        _ttsEngines = ttsEngines?.ToDictionary(e => e.EngineId, e => e)
                      ?? throw new ArgumentNullException(nameof(ttsEngines));

        _logger.LogInformation("CompositeTtsService Initialized with {EngineCount} engines.", _ttsEngines.Count);

        // Initialize engines asynchronously in the background
        _ = InitializeEnginesAsync();

        // Register for messages AFTER basic setup
        _messenger.Register<NewEventMessage>(this);
        _settingsService.SettingsUpdated += OnSettingsUpdated;

        // Start the queue processing task
        _cts = new CancellationTokenSource();
        _processingTask = ProcessSpeechQueueAsync(_cts.Token);
    }

    private async Task InitializeEnginesAsync()
    {
        _logger.LogInformation("Starting asynchronous initialization of TTS engines...");
        var initTasks = _ttsEngines.Values.Select(engine => engine.InitializeAsync()).ToList();
        try
        {
            await Task.WhenAll(initTasks);
            _logger.LogInformation("All TTS engines initialized.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during asynchronous initialization of one or more TTS engines.");
            // Engines that failed will likely log their own critical errors.
        }
    }

    private void OnSettingsUpdated(object? sender, EventArgs e) =>
        // Settings like rate/volume/voice are applied when SpeakAsync is called
        // or when GetInstalledVoicesAsync is called, ensuring the current settings are used.
        // We might need to re-apply if settings change *while* speaking, but the queue handles this.
        _logger.LogDebug("Settings updated event received by CompositeTtsService.");// Clear queue if TTS is disabled? Or let it finish? For now, let it finish.// if (!_settingsService.CurrentSettings.TextToSpeech.Enabled) { ClearQueue(); }

    public async Task<IEnumerable<string>> GetInstalledWindowsVoicesAsync()
    {
        if (_ttsEngines.TryGetValue(TtsSettings.WindowsEngine, out IEngineSpecificTtsService? engine))
        {
            return await engine.GetInstalledVoicesAsync();
        }

        _logger.LogWarning("Windows TTS engine not found.");
        return [];
    }

    public async Task<IEnumerable<string>> GetInstalledKokoroVoicesAsync()
    {
        if (_ttsEngines.TryGetValue(TtsSettings.KokoroEngine, out IEngineSpecificTtsService? engine))
        {
            return await engine.GetInstalledVoicesAsync();
        }

        _logger.LogWarning("Kokoro TTS engine not found.");
        return [];
    }

    // These Set methods apply the setting to the *currently selected* engine immediately.
    public void SetWindowsVoice(string voiceName)
    {
        if (_settingsService.CurrentSettings.TextToSpeech.SelectedEngine == TtsSettings.WindowsEngine)
        {
            if (_ttsEngines.TryGetValue(TtsSettings.WindowsEngine, out IEngineSpecificTtsService? engine))
            {
                engine.SetVoice(voiceName);
            }
        }
    }

    public void SetKokoroVoice(string voiceName)
    {
        if (_settingsService.CurrentSettings.TextToSpeech.SelectedEngine == TtsSettings.KokoroEngine)
        {
            if (_ttsEngines.TryGetValue(TtsSettings.KokoroEngine, out IEngineSpecificTtsService? engine))
            {
                engine.SetVoice(voiceName);
            }
        }
    }

    public void SetVolume(int volume)
    {
        string selectedEngineId = _settingsService.CurrentSettings.TextToSpeech.SelectedEngine;
        if (_ttsEngines.TryGetValue(selectedEngineId, out IEngineSpecificTtsService? engine))
        {
            engine.SetVolume(volume);
        }
    }

    public void SetRate(int rate)
    {
        string selectedEngineId = _settingsService.CurrentSettings.TextToSpeech.SelectedEngine;
        if (_ttsEngines.TryGetValue(selectedEngineId, out IEngineSpecificTtsService? engine))
        {
            engine.SetRate(rate);
        }
    }

    /// <summary>
    /// Adds text to the speech queue. The background task will process it.
    /// </summary>
    public Task SpeakAsync(string textToSpeak)
    {
        if (!_settingsService.CurrentSettings.TextToSpeech.Enabled)
        {
            _logger.LogTrace("SpeakAsync skipped: TTS is disabled.");
            return Task.CompletedTask;
        }

        if (string.IsNullOrWhiteSpace(textToSpeak))
        {
            _logger.LogTrace("SpeakAsync skipped: Text is null or whitespace.");
            return Task.CompletedTask;
        }

        if (string.IsNullOrWhiteSpace(textToSpeak))
        {
            _logger.LogDebug("SpeakAsync skipped: Text became empty after normalization. Original: '{Original}'", textToSpeak);
            return Task.CompletedTask;
        }


        _logger.LogDebug("Adding text to speech queue: \"{TextToSpeak}\"", textToSpeak);
        _speechQueue.Enqueue(textToSpeak);

        return Task.CompletedTask; // Return immediately, background task handles speaking
    }

    /// <summary>
    /// Processes events received via the messenger, formats them, and adds to the speech queue.
    /// </summary>
    public void Receive(NewEventMessage message)
    {
        if (!_settingsService.CurrentSettings.TextToSpeech.Enabled)
        {
            _logger.LogTrace("Receive(NewEventMessage) skipped: TTS is disabled.");
            return;
        }

        // Let the formatting service handle filtering and formatting
        string? textToSpeak = _formattingService.FormatAndNormalizeEvent(
            message.Value,
            _settingsService.CurrentSettings.TextToSpeech
        );

        if (!string.IsNullOrWhiteSpace(textToSpeak))
        {
            // Add the formatted and normalized text to the queue
            _speechQueue.Enqueue(textToSpeak);
            _logger.LogDebug("Enqueued formatted event text: \"{TextToSpeak}\"", textToSpeak);
        }
        else
        {
            _logger.LogTrace("Event type {EventType} resulted in empty text after formatting/normalization, not queued.", message.Value.GetType().Name);
        }
    }

    /// <summary>
    /// Background task to process the speech queue sequentially.
    /// </summary>
    private async Task ProcessSpeechQueueAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Speech queue processing task started.");
        while (!cancellationToken.IsCancellationRequested)
        {
            if (_speechQueue.TryDequeue(out string? textToSpeak))
            {
                // Check if TTS is still enabled before speaking
                if (!_settingsService.CurrentSettings.TextToSpeech.Enabled)
                {
                    _logger.LogTrace("Skipping dequeued item as TTS is now disabled.");
                    continue; // Skip this item
                }

                // Acquire the lock to ensure only one utterance at a time
                await _speakLock.WaitAsync(cancellationToken);
                try
                {
                    // Check cancellation again after acquiring lock
                    if (cancellationToken.IsCancellationRequested) break;

                    string selectedEngineId = _settingsService.CurrentSettings.TextToSpeech.SelectedEngine;
                    if (_ttsEngines.TryGetValue(selectedEngineId, out IEngineSpecificTtsService? engine))
                    {
                        _logger.LogDebug("Processing queued speech item using engine '{EngineId}': \"{TextToSpeak}\"", selectedEngineId, textToSpeak);

                        // Apply current settings just before speaking
                        TtsSettings currentSettings = _settingsService.CurrentSettings.TextToSpeech;
                        engine.SetRate(currentSettings.Rate);
                        engine.SetVolume(currentSettings.Volume);
                        string? voice = selectedEngineId == TtsSettings.WindowsEngine
                                        ? currentSettings.SelectedWindowsVoice
                                        : currentSettings.SelectedKokoroVoice;
                        if (!string.IsNullOrEmpty(voice))
                        {
                            engine.SetVoice(voice);
                        }
                        else
                        {
                            _logger.LogDebug("No specific voice selected for engine {EngineId}, using engine default.", selectedEngineId);
                            // TODO: Consider explicitly setting engine default if necessary/possible
                        }


                        await engine.SpeakAsync(textToSpeak);
                        // Optional: Add a small delay between utterances?
                        await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
                    }
                    else
                    {
                        _logger.LogWarning("Cannot speak queued item: Selected engine '{EngineId}' not found.", selectedEngineId);
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("Speech queue processing cancelled while speaking.");
                    break; // Exit loop on cancellation
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error speaking item from queue: \"{TextToSpeak}\"", textToSpeak);
                    // Continue processing the rest of the queue
                }
                finally
                {
                    _speakLock.Release();
                }
            }
            else
            {
                // Queue is empty, wait a bit before checking again
                await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
            }
        }

        _logger.LogInformation("Speech queue processing task finished.");
    }

    private void ClearQueue()
    {
        _logger.LogInformation("Clearing speech queue...");
        // Note: ConcurrentQueue doesn't have a Clear(). We need to dequeue until empty.
        while (_speechQueue.TryDequeue(out _)) { }

        _logger.LogInformation("Speech queue cleared.");
        // TODO: Should we also cancel the *currently speaking* item if queue is cleared?
        // If using System.Speech, _synthesizer.SpeakAsyncCancelAll() could be called here.
        // Need a way to signal cancellation to the active engine.
    }


    // ProcessEventForTts is effectively handled by Receive(NewEventMessage) now.
    // This method is kept to fulfill the ITtsService interface but delegates.
    public void ProcessEventForTts(BaseEvent eventData) => Receive(new NewEventMessage(eventData));


    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _logger.LogInformation("Disposing CompositeTtsService...");

        // Unregister message handler and event listener first
        _messenger.UnregisterAll(this);
        _settingsService.SettingsUpdated -= OnSettingsUpdated;

        // Signal cancellation to the processing task and wait for it to finish
        if (_cts != null)
        {
            _logger.LogDebug("Cancelling speech queue processing task...");
            _cts.Cancel();
            try
            {
                // Wait for the task to complete, with a timeout
                if (_processingTask != null && !_processingTask.Wait(TimeSpan.FromSeconds(3)))
                {
                    _logger.LogWarning("Speech queue processing task did not finish within the timeout period.");
                }
                else
                {
                    _logger.LogDebug("Speech queue processing task finished.");
                }
            }
            catch (AggregateException ae) when (ae.InnerExceptions.All(e => e is TaskCanceledException))
            {
                _logger.LogDebug("Speech queue processing task cancelled as expected.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error waiting for speech queue task during dispose.");
            }

            _cts.Dispose();
            _cts = null;
            _processingTask = null;
        }

        // Dispose engine-specific services
        _logger.LogDebug("Disposing engine-specific TTS services...");
        foreach (IEngineSpecificTtsService engine in _ttsEngines.Values)
        {
            try
            {
                engine.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing engine {EngineId}.", engine.EngineId);
            }
        }

        _ttsEngines.Clear();

        _speakLock.Dispose();

        _logger.LogInformation("CompositeTtsService disposed.");
        GC.SuppressFinalize(this);
    }
}
