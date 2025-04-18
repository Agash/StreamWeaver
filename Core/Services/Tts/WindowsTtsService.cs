using System.Speech.Synthesis;
using Microsoft.Extensions.Logging;
using StreamWeaver.Core.Models.Settings;

namespace StreamWeaver.Core.Services.Tts;

/// <summary>
/// Provides Text-to-Speech (TTS) functionality using the Windows built-in SpeechSynthesizer.
/// Implements the engine-specific interface for TTS operations.
/// </summary>
public sealed class WindowsTtsService(ILogger<WindowsTtsService> logger) : IEngineSpecificTtsService
{
    private readonly ILogger<WindowsTtsService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private SpeechSynthesizer? _synthesizer;
    private bool _isDisposed = false;
    private string? _selectedVoiceName; // Store the selected voice name

    public string EngineId => TtsSettings.WindowsEngine; // Use constant

    public Task InitializeAsync()
    {
        if (_synthesizer != null)
        {
            _logger.LogInformation("Windows TTS Engine already initialized.");
            return Task.CompletedTask;
        }

        try
        {
            _logger.LogInformation("Initializing Windows SpeechSynthesizer...");
            _synthesizer = new SpeechSynthesizer();
            _logger.LogInformation("Windows SpeechSynthesizer initialized.");
            // Apply default rate/volume, voice will be set via SetVoice
            _synthesizer.Volume = 80; // Default volume
            _synthesizer.Rate = 0; // Default rate
        }
        catch (PlatformNotSupportedException pnsEx)
        {
            _logger.LogCritical(pnsEx, "Failed to initialize SpeechSynthesizer: Platform Not Supported. System.Speech might require Desktop Runtime components. Windows TTS will be disabled.");
            _synthesizer = null;
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Failed to initialize SpeechSynthesizer. Windows TTS will be disabled.");
            _synthesizer = null;
        }
        return Task.CompletedTask; // Initialization is synchronous here
    }

    public Task<IEnumerable<string>> GetInstalledVoicesAsync()
    {
        if (_synthesizer == null)
        {
            _logger.LogWarning("Cannot get installed voices: Synthesizer not available.");
            return Task.FromResult(Enumerable.Empty<string>());
        }

        // Using Task.Run for potentially blocking OS call
        return Task.Run(() =>
        {
            try
            {
                _logger.LogDebug("Fetching installed and enabled Windows voices...");
                var voices = _synthesizer.GetInstalledVoices()
                                        .Where(v => v.Enabled)
                                        .Select(v => v.VoiceInfo.Name)
                                        .ToList();
                _logger.LogDebug("Found {VoiceCount} enabled Windows voices.", voices.Count);
                return voices.AsEnumerable();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting installed Windows voices.");
                return Enumerable.Empty<string>();
            }
        });
    }

    public void SetVoice(string voiceName)
    {
        if (_synthesizer == null)
        {
            _logger.LogWarning("SetVoice skipped: Synthesizer not available.");
            return;
        }
        if (string.IsNullOrWhiteSpace(voiceName))
        {
            _logger.LogWarning("SetVoice called with null or empty voice name.");
            return;
        }

        try
        {
            // Check if the voice needs changing
            if (_selectedVoiceName != null && _selectedVoiceName.Equals(voiceName, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogTrace("SetVoice skipped: Voice '{VoiceName}' is already selected.", voiceName);
                return;
            }

            _logger.LogDebug("Attempting to set Windows TTS voice to: {VoiceName}", voiceName);
            // Efficiently check if the voice exists and is enabled
            bool voiceExists = _synthesizer
                .GetInstalledVoices()
                .Any(v => v.Enabled && v.VoiceInfo.Name.Equals(voiceName, StringComparison.OrdinalIgnoreCase));

            if (voiceExists)
            {
                _synthesizer.SelectVoice(voiceName);
                _selectedVoiceName = voiceName; // Store the successfully set voice name
                _logger.LogInformation("Windows TTS Voice set to: {VoiceName}", voiceName);
            }
            else
            {
                _logger.LogWarning("Windows TTS Voice '{VoiceName}' not found or is not enabled.", voiceName);
                // Optionally fallback to default if the current selection becomes invalid
                if (_selectedVoiceName != null && _selectedVoiceName.Equals(voiceName, StringComparison.OrdinalIgnoreCase))
                {
                    _synthesizer.SelectVoice(null); // Select default
                    _selectedVoiceName = null;
                    _logger.LogInformation("Fell back to default system voice as '{VoiceName}' is unavailable.", voiceName);
                }
            }
        }
        catch (ArgumentException argEx) // Specific exception for SelectVoice if voice doesn't exist
        {
            _logger.LogWarning(argEx, "Failed to set Windows TTS voice to '{VoiceName}'. Voice likely not found.", voiceName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting Windows TTS voice to '{VoiceName}'.", voiceName);
        }
    }

    public void SetVolume(int volume)
    {
        if (_synthesizer == null) return;
        try
        {
            int clampedVolume = Math.Clamp(volume, 0, 100);
            if (_synthesizer.Volume != clampedVolume)
            {
                _synthesizer.Volume = clampedVolume;
                _logger.LogTrace("Windows TTS Volume set to: {Volume}", clampedVolume);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting Windows TTS volume to {Volume}.", volume);
        }
    }

    public void SetRate(int rate)
    {
        if (_synthesizer == null) return;
        try
        {
            int clampedRate = Math.Clamp(rate, -10, 10);
            if (_synthesizer.Rate != clampedRate)
            {
                _synthesizer.Rate = clampedRate;
                _logger.LogTrace("Windows TTS Rate set to: {Rate}", clampedRate);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting Windows TTS rate to {Rate}.", rate);
        }
    }

    public Task SpeakAsync(string textToSpeak)
    {
        // Basic validation done by Composite Service, focus on engine interaction here
        if (_synthesizer == null)
        {
            _logger.LogWarning("SpeakAsync called but synthesizer is not available.");
            return Task.CompletedTask; // Indicate immediate failure
        }

        if (string.IsNullOrWhiteSpace(textToSpeak))
        {
            _logger.LogTrace("SpeakAsync called with empty text, skipping.");
            return Task.CompletedTask;
        }

        // Task.Run is appropriate here as SpeakAsync might block briefly or perform COM interop.
        // The Composite Service will handle queuing if needed.
        return Task.Run(() =>
        {
            try
            {
                // Check state before potentially cancelling
                if (_synthesizer.State == SynthesizerState.Speaking)
                {
                    _logger.LogTrace("Cancelling previous Windows speech before speaking new text.");
                    _synthesizer.SpeakAsyncCancelAll();
                }
                _logger.LogInformation("Speaking (Windows): \"{TextToSpeak}\"", textToSpeak);
                _synthesizer.SpeakAsync(textToSpeak); // Fire-and-forget within the Task
            }
            catch (ObjectDisposedException)
            {
                _logger.LogWarning("SpeakAsync (Windows) failed: SpeechSynthesizer was disposed.");
            }
            catch (InvalidOperationException opEx)
            {
                _logger.LogError(opEx, "Invalid operation during SpeakAsync (Windows), synthesizer might be in an unexpected state.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during SpeakAsync (Windows) execution: {ErrorMessage}", ex.Message);
            }
        });
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (_isDisposed) return;
        if (disposing)
        {
            _logger.LogInformation("Disposing Windows TTS Engine Service...");
            if (_synthesizer != null)
            {
                try
                {
                    if (_synthesizer.State == SynthesizerState.Speaking)
                    {
                        _synthesizer.SpeakAsyncCancelAll();
                    }
                    _synthesizer.Dispose();
                    _logger.LogInformation("Windows SpeechSynthesizer disposed.");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Exception during Windows SpeechSynthesizer disposal.");
                }
                _synthesizer = null;
            }
            _logger.LogInformation("Windows TTS Engine Service disposed.");
        }
        _isDisposed = true;
    }
}
