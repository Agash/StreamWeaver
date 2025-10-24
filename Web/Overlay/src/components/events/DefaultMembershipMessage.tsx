// src/components/events/DefaultMembershipMessage.tsx
import React from 'react';
import { MembershipEvent, OverlaySettings, MembershipEventType } from '../../types';
import { renderSegments } from './renderHelper';
import { isStandardBadge, isCustomBadge, standardBadgeComponentMap, standardBadgeColorOverrides } from './badgeHelper';

// Import Platform SVG URLs AS COMPONENTS
import YouTubeIcon from '/src/assets/icons/youtube.svg?react';

interface DefaultMembershipMessageProps {
    event: MembershipEvent;
    settings: OverlaySettings | null;
}

const DefaultMembershipMessage: React.FC<DefaultMembershipMessageProps> = ({ event, settings }) => {
    if (!settings) return null;

    const eventClass = 'event-membership';
    let headerText = event.headerText ?? '';
    const accentColor = event.platform === 'YouTube' ? '#0F9D58' : settings.chat.subColor;

    if (!headerText) {
        switch (event.membershipType) {
            case MembershipEventType.New: headerText = `Welcome member!`; break;
            case MembershipEventType.Milestone: headerText = `${event.milestoneMonths}-Month Milestone!`; break;
            case MembershipEventType.GiftPurchase: headerText = `purchased ${event.giftCount ?? '?'} gift memberships!`; break;
            case MembershipEventType.GiftRedemption: headerText = `received a gift membership!`; break;
        }
    }

    const nameStyle: React.CSSProperties = {};
    let nameColor = settings.chat.textColor; // Default color
    if (settings.chat.usePlatformColors && event.usernameColor) {
         nameColor = event.usernameColor;
    }
    nameStyle.color = nameColor;

    const containerStyle: React.CSSProperties = {
        backgroundColor: accentColor + '1A',
        color: settings.chat.textColor,
        borderColor: accentColor,
        fontFamily: settings.chat.font,
        fontSize: `${settings.chat.fontSize}px`,
    };

    // Filter badges
    const standardBadges = event.badges?.filter(isStandardBadge) ?? [];
    const customBadges = event.badges?.filter(isCustomBadge) ?? []; // Should contain the member badge

    return (
        <div
            className={`${eventClass} platform-${event.platform.toLowerCase()} mb-1 p-1.5 rounded flex items-start relative border-l-2`}
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
                 <YouTubeIcon title="YouTube Platform" className="h-[1em] w-auto mr-1.5 shrink-0 inline-block align-middle" fill="#FF0000" />
            )}

            {/* Main Content Area */}
            <div className="flex-grow">
                {/* Header Line */}
                <div className="membership-header flex items-center flex-wrap mb-0.5">
                    {/* Username */}
                    <span className="username font-semibold mr-1 shrink-0" style={nameStyle}>
                        {event.membershipType === MembershipEventType.GiftPurchase ? event.gifterUsername : event.username}
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
                            {/* Custom Badges (Using IMG - Member Badge) */}
                            {customBadges.map((badge) => (
                                <img key={badge.identifier} src={badge.imageUrl ?? ''} alt={badge.identifier.split('/').pop() ?? 'badge'} className="h-[1em] w-auto inline-block align-middle" title={badge.identifier}/>
                            ))}
                        </span>
                    )}

                    {/* Info Text */}
                    <span className="membership-info mr-1">{headerText}</span>
                    {/* Level (Conditional) */}
                    {event.membershipType !== MembershipEventType.GiftPurchase && event.levelName && (
                        <span className="membership-level text-xs opacity-90">({event.levelName})</span>
                    )}
                </div>

                {/* Milestone User Message (Conditional) */}
                {event.membershipType === MembershipEventType.Milestone && event.parsedMessage?.length > 0 && (
                    <div className="membership-body mt-1 pl-2 border-l-2 border-gray-500/50">
                        <span className="message-content break-words">
                            {renderSegments(event.parsedMessage, settings)}
                        </span>
                    </div>
                )}
            </div>
        </div>
    );
};

export default DefaultMembershipMessage;