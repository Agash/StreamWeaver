using StreamWeaver.Core.Models.Events;

namespace StreamWeaver.Core.Services.Tts;

public interface ITtsService
{
    Task SpeakAsync(string textToSpeak);

    Task<IEnumerable<string>> GetInstalledWindowsVoicesAsync();
    Task<IEnumerable<string>> GetInstalledKokoroVoicesAsync();
    void SetWindowsVoice(string voiceName);
    void SetKokoroVoice(string voiceName);

    void SetVolume(int volume); // 0-100
    void SetRate(int rate); // -10 to 10 (or adjust range per engine)
    void ProcessEventForTts(BaseEvent eventData);
}
