// src/components/events/DefaultFollowMessage.tsx
import React from 'react';
import { FollowEvent, OverlaySettings } from '../../types';

interface DefaultFollowMessageProps {
    event: FollowEvent;
    settings: OverlaySettings | null;
}

const DefaultFollowMessage: React.FC<DefaultFollowMessageProps> = ({ event, settings }) => {
    if (!settings) return null;

    const accentColor = '#00BCD4'; // Teal accent for follows
    const containerStyle: React.CSSProperties = {
        backgroundColor: accentColor + '1A', // ~10% Opacity
        color: settings.chat.textColor,
        borderColor: accentColor,
        fontFamily: settings.chat.font,
        fontSize: `${settings.chat.fontSize}px`,
    };

    return (
        // Tailwind: margin, padding, rounded, flex, border
        <div
            className={`event-message event-follow platform-${event.platform.toLowerCase()} mb-1 p-1.5 rounded flex items-center border-l-2`}
            style={containerStyle}
        >
            {/* Timestamp (Optional) */}
            {settings.chat.timestampFormat && (
                <span className="timestamp text-xs opacity-80 mr-1.5 shrink-0 pt-px">
                    {new Date(event.timestamp).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
                </span>
            )}
            {/* Platform Icon (Optional) */}
            {settings.chat.showPlatformIcons && (
                <span className={`platform-icon platform-icon-${event.platform.toLowerCase()} mr-1.5 text-sm shrink-0 font-semibold`}>
                    {event.platform === 'Twitch' ? 'T' : '?'} {/* Placeholder */}
                </span>
            )}
            <span className="username font-semibold mr-1">{event.username}</span>
            <span className="message-content">just followed!</span>
        </div>
    );
};

export default DefaultFollowMessage;