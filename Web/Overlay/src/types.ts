// Settings Interfaces (Mirror C# Models)
export interface ChatOverlaySettings {
    maxMessages: number;
    font: string; // Still useful for inline style fallback
    fontSize: number; // Still useful for inline style fallback/calculations
    textColor: string; // Used for inline style
    backgroundColor: string; // Used for inline style
    showBadges: boolean;
    showPlatformIcons: boolean;
    showEmotes: boolean;
    fadeMessages: boolean;
    fadeDelaySeconds: number;
    usePlatformColors: boolean;
    timestampFormat: string;
    highlightColor: string; // Used for inline style
    subColor: string; // Used for inline style
    donationColor: string; // Used for inline style
}

export interface OverlaySettings {
    webServerPort: number; // Might not be needed client-side
    chat: ChatOverlaySettings;
    // Add other overlay types (subtimer, goal) here later
}

// Plugin Manifest Interfaces (Mirror C# Models)
export interface WebComponentRegistration {
    tagName: string;
    scriptPath: string;
}

export interface WebPluginManifest {
    id: string;
    name: string;
    version: string;
    author: string;
    description?: string;
    entryScript: string;
    entryStyle?: string;
    providesComponents?: string[];
    registersWebComponents?: WebComponentRegistration[];
    basePath: string; // Provided by backend
}

// --- Event Interfaces (Mirror C# Models) ---

export interface BaseEvent {
    $type: string; // Contains values like "ChatMessageEvent", "DonationEvent", etc.
    
    id: string;
    timestamp: string; // Dates come as strings in JSON
    platform: string; // "Twitch", "YouTube", "Streamlabs", "System", "Module"
    originatingAccountId?: string | null;
}

export interface BadgeInfo {
    identifier: string; // e.g., "twitch/moderator/1", "youtube/member/1"
    imageUrl?: string | null;
}

// eslint-disable-next-line @typescript-eslint/no-empty-object-type
export interface MessageSegment {
    // Base type for segments
}

export interface TextSegment extends MessageSegment {
    text: string;
}

export interface EmoteSegment extends MessageSegment {
    name: string;
    imageUrl: string;
    id: string;
    platform: string;
}

export interface ChatMessageEvent extends BaseEvent {
    username: string;
    rawMessage?: string | null;
    parsedMessage: MessageSegment[];
    userId?: string | null;
    usernameColor?: string | null;
    badges: BadgeInfo[];
    profileImageUrl?: string | null;
    isOwner: boolean;
    isActionMessage: boolean;
    isHighlight: boolean;
    bitsDonated: number;
}

export enum DonationType {
    Streamlabs = 0,
    SuperChat = 1,
    Bits = 2,
    SuperSticker = 3,
    Other = 4,
}

export interface DonationEvent extends BaseEvent {
    username: string;
    userId?: string | null;
    usernameColor?: string | null;
    badges: BadgeInfo[];
    amount: number;
    currency: string; // e.g., "USD", "EUR", "Bits"
    rawMessage: string;
    parsedMessage: MessageSegment[];
    profileImageUrl?: string | null;
    isOwner: boolean;
    type: DonationType;
    donationId?: string | null;
    // YouTube Specific
    bodyBackgroundColor?: string | null;
    headerBackgroundColor?: string | null;
    headerTextColor?: string | null;
    bodyTextColor?: string | null;
    authorNameTextColor?: string | null;
    stickerImageUrl?: string | null;
    stickerAltText?: string | null;
    // Calculated property (might be useful to add/calculate client-side too)
    formattedAmount?: string; // e.g., "$10.00", "100 bits"
}

export enum MembershipEventType {
    Unknown = 0,
    New = 1,
    Milestone = 2,
    GiftPurchase = 3,
    GiftRedemption = 4,
}

export interface MembershipEvent extends BaseEvent {
    username: string; // Member (New/Milestone/Redemption) or Gifter (Purchase)
    userId?: string | null;
    usernameColor?: string | null;
    badges: BadgeInfo[];
    profileImageUrl?: string | null;
    isOwner: boolean;
    membershipType: MembershipEventType;
    levelName?: string | null;
    milestoneMonths?: number | null;
    gifterUsername?: string | null;
    giftCount?: number | null;
    headerText?: string | null; // System message part
    parsedMessage: MessageSegment[]; // User comment (usually for Milestone)
}

export interface SubscriptionEvent extends BaseEvent {
    username: string; // Subscriber or Gifter
    userId?: string | null;
    usernameColor?: string | null;
    badges: BadgeInfo[];
    profileImageUrl?: string | null;
    isOwner: boolean;
    isGift: boolean;
    recipientUsername?: string | null;
    recipientUserId?: string | null;
    months: number;
    cumulativeMonths: number;
    giftCount: number;
    totalGiftCount: number;
    tier: string; // e.g., "Tier 1", "Twitch Prime"
    message?: string | null; // User message (resub/gift)
}

export interface FollowEvent extends BaseEvent {
    username: string;
    userId?: string | null;
}

export interface RaidEvent extends BaseEvent {
    raiderUsername: string;
    raiderUserId?: string | null;
    viewerCount: number;
}

export interface HostEvent extends BaseEvent {
    isHosting: boolean; // True if WE started hosting, False if WE are being hosted/stopped
    hosterUsername?: string | null; // Channel hosting us
    hostedChannel?: string | null; // Channel we are hosting
    viewerCount: number;
    isAutoHost: boolean;
}

export enum SystemMessageLevel {
    Info = 0,
    Warning = 1,
    Error = 2,
}

export interface SystemMessageEvent extends BaseEvent {
    message: string;
    level: SystemMessageLevel;
}

export interface PollOption {
    text: string;
    votePercentage?: string | null;
    voteCount?: number | null; // ulong in C#, use number/bigint if needed
}

export interface YouTubePollUpdateEvent extends BaseEvent {
    pollId: string;
    question: string;
    options: PollOption[];
    isActive: boolean;
}

export interface BotMessageEvent extends BaseEvent {
    senderDisplayName: string;
    senderAccountId: string;
    message: string;
    target: string;
    parsedMessage: MessageSegment[];
}

export interface CommandInvocationEvent extends BaseEvent {
    originalCommandMessage: ChatMessageEvent;
    replyMessage?: string | null;
    botSenderDisplayName: string;
}

// --- WebSocket Message Types ---

export interface InitPayload {
    settings: OverlaySettings;
    plugins: WebPluginManifest[];
}

// Union of all possible payload types
export type WebSocketPayload =
    | InitPayload
    | OverlaySettings
    | ChatMessageEvent
    | DonationEvent
    | MembershipEvent
    | SubscriptionEvent
    | FollowEvent
    | RaidEvent
    | HostEvent
    | SystemMessageEvent
    | YouTubePollUpdateEvent
    | BotMessageEvent
    | CommandInvocationEvent;
// Add other specific events here as needed

export interface WebSocketMessage {
    type: 'init' | 'settings' | 'event';
    payload: WebSocketPayload;
}

// Define the structure for items managed in the displayed state
export interface DisplayedItem {
    event: BaseEvent;
    key: string; // Use event.id
}

// Add Connection Status Type
export type ConnectionStatus = 'connecting' | 'connected' | 'disconnected' | 'error';

// --- Global Window Structure for Plugins ---
// Define this so plugins have a clear API to register against
// Use a more specific type for component props if possible, but Record<string, unknown> is a safe default.
// It's better than `any` because it forces consumers to check types.
export type OverlayComponentProps = { event: BaseEvent; settings: OverlaySettings };
export type OverlayComponentType = React.ComponentType<OverlayComponentProps>;

declare global {
    interface Window {
        StreamWeaverOverlay: {
            plugins: {
                registry: {
                    componentOverrides: Record<string, OverlayComponentType>;
                    // Add other registry types if needed (e.g., event handlers)
                };
                registerComponentOverride: (
                    componentKey: string,
                    component: OverlayComponentType
                ) => void;
            };
        };
    }
}