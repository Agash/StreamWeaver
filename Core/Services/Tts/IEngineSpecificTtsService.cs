namespace StreamWeaver.Core.Services.Tts;

/// <summary>
/// Defines the contract for a TTS engine-specific service, handling the direct interaction
/// with a particular speech synthesis engine (e.g., Windows SAPI, KokoroSharp).
/// </summary>
public interface IEngineSpecificTtsService : IDisposable
{
    /// <summary>
    /// Gets the unique identifier for the engine this service manages (e.g., "Windows", "Kokoro").
    /// Must match the constants defined in TtsSettings.
    /// </summary>
    string EngineId { get; }

    /// <summary>
    /// Initializes the TTS engine asynchronously.
    /// This might involve loading models, connecting to services, etc.
    /// </summary>
    /// <returns>A task representing the asynchronous initialization operation.</returns>
    Task InitializeAsync();

    /// <summary>
    /// Gets the list of available voices for this specific engine.
    /// </summary>
    /// <returns>A task resulting in an enumerable collection of voice names.</returns>
    Task<IEnumerable<string>> GetInstalledVoicesAsync();

    /// <summary>
    /// Sets the voice to be used by the engine.
    /// </summary>
    /// <param name="voiceName">The name of the voice to select.</param>
    void SetVoice(string voiceName);

    /// <summary>
    /// Sets the speaking rate of the engine.
    /// </summary>
    /// <param name="rate">The rate value (engine-specific range, typically -10 to 10).</param>
    void SetRate(int rate);

    /// <summary>
    /// Sets the volume of the engine.
    /// </summary>
    /// <param name="volume">The volume value (typically 0-100).</param>
    void SetVolume(int volume);

    /// <summary>
    /// Speaks the provided text using the engine's currently configured settings.
    /// This method should handle the actual synthesis and playback for this engine.
    /// Queuing logic will be handled by the CompositeTtsService.
    /// </summary>
    /// <param name="textToSpeak">The text to be synthesized and spoken.</param>
    /// <returns>A task representing the asynchronous speech operation.</returns>
    Task SpeakAsync(string textToSpeak);
}
