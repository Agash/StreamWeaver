import React from 'react';
import { ChatMessageEvent, OverlaySettings } from '../../types';
import { renderSegments } from './renderHelper';
import { isStandardBadge, isCustomBadge, standardBadgeComponentMap, standardBadgeColorOverrides } from './badgeHelper';

// Import Platform SVG URLs AS COMPONENTS
import TwitchIcon from '/src/assets/icons/twitch.svg?react';
import YouTubeIcon from '/src/assets/icons/youtube.svg?react';

interface DefaultChatMessageProps {
    event: ChatMessageEvent;
    settings: OverlaySettings | null;
}

const DefaultChatMessage: React.FC<DefaultChatMessageProps> = ({ event, settings }) => {
    if (!settings) return null;

    const nameStyle: React.CSSProperties = {};
    let nameColor = settings.chat.textColor; // Default color
    if (settings.chat.usePlatformColors && event.usernameColor) {
        nameColor = event.usernameColor;
    }
    nameStyle.color = nameColor; // Apply final color

    const containerStyle: React.CSSProperties = {
        backgroundColor: settings.chat.backgroundColor,
        color: settings.chat.textColor,
        fontFamily: settings.chat.font,
        fontSize: `${settings.chat.fontSize}px`,
    };

    const highlightStyle: React.CSSProperties = event.isHighlight
        ? { backgroundColor: settings.chat.highlightColor + '26' }
        : {};
    const highlightBorderStyle: React.CSSProperties = event.isHighlight
        ? { borderColor: settings.chat.highlightColor }
        : {};

    // Platform Icon Component and Color
    const PlatformIconComponent = event.platform === 'Twitch' ? TwitchIcon :
                                 event.platform === 'YouTube' ? YouTubeIcon : undefined;
    const platformColor = event.platform === 'Twitch' ? '#9146FF' :
                         event.platform === 'YouTube' ? '#FF0000' :
                         settings.chat.textColor;

    // Filter badges
    const standardBadges = event.badges?.filter(isStandardBadge) ?? [];
    const customBadges = event.badges?.filter(isCustomBadge) ?? [];

    return (
        <div
            className="mb-1 p-1.5 rounded flex items-start relative"
            style={containerStyle}
        >
            {/* Highlight Overlay */}
            {event.isHighlight && (
                <>
                    <div className="absolute inset-0 rounded pointer-events-none" style={highlightStyle}></div>
                    <div className="absolute inset-0 border-2 rounded pointer-events-none" style={highlightBorderStyle}></div>
                </>
            )}

            {/* Timestamp */}
            {settings.chat.timestampFormat && (
                <span className="text-xs opacity-80 mr-1.5 shrink-0 pt-px">
                    {new Date(event.timestamp).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
                </span>
            )}

            {/* Platform Icon (Render SVG Component) */}
            {settings.chat.showPlatformIcons && PlatformIconComponent && (
                 <PlatformIconComponent
                    title={`${event.platform} Platform`}
                    // Tailwind: size relative to font, margin, prevent shrinking, alignment
                    className="h-[1em] w-auto mr-1.5 shrink-0 inline-block align-middle"
                    // Pass fill color directly
                    fill={platformColor}
                />
            )}

            {/* Username */}
            <span className="username font-semibold mr-1 shrink-0" style={nameStyle}>
                {event.username}
            </span>

            {/* ALL Badges (Standard + Custom) - After Username */}
            {settings.chat.showBadges && (standardBadges.length > 0 || customBadges.length > 0) && (
                <span className="badges ml-0.5 inline-flex items-center space-x-1 shrink-0">
                    {/* Standard Badges (Render SVG Component) */}
                    {standardBadges.map((badge) => {
                        const badgeMeta = standardBadgeComponentMap[badge.identifier];
                        // Determine color: Override > Username Color (for mod) > Default Text Color
                        const colorOverride = standardBadgeColorOverrides[badge.identifier];
                        const isModeratorBadge = badge.identifier.includes('/moderator/');
                        const badgeColor = colorOverride ?? (isModeratorBadge ? nameColor : settings.chat.textColor);

                        return badgeMeta ? (
                            <badgeMeta.Component
                                key={badge.identifier}
                                aria-label={badgeMeta.alt}
                                className="h-[1em] w-auto inline-block align-middle" // Size and alignment
                                fill={badgeColor} // Apply dynamic fill
                            />
                        ) : null; // Skip if not found in map
                    })}
                    {/* Custom Badges (Using IMG) */}
                    {customBadges.map((badge) => (
                        <img
                            key={badge.identifier}
                            src={badge.imageUrl ?? ''}
                            alt={badge.identifier.split('/').pop() ?? 'badge'}
                            className="h-[1em] w-auto inline-block align-middle"
                            title={badge.identifier}
                        />
                    ))}
                </span>
            )}

            {/* Separator */}
            {!event.isActionMessage && <span className="separator mr-1 shrink-0">:</span>}

            {/* Message Content */}
            <span
                className={`message-content break-words ${event.isActionMessage ? 'italic' : ''}`}
                style={event.isActionMessage ? nameStyle : {}}
            >
                {renderSegments(event.parsedMessage, settings)}
            </span>
        </div>
    );
};

export default DefaultChatMessage;