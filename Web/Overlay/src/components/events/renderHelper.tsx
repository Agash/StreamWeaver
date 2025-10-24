import React from 'react';
import { MessageSegment, TextSegment, EmoteSegment, OverlaySettings } from '../../types';

// Renderer for message segments using Tailwind and dynamic height
export const renderSegments = (segments: MessageSegment[], settings: OverlaySettings | null): React.ReactNode => {
    if (!segments || segments.length === 0 || !settings) {
        return null;
    }

    // Calculate emote height based on font size for better scaling
    // Ensure fontSize is treated as a number
    const fontSize = Number(settings.chat.fontSize) || 14; // Default to 14 if invalid
    const emoteHeight = fontSize * 1.5; // Adjust multiplier as needed

    return segments.map((segment, index) => {
        // Check if segment is a TextSegment
        if ('text' in segment) {
            const textSegment = segment as TextSegment;
            // Render text directly. Replace multiple spaces with single space.
            return <React.Fragment key={index}>{textSegment.text.replace(/ {2,}/g, ' ')}</React.Fragment>;
        }
        // Check if segment is an EmoteSegment
        else if ('imageUrl' in segment && settings.chat.showEmotes) {
            const emoteSegment = segment as EmoteSegment;
            return (
                <img
                    key={index}
                    src={emoteSegment.imageUrl}
                    alt={emoteSegment.name}
                    // Tailwind: display, vertical alignment (centers with text), horizontal margin
                    className="inline-block align-middle mx-px"
                    title={emoteSegment.name} // Tooltip
                    // Use inline style for dynamic height based on font size setting
                    style={{ height: `${emoteHeight}px` }}
                />
            );
        }
        // Fallback for unknown segment types
        return null;
    });
};