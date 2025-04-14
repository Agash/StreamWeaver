using CommunityToolkit.Mvvm.Messaging.Messages;
using StreamWeaver.Core.Models.Events;

namespace StreamWeaver.Core.Messaging;

// Wrapper message to send different event types over the main messenger
// Using ValueChangedMessage allows subscribers to potentially get the old value too,
// but for simple event broadcasting, a custom message inheriting from RequestMessage or just
// sending the BaseEvent directly might also work. ValueChangedMessage is common though.
public class NewEventMessage(BaseEvent eventData) : ValueChangedMessage<BaseEvent>(eventData) { }
