using CommunityToolkit.Mvvm.Messaging.Messages;

namespace StreamWeaver.Core.Messaging;

/// <summary>
/// A simple marker message indicating that platform connection states
/// may have changed and related UI elements (like Send Targets) should refresh.
/// </summary>
public class ConnectionsUpdatedMessage()
    : ValueChangedMessage<bool>(
        true
    ) // Pass true, meaning "something changed". Value isn't strictly needed.
{ }
