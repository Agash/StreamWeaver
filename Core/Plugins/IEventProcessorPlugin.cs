using StreamWeaver.Core.Models.Events;

namespace StreamWeaver.Core.Plugins;

/// <summary>
/// Interface for plugins that need to process aggregated events from StreamWeaver.
/// </summary>
public interface IEventProcessorPlugin : IPlugin
{
    /// <summary>
    /// Called for every event aggregated by StreamWeaver's UnifiedEventService.
    /// Allows plugins to react to any type of event (chat, subs, follows, etc.).
    /// Implementations should handle events quickly to avoid blocking the event pipeline.
    /// </summary>
    /// <param name="eventData">The aggregated event data.</param>
    /// <returns>A task representing the asynchronous processing of the event.</returns>
    Task ProcessEventAsync(BaseEvent eventData);
}
