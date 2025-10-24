// src/components/events/DefaultDonationMessage.tsx
import React from 'react';
import { DonationEvent, OverlaySettings, DonationType } from '../../types';
import { renderSegments } from './renderHelper';
import { isStandardBadge, isCustomBadge, standardBadgeComponentMap, standardBadgeColorOverrides } from './badgeHelper';

interface DefaultDonationMessageProps {
    event: DonationEvent;
    settings: OverlaySettings | null;
}

const DefaultDonationMessage: React.FC<DefaultDonationMessageProps> = ({ event, settings }) => {
    if (!settings) return null;

    const headerStyle: React.CSSProperties = {};
    const bodyStyle: React.CSSProperties = {};
    const nameStyle: React.CSSProperties = {};
    const amountStyle: React.CSSProperties = {};
    const messageStyle: React.CSSProperties = {};

    let eventClass = 'event-donation';
    let accentColor = settings.chat.donationColor;
    let headerTextColor = settings.chat.textColor; // Default header text color

    if (event.type === DonationType.SuperChat || event.type === DonationType.SuperSticker) {
        eventClass += ' donation-superchat';
        if (event.headerBackgroundColor) headerStyle.backgroundColor = event.headerBackgroundColor;
        if (event.headerTextColor) {
            headerTextColor = event.headerTextColor; // Use specific header text color
            nameStyle.color = headerTextColor;
            amountStyle.color = headerTextColor;
        }
        if (event.bodyBackgroundColor) bodyStyle.backgroundColor = event.bodyBackgroundColor;
        if (event.bodyTextColor) messageStyle.color = event.bodyTextColor;
        accentColor = event.headerBackgroundColor ?? event.bodyBackgroundColor ?? accentColor;

    } else if (event.type === DonationType.Bits) {
        eventClass += ' donation-bits';
        accentColor = '#9146FF';
    } else {
        eventClass += ' donation-streamlabs';
    }

    // Apply default text color if not overridden
    if (!nameStyle.color) nameStyle.color = headerTextColor; // Use calculated header text color
    if (!amountStyle.color) amountStyle.color = headerTextColor; // Use calculated header text color
    if (!messageStyle.color) messageStyle.color = settings.chat.textColor;

    // Fallback backgrounds
    if (!headerStyle.backgroundColor) headerStyle.backgroundColor = accentColor + 'CC';
    if (!bodyStyle.backgroundColor) bodyStyle.backgroundColor = accentColor + '40';

    // Filter badges
    const standardBadges = event.badges?.filter(isStandardBadge) ?? [];
    const customBadges = event.badges?.filter(isCustomBadge) ?? [];

    return (
        <div
            className={`${eventClass} platform-${event.platform.toLowerCase()} mb-1 rounded overflow-hidden shadow-sm`}
            style={{ fontSize: `${settings.chat.fontSize}px`, fontFamily: settings.chat.font }}
        >
            {/* Header */}
            <div
                className="donation-header p-1.5 flex items-center space-x-1.5"
                style={headerStyle}
            >
                {/* Username */}
                <span className="username font-semibold grow shrink min-w-0 truncate" style={nameStyle}>
                    {event.username}
                </span>

                {/* ALL Badges (Standard + Custom) - After Username */}
                {settings.chat.showBadges && (standardBadges.length > 0 || customBadges.length > 0) && (
                    <span className="badges ml-0.5 inline-flex items-center space-x-1 shrink-0">
                        {/* Standard Badges (Render SVG Component) */}
                        {standardBadges.map((badge) => {
                            const badgeMeta = standardBadgeComponentMap[badge.identifier];
                            // Determine color: Override > Header Text Color (for mod) > Header Text Color (default)
                            const colorOverride = standardBadgeColorOverrides[badge.identifier];
                            const isModeratorBadge = badge.identifier.includes('/moderator/');
                            // Use headerTextColor as the base for badges within the donation header
                            const badgeColor = colorOverride ?? (isModeratorBadge ? headerTextColor : headerTextColor);

                            return badgeMeta ? (
                                <badgeMeta.Component
                                    key={badge.identifier}
                                    aria-label={badgeMeta.alt}
                                    className="h-[1em] w-auto inline-block align-middle"
                                    fill={badgeColor}
                                />
                            ) : null;
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

                {/* Amount */}
                <span className="donation-amount font-bold shrink-0" style={amountStyle}>
                    {event.formattedAmount ?? `${event.amount} ${event.currency}`}
                </span>
            </div>

            {/* Body (Message or Sticker) */}
            {(event.parsedMessage?.length > 0 || event.stickerImageUrl) && (
                <div className="donation-body p-1.5" style={bodyStyle}>
                    {event.stickerImageUrl && (
                        <img src={event.stickerImageUrl} alt={event.stickerAltText ?? 'Super Sticker'} className="max-h-[80px] w-auto inline-block my-1"/>
                    )}
                    {event.parsedMessage?.length > 0 && (
                        <span className="message-content block break-words" style={messageStyle}>
                            {renderSegments(event.parsedMessage, settings)}
                        </span>
                    )}
                </div>
            )}
        </div>
    );
};

export default DefaultDonationMessage;