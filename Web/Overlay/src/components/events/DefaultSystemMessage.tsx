// src/components/events/DefaultSystemMessage.tsx
import React from 'react';
import { SystemMessageEvent, OverlaySettings, SystemMessageLevel } from '../../types';

interface DefaultSystemMessageProps {
    event: SystemMessageEvent;
    settings: OverlaySettings | null;
}

const DefaultSystemMessage: React.FC<DefaultSystemMessageProps> = ({ event, settings }) => {
    if (!settings) return null;

    let levelClass = 'border-blue-500 bg-blue-500/10'; // Info default
    let iconGlyph = 'ℹ️';

    switch (event.level) {
        case SystemMessageLevel.Warning:
            levelClass = 'border-yellow-500 bg-yellow-500/10';
            iconGlyph = '⚠️';
            break;
        case SystemMessageLevel.Error:
            levelClass = 'border-red-600 bg-red-600/10';
            iconGlyph = '❌';
            break;
    }

     const containerStyle: React.CSSProperties = {
        color: settings.chat.textColor, // Use main text color
        fontFamily: settings.chat.font,
        fontSize: `${settings.chat.fontSize}px`,
    };

    return (
        // Tailwind: base classes, level classes, margin, padding, rounded, flex, border, italic, size
        <div
            className={`event-message event-system ${levelClass} mb-1 p-1.5 rounded flex items-center border-l-2 italic text-sm`}
            style={containerStyle}
        >
             {/* Timestamp (Optional) */}
            {settings.chat.timestampFormat && (
                <span className="timestamp text-xs opacity-80 mr-1.5 shrink-0 pt-px">
                    {new Date(event.timestamp).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
                </span>
            )}
            {/* Icon */}
            <span className="mr-1.5">{iconGlyph}</span>
            {/* Message */}
            <span className="message-content">{event.message}</span>
        </div>
    );
};

export default DefaultSystemMessage;