// -------- START: StreamWeaver/Core/Services/Tts/KokoroTtsService.cs --------
using System.Collections.Concurrent; // Needed for BlockingCollection/ConcurrentQueue potentially used by KokoroEngine
using KokoroSharp;
using KokoroSharp.Core;
using KokoroSharp.Processing; // Needed for Tokenizer, SegmentationSystem
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime; // Needed for SessionOptions (though maybe not directly used here yet)
using StreamWeaver.Core.Models.Settings;
using StreamWeaver.Core.Services.Settings;

namespace StreamWeaver.Core.Services.Tts;

/// <summary>
/// Provides Text-to-Speech (TTS) functionality using the KokoroSharp library.
/// Handles model loading, voice management, inference via KokoroEngine, and playback via KokoroPlayback.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="KokoroTtsService"/> class.
/// </remarks>
/// <param name="settingsService">Service for accessing application settings.</param>
/// <param name="logger">The logger instance for logging messages.</param>
public sealed partial class KokoroTtsService(
    ISettingsService settingsService,
    ILogger<KokoroTtsService> logger
    ) : IEngineSpecificTtsService
{
    private readonly ILogger<KokoroTtsService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly ISettingsService _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
    private KokoroEngine? _engine; // Use the lower-level engine
    private KokoroPlayback? _playback; // Manage playback instance directly
    private bool _isDisposed = false;
    private string? _selectedVoiceName; // Keep track of the selected voice

    // Default pipeline config for segmentation if none provided externally
    private static readonly KokoroTTSPipelineConfig s_defaultPipelineConfig = new();
    private static Dictionary<KModel, string> ModelNamesMap { get; } = new Dictionary<KModel, string>
    {
        {
            KModel.float32,
            "kokoro.onnx"
        },
        {
            KModel.float16,
            "kokoro-quant.onnx"
        },
        {
            KModel.int8,
            "kokoro-quant-convinteger.onnx"
        }
    };

    /// <summary>
    /// Gets the unique identifier for this engine ("Kokoro").
    /// </summary>
    public string EngineId => TtsSettings.KokoroEngine;

    /// <summary>
    /// Initializes the Kokoro TTS engine asynchronously.
    /// Loads the float16 model, creates playback instance, and loads voices.
    /// </summary>
    /// <returns>A task representing the asynchronous initialization operation.</returns>
    public async Task InitializeAsync() // Make async as LoadModelAsync needs await
    {
        if (_engine != null && _playback != null)
        {
            _logger.LogInformation("Kokoro TTS Engine and Playback already initialized.");
            return;
        }

        _logger.LogInformation("Initializing Kokoro TTS Engine and Playback...");
        try
        {
            // --- Load Model ---
            // Step 1: Ensure model file exists (download if necessary)
            string modelFileName = ModelNamesMap[KModel.float16];
            if (!KokoroTTS.IsDownloaded(KModel.float16))
            {
                _logger.LogInformation("Kokoro model '{ModelName}' not found. Attempting download...", modelFileName);
                // Use LoadModelAsync just for the download side-effect
                await KokoroTTS.LoadModelAsync(KModel.float16, progress =>
                {
                    // Optional: Log download progress if needed
                    // _logger.LogTrace("Model download progress: {ProgressPercent:P0}", progress);
                });
                if (!KokoroTTS.IsDownloaded(KModel.float16))
                {
                    throw new FileNotFoundException($"Failed to download or locate Kokoro model: {modelFileName}");
                }
                _logger.LogInformation("Kokoro model '{ModelName}' downloaded.", modelFileName);
            }

            // Step 2: Create KokoroEngine instance with the model path
            _logger.LogDebug("Creating KokoroEngine instance with model: {ModelName}", modelFileName);
            // TODO: Expose SessionOptions configuration possibility later if needed
            _engine = new KokoroEngine(modelFileName);
            _logger.LogInformation("KokoroEngine initialized successfully.");

            // Step 3: Create KokoroPlayback instance
            _logger.LogDebug("Creating KokoroPlayback instance...");
            _playback = new KokoroPlayback
            {
                NicifySamples = true // Enable sample nicification (trim silence etc.)
            };
            _logger.LogInformation("KokoroPlayback initialized.");

            // Step 4: Load Voices
            try
            {
                _logger.LogDebug("Loading Kokoro voices from default path...");
                KokoroVoiceManager.LoadVoicesFromPath();
                _logger.LogInformation("Kokoro voices loaded. Count: {VoiceCount}", KokoroVoiceManager.Voices.Count);
            }
            catch (DirectoryNotFoundException)
            {
                _logger.LogError("Kokoro voices directory not found at the default path ('voices'). Ensure voices are deployed correctly.");
            }
            catch (Exception voiceEx)
            {
                _logger.LogError(voiceEx, "Error loading Kokoro voices.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Failed to initialize Kokoro TTS Engine/Playback. Kokoro TTS will be unavailable.");
            _engine?.Dispose();
            _playback?.Dispose();
            _engine = null;
            _playback = null;
        }
    }

    /// <summary>
    /// Gets the names of the installed/loaded Kokoro voices.
    /// </summary>
    /// <returns>A task resulting in an enumerable collection of voice names.</returns>
    public Task<IEnumerable<string>> GetInstalledVoicesAsync()
    {
        // Check engine state as a proxy for successful initialization
        if (_engine == null)
        {
            _logger.LogWarning("Cannot get Kokoro voices: Engine not initialized.");
            return Task.FromResult(Enumerable.Empty<string>());
        }

        try
        {
            var voiceNames = KokoroVoiceManager.Voices.Select(v => v.Name).ToList();
            if (voiceNames.Count == 0)
            {
                _logger.LogWarning("GetInstalledVoicesAsync called, but KokoroVoiceManager.Voices is empty. Voices might not have loaded correctly.");
            }
            else
            {
                _logger.LogDebug("Returning {VoiceCount} installed Kokoro voices.", voiceNames.Count);
            }
            return Task.FromResult(voiceNames.AsEnumerable());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving Kokoro voices from KokoroVoiceManager.");
            return Task.FromResult(Enumerable.Empty<string>());
        }
    }

    /// <summary>
    /// Sets the voice to be used for subsequent speech synthesis.
    /// Stores the name for use in the SpeakAsync method.
    /// </summary>
    /// <param name="voiceName">The name of the Kokoro voice to select.</param>
    public void SetVoice(string voiceName)
    {
        if (_engine == null)
        {
            _logger.LogWarning("SetVoice skipped: Kokoro engine not available.");
            return;
        }
        if (string.IsNullOrWhiteSpace(voiceName))
        {
            _logger.LogWarning("SetVoice called with null or empty voice name.");
            return;
        }

        if (KokoroVoiceManager.Voices.Any(v => v.Name.Equals(voiceName, StringComparison.OrdinalIgnoreCase)))
        {
            _selectedVoiceName = voiceName;
            _logger.LogInformation("Kokoro voice '{VoiceName}' selected for next SpeakAsync call.", voiceName);
        }
        else
        {
            _logger.LogWarning("Kokoro voice '{VoiceName}' not found in loaded voices. Selection ignored.", voiceName);
            if (_selectedVoiceName != null && _selectedVoiceName.Equals(voiceName, StringComparison.OrdinalIgnoreCase))
            {
                _selectedVoiceName = null;
            }
        }
    }

    /// <summary>
    /// Placeholder for setting the rate. Speed is applied during SpeakAsync based on settings.
    /// </summary>
    /// <param name="rate">The rate value (-10 to 10).</param>
    public void SetRate(int rate)
    {
        if (_engine == null) return;
        _logger.LogTrace("SetRate called ({Rate}), but speed is applied during SpeakAsync for Kokoro.", rate);
    }

    /// <summary>
    /// Sets the playback volume by adjusting the managed KokoroPlayback instance.
    /// </summary>
    /// <param name="volume">The volume value (0-100).</param>
    public void SetVolume(int volume)
    {
        if (_playback == null)
        {
            _logger.LogWarning("SetVolume skipped: Kokoro playback instance not available.");
            return;
        }

        try
        {
            int clampedVolumeInt = Math.Clamp(volume, 0, 100);
            float floatVolume = clampedVolumeInt / 100.0f;

            _playback.SetVolume(floatVolume);
            _logger.LogDebug("Kokoro Playback Volume set to: {VolumePercent}% ({FloatVolume})", clampedVolumeInt, floatVolume);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting Kokoro playback volume to {Volume}.", volume);
        }
    }

    /// <summary>
    /// Speaks the provided text using the Kokoro TTS engine with the currently selected voice and rate/speed settings.
    /// Manages tokenization, segmentation, job creation, and queuing samples to the playback instance.
    /// </summary>
    /// <param name="textToSpeak">The text to synthesize.</param>
    /// <returns>A task representing the asynchronous speech operation (returns quickly as processing is backgrounded).</returns>
    public Task SpeakAsync(string textToSpeak)
    {
        if (_isDisposed)
        {
            _logger.LogWarning("SpeakAsync called after disposal.");
            return Task.CompletedTask;
        }
        if (_engine == null || _playback == null)
        {
            _logger.LogError("SpeakAsync failed: Kokoro engine or playback is not initialized.");
            return Task.CompletedTask;
        }
        if (string.IsNullOrWhiteSpace(textToSpeak))
        {
            _logger.LogTrace("SpeakAsync called with empty text, skipping.");
            return Task.CompletedTask;
        }
        if (string.IsNullOrWhiteSpace(_selectedVoiceName))
        {
            _logger.LogWarning("SpeakAsync called but no Kokoro voice selected. Attempting fallback...");
            _selectedVoiceName = KokoroVoiceManager.Voices.FirstOrDefault()?.Name;
            if (string.IsNullOrWhiteSpace(_selectedVoiceName))
            {
                _logger.LogError("SpeakAsync failed: No Kokoro voices available to select as default.");
                return Task.CompletedTask;
            }
            _logger.LogInformation("Using fallback voice: {FallbackVoice}", _selectedVoiceName);
        }

        KokoroVoice? selectedVoice = KokoroVoiceManager.GetVoice(_selectedVoiceName);
        if (selectedVoice == null)
        {
            _logger.LogError("SpeakAsync failed: Selected voice '{VoiceName}' not found in loaded voices.", _selectedVoiceName);
            return Task.CompletedTask;
        }

        // --- Calculate Speed ---
        int rate = _settingsService.CurrentSettings.TextToSpeech.Rate;
        float baseSpeed = 1.0f;
        float speedMultiplier = 0.05f;
        float calculatedSpeed = baseSpeed + (rate * speedMultiplier);
        float clampedSpeed = Math.Clamp(calculatedSpeed, 0.5f, 2.0f);
        // -----------------------

        _logger.LogInformation(
            "Queueing speech (Kokoro) - Voice: '{VoiceName}', Speed: {Speed}: \"{Text}\"",
             _selectedVoiceName, clampedSpeed, textToSpeak);

        try
        {
            // 1. Tokenize
            int[] tokens = Tokenizer.Tokenize(textToSpeak.Trim(), selectedVoice.GetLangCode());

            // 2. Segment (using default strategy for now)
            List<int[]> segments = s_defaultPipelineConfig.SegmentationFunc(tokens);
            _logger.LogDebug("Segmented text into {SegmentCount} parts.", segments.Count);

            // 3. Create Job
            // We handle playback manually, so the job's OnComplete callback is null initially.
            KokoroJob job = KokoroJob.Create(segments, selectedVoice, clampedSpeed, null);

            // 4. Define Step Completion Logic (Enqueue to our playback)
            foreach (KokoroJob.KokoroJobStep? step in job.Steps)
            {
                step.OnStepComplete = (samples) =>
                {
                    if (_playback != null && !_isDisposed)
                    {
                        _logger.LogTrace("Step completed, queueing {SampleCount} samples to playback.", samples.Length);
                        _playback.Enqueue(samples); // Enqueue samples to our managed playback instance

                        // Add pause if needed based on the *original* token ending punctuation
                        bool endsWithPunctuation = Tokenizer.PunctuationTokens.Contains(step.Tokens[^1]);
                        if (endsWithPunctuation && _playback.NicifySamples) // Only pause if nicifying (otherwise natural silence exists)
                        {
                            char endingChar = Tokenizer.TokenToChar[step.Tokens[^1]];
                            float pauseSeconds = s_defaultPipelineConfig.SecondsOfPauseBetweenProperSegments[endingChar];
                            if (pauseSeconds > 0)
                            {
                                int pauseSamples = (int)(pauseSeconds * KokoroPlayback.waveFormat.SampleRate);
                                _logger.LogTrace("Adding {PauseSeconds}s pause ({PauseSamples} samples) after segment ending with '{EndingChar}'.", pauseSeconds, pauseSamples, endingChar);
                                _playback.Enqueue(new float[pauseSamples]);
                            }
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Step completed, but playback instance is null or service disposed. Samples discarded.");
                    }
                };
            }

            // 5. Enqueue Job to Engine
            _engine.EnqueueJob(job);
            _logger.LogDebug("Kokoro job enqueued for processing.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during Kokoro SpeakAsync preparation/enqueue for voice '{VoiceName}'.", _selectedVoiceName);
        }

        // Return immediately, processing happens in the background.
        return Task.CompletedTask;
    }

    // --- IDisposable Implementation ---

    private void Dispose(bool disposing)
    {
        if (!_isDisposed)
        {
            if (disposing)
            {
                
                _logger.LogInformation("Disposing Kokoro TTS Engine Service...");
                // Dispose managed state (managed objects)
                _playback?.Dispose(); // Dispose playback first
                _engine?.Dispose();   // Then dispose the engine
                _playback = null;
                _engine = null;
                _logger.LogInformation("Kokoro TTS Engine Service disposed.");
            }
            _isDisposed = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
