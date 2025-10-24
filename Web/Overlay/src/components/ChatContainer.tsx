import React from 'react';
import { AnimatePresence, motion } from 'framer-motion';
import { useStore } from '../store';
import { ChatMessageEvent, DonationEvent, MembershipEvent, SubscriptionEvent, FollowEvent, RaidEvent, SystemMessageEvent } from '../types';
import DefaultChatMessage from './events/DefaultChatMessage';
import DefaultDonationMessage from './events/DefaultDonationMessage';
import DefaultMembershipMessage from './events/DefaultMembershipMessage';
import DefaultFollowMessage from './events/DefaultFollowMessage';
import DefaultRaidMessage from './events/DefaultRaidMessage';
import DefaultSubscriptionMessage from './events/DefaultSubscriptionMessage';
import DefaultSystemMessage from './events/DefaultSystemMessage';


const ChatContainer: React.FC = () => {
    const displayedItems = useStore((state) => state.displayedItems);
    const settings = useStore((state) => state.settings);

    if (!settings) return null;

    const messageVariants = {
        initial: { opacity: 0, y: 20, scale: 0.95 },
        animate: { opacity: 1, y: 0, scale: 1, transition: { duration: 0.3, ease: "easeOut" } },
        exit: { opacity: 0, x: -30, scale: 0.9, transition: { duration: 0.2, ease: "easeIn" } }
    };

    return (
        <div
            id="chat-container"
            className="flex flex-col-reverse h-full overflow-hidden p-2 min-h-0"
            // Optional: Add border to debug its bounds
            // style={{ border: '2px solid limegreen', /* other styles */ }}
            style={{
                 fontFamily: settings.chat.font,
                 fontSize: `${settings.chat.fontSize}px`,
            }}
        >

            {/* MESSAGE LIST WRAPPER: MUST be the element directly after the spacer */}
            {/* This div contains the messages and sits at the bottom */}
            <div> {/* Optional: border: '1px solid cyan' */}
                <AnimatePresence initial={false}>
                    {displayedItems.map((item) => {
                        const eventData = item.event;
                        const key = item.key;
                        const eventTypeKey = eventData.$type;
                        const OverrideComponent = window.StreamWeaverOverlay?.plugins?.registry?.componentOverrides?.[eventTypeKey];

                        return (
                            <motion.div
                                key={key}
                                layout // Important for smooth positioning
                                variants={messageVariants}
                                initial="initial"
                                animate="animate"
                                exit="exit"
                                // Add margin-bottom for spacing between items
                                className="chat-item-wrapper w-full flex-shrink-0 mb-1"
                            >
                                {/* Render logic based on event type */}
                                {OverrideComponent ? (
                                    <OverrideComponent event={eventData} settings={settings} />
                                ) : (() => {
                                    switch (eventTypeKey) {
                                        case 'ChatMessageEvent': return <DefaultChatMessage event={eventData as ChatMessageEvent} settings={settings} />;
                                        case 'DonationEvent': return <DefaultDonationMessage event={eventData as DonationEvent} settings={settings} />;
                                        case 'MembershipEvent': return <DefaultMembershipMessage event={eventData as MembershipEvent} settings={settings} />;
                                        case 'SubscriptionEvent': return <DefaultSubscriptionMessage event={eventData as SubscriptionEvent} settings={settings} />;
                                        case 'FollowEvent': return <DefaultFollowMessage event={eventData as FollowEvent} settings={settings} />;
                                        case 'RaidEvent': return <DefaultRaidMessage event={eventData as RaidEvent} settings={settings} />;
                                        case 'SystemMessageEvent': return <DefaultSystemMessage event={eventData as SystemMessageEvent} settings={settings} />;
                                        default:
                                            console.warn(`[ChatContainer] No component for event type: ${eventTypeKey}, ID: ${key}`);
                                            return <div className="p-1 mb-1 bg-red-900 text-white rounded text-xs">Unhandled: {eventTypeKey}</div>;
                                    }
                                })()}
                            </motion.div>
                        );
                    })}
                </AnimatePresence>
            </div>
            
            {/* SPACER: MUST be direct child of flex-col container */}
            <div className="flex-grow border border-b-emerald-500"> {/* Optional: border: '1px dashed grey' */}</div>
        </div>
    );
};

export default ChatContainer;