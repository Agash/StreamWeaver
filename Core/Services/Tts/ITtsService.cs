using StreamWeaver.Core.Models.Events;

namespace StreamWeaver.Core.Services.Tts;

public interface ITtsService
{
    Task SpeakAsync(string textToSpeak);
    Task<IEnumerable<string>> GetInstalledVoicesAsync();
    void SetVoice(string voiceName);
    void SetVolume(int volume); // 0-100
    void SetRate(int rate); // -10 to 10
    void ProcessEventForTts(BaseEvent eventData);
}
