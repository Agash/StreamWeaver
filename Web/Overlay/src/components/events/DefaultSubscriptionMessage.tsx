// src/components/events/DefaultSubscriptionMessage.tsx
import React from 'react';
import { SubscriptionEvent, OverlaySettings } from '../../types';
import { renderSegments } from './renderHelper';
import { isStandardBadge, isCustomBadge, standardBadgeComponentMap, standardBadgeColorOverrides } from './badgeHelper';

import TwitchIcon from '/src/assets/icons/twitch.svg?react';

interface DefaultSubscriptionMessageProps {
    event: SubscriptionEvent;
    settings: OverlaySettings | null;
}

const DefaultSubscriptionMessage: React.FC<DefaultSubscriptionMessageProps> = ({ event, settings }) => {
    if (!settings) return null;

    let messageIntro = '';
    let messageDetails = '';
    const userMessage = event.message ?? '';

    if (event.isGift) {
        messageIntro = ` gifted a ${event.tier} sub`;
        if (event.recipientUsername) {
            messageIntro += ` to ${event.recipientUsername}`;
        }
        if (event.months > 1) {
            messageIntro += ` (${event.months} months)`;
        }
        messageIntro += '!';
        if (event.totalGiftCount > 0 && event.totalGiftCount !== event.giftCount) {
             messageDetails = ` (Total Gifts: ${event.totalGiftCount})`;
        }
    } else {
        messageIntro = ` subscribed with ${event.tier}`;
        if (event.cumulativeMonths > 1) {
            messageIntro += ` for ${event.cumulativeMonths} months!`;
        } else {
            messageIntro += '!';
        }
    }

    const nameStyle: React.CSSProperties = {};
    let nameColor = settings.chat.textColor; // Default color
    if (settings.chat.usePlatformColors && event.usernameColor) {
        nameColor = event.usernameColor;
    }
    nameStyle.color = nameColor;

    const accentColor = settings.chat.subColor;
    const containerStyle: React.CSSProperties = {
        backgroundColor: accentColor + '1A',
        color: settings.chat.textColor,
        borderColor: accentColor,
        fontFamily: settings.chat.font,
        fontSize: `${settings.chat.fontSize}px`,
    };

    // Filter badges
    const standardBadges = event.badges?.filter(isStandardBadge) ?? [];
    const customBadges = event.badges?.filter(isCustomBadge) ?? []; // Should contain sub/founder badges

    return (
        <div
            className={`event-message event-subscription platform-${event.platform.toLowerCase()} mb-1 p-1.5 rounded flex items-start relative border-l-2`}
            style={containerStyle}
        >
            {/* Timestamp */}
            {settings.chat.timestampFormat && (
                <span className="timestamp text-xs opacity-80 mr-1.5 shrink-0 pt-px">
                    {new Date(event.timestamp).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
                </span>
            )}

            {/* Platform Icon */}
            {settings.chat.showPlatformIcons && (
                 <TwitchIcon title="Twitch Platform" className="h-[1em] w-auto mr-1.5 shrink-0 inline-block align-middle" fill="#9146FF" />
            )}

            {/* Main Content Area */}
            <div className="flex-grow">
                 {/* Header Line */}
                 <div className="subscription-header flex items-center flex-wrap mb-0.5">
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
                                const colorOverride = standardBadgeColorOverrides[badge.identifier];
                                const isModeratorBadge = badge.identifier.includes('/moderator/');
                                const badgeColor = colorOverride ?? (isModeratorBadge ? nameColor : settings.chat.textColor);
                                return badgeMeta ? (
                                    <badgeMeta.Component key={badge.identifier} aria-label={badgeMeta.alt} className="h-[1em] w-auto inline-block align-middle" fill={badgeColor}/>
                                ) : null;
                            })}
                            {/* Custom Badges (Using IMG - Sub/Founder) */}
                            {customBadges.map((badge) => (
                                <img key={badge.identifier} src={badge.imageUrl ?? ''} alt={badge.identifier.split('/').pop() ?? 'badge'} className="h-[1em] w-auto inline-block align-middle" title={badge.identifier}/>
                            ))}
                        </span>
                    )}

                    {/* Info Text */}
                    <span className="subscription-info mr-1">{messageIntro}</span>
                    {/* Details Text */}
                    {messageDetails && <span className="subscription-details text-xs opacity-90">{messageDetails}</span>}
                </div>

                {/* User Message (Conditional) */}
                {userMessage && !event.isGift && (
                    <div className="subscription-body mt-1 pl-2 border-l-2 border-gray-500/50">
                        <span className="message-content italic break-words">
                             {renderSegments([{ text: `"${userMessage}"` }], settings)}
                        </span>
                    </div>
                )}
            </div>
        </div>
    );
};

export default DefaultSubscriptionMessage;