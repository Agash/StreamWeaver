import React from 'react';
import { BadgeInfo } from '../../types';

// --- Import SVG assets AS COMPONENTS ---
import YtOwnerIcon from '/src/assets/icons/youtube_owner.svg?react';
import YtModIcon from '/src/assets/icons/youtube_moderator.svg?react';
import YtVerifiedIcon from '/src/assets/icons/youtube_verified.svg?react';

// import TwitchBroadcasterIcon from '/src/assets/icons/twitch_broadcaster.svg?react';
// import TwitchModIcon from '/src/assets/icons/twitch_moderator.svg?react';
// import TwitchVipIcon from '/src/assets/icons/twitch_vip.svg?react';
// import TwitchPartnerIcon from '/src/assets/icons/twitch_partner.svg?react';


// --- Helper Functions ---

/** Set of known standard badge identifiers for quick lookup. */
export const standardBadgeIdentifiers = new Set([
    'youtube/owner/1',
    'youtube/moderator/1',
    'youtube/verified/1',
    'twitch/broadcaster/1',
    'twitch/moderator/1',
    'twitch/vip/1',
    'twitch/partner/1',
]);

/** Checks if a badge is considered a standard role badge based on its identifier. */
export const isStandardBadge = (badge: BadgeInfo): boolean => {
    return standardBadgeIdentifiers.has(badge.identifier);
};

/** Checks if a badge is considered a custom (non-standard role) badge. */
export const isCustomBadge = (badge: BadgeInfo): boolean => {
    return !standardBadgeIdentifiers.has(badge.identifier);
};

// Define specific override colors for standard badges
export const standardBadgeColorOverrides: Record<string, string> = {
    'youtube/owner/1': '#FFD700', // Gold/Yellow for YT Owner
    'youtube/verified/1': '#AAAAAA', // Gray for Verified
    'twitch/broadcaster/1': '#E91916', // Red for Broadcaster
    'twitch/vip/1': '#E005B9', // Pink for VIP
    'twitch/partner/1': '#9146FF', // Purple for Partner
    // Moderator color will come from username color, so no override needed here
};


/** Map standard identifiers to their imported SVG Component and alt text. */
export const standardBadgeComponentMap: Record<string, { Component: React.FC<React.SVGProps<SVGSVGElement>>; alt: string }> = {
    // YouTube
    'youtube/owner/1': { Component: YtOwnerIcon, alt: 'Owner' },
    'youtube/moderator/1': { Component: YtModIcon, alt: 'Moderator' },
    'youtube/verified/1': { Component: YtVerifiedIcon, alt: 'Verified' },

    // Twitch
    // 'twitch/broadcaster/1': { Component: TwitchBroadcasterIcon, alt: 'Broadcaster' },
    // 'twitch/moderator/1': { Component: TwitchModIcon, alt: 'Moderator' },
    // 'twitch/vip/1': { Component: TwitchVipIcon, alt: 'VIP' },
    // 'twitch/partner/1': { Component: TwitchPartnerIcon, alt: 'Partner' },
};