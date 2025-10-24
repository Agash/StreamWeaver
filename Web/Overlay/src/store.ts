import { create } from 'zustand';
import {
    OverlaySettings,
    WebPluginManifest,
    BaseEvent,
    DisplayedItem,
    ConnectionStatus,
    InitPayload,
} from './types';

interface AppState {
    settings: OverlaySettings | null;
    plugins: WebPluginManifest[];
    displayedItems: DisplayedItem[];
    isConnected: boolean; // Simplified from ConnectionStatus for easy boolean checks
    connectionStatus: ConnectionStatus;
}

interface AppActions {
    setSettings: (settings: OverlaySettings) => void;
    setPlugins: (plugins: WebPluginManifest[]) => void;
    addEvent: (event: BaseEvent) => void;
    removeItemByKey: (key: string) => void;
    setConnectionStatus: (status: ConnectionStatus) => void;
    initialize: (payload: InitPayload) => void;
    resetDisplayedItems: () => void;
}

export const useStore = create<AppState & AppActions>((set, get) => ({
    // Initial State
    settings: null,
    plugins: [],
    displayedItems: [],
    isConnected: false,
    connectionStatus: 'disconnected',

    // Actions
    setSettings: (newSettings) => {
        console.log('[Store] Updating settings:', Object.keys(newSettings || {}));
        set({ settings: newSettings });
        // Re-apply maxMessages limit if settings change affects it
        const currentItems = get().displayedItems;
        const maxMessages = newSettings?.chat?.maxMessages ?? 10; // Use default if settings are somehow null/invalid
        if (currentItems.length > maxMessages) {
            console.log(`[Store] Applying new maxMessages (${maxMessages}) due to settings update.`);
            set({ displayedItems: currentItems.slice(-maxMessages) });
        }
    },

    setPlugins: (plugins) => {
        console.log('[Store] Updating plugins:', plugins.map(p => p.id));
        set({ plugins });
    },

    addEvent: (event) => {
        const settings = get().settings;
        if (!settings) {
            console.warn(`[Store] Received event ${event.id} before settings initialized. Ignoring.`);
            return;
        }
        const { maxMessages } = settings.chat;
        const newItem: DisplayedItem = { event, key: event.id };

        console.log(`[Store] Adding event ${newItem.key}. Max: ${maxMessages}`);

        set((state) => {
            const itemsWithNew = [...state.displayedItems, newItem];
            const requiresSlice = itemsWithNew.length > maxMessages;
            const finalNextItems = requiresSlice
                ? itemsWithNew.slice(-maxMessages) // Keep the *last* maxMessages items
                : itemsWithNew;

            if (requiresSlice) {
                 const finalKeys = new Set(finalNextItems.map(item => item.key));
                 const removedItems = state.displayedItems.filter(item => !finalKeys.has(item.key));
                 if (removedItems.length > 0) {
                    console.log(`[Store] Sliced off ${removedItems.length} old items due to limit:`, removedItems.map(i => i.key).join(', '));
                 }
                 if (!finalKeys.has(newItem.key)) {
                     console.log(`[Store] New item ${newItem.key} was immediately sliced off.`);
                 }
            }
             console.log(`[Store] Updated displayedItems. Count: ${finalNextItems.length}`);
            return { displayedItems: finalNextItems };
        });
    },

    removeItemByKey: (key) => {
        console.log(`[Store] Removing item by key (likely timeout): ${key}`);
        set((state) => ({
            displayedItems: state.displayedItems.filter((item) => item.key !== key),
        }));
    },

    setConnectionStatus: (status) => {
        console.log(`[Store] Updating connection status: ${status}`);
        set({ connectionStatus: status, isConnected: status === 'connected' });
         // Clear items maybe only on explicit disconnect or error? Or on connecting?
         if (status === 'connecting' || status === 'disconnected' || status === 'error') {
            // Maybe reset items here if desired on disconnect/error states
            // set({ displayedItems: [] }); // Optional: Decide if state should clear
         }
    },

    initialize: (payload) => {
        console.log('[Store] Initializing state from payload.');
        set({
            settings: payload.settings,
            plugins: payload.plugins,
            displayedItems: [], // Start fresh
            isConnected: get().connectionStatus === 'connected', // Reflect current status potentially
        });
        // Apply initial max messages constraint if needed (though items start empty)
    },

    resetDisplayedItems: () => {
        console.log('[Store] Resetting displayed items.');
        set({ displayedItems: [] });
    },
}));