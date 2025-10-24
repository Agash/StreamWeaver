import { useEffect, useRef, useCallback } from 'react';
import { useStore } from './store';
import { webSocketService } from './webSocketService';
import ChatContainer from './components/ChatContainer';
import { DisplayedItem, WebSocketMessage, InitPayload, OverlaySettings, BaseEvent } from './types';

// Initialize global plugin registry (if not done elsewhere)
if (!window.StreamWeaverOverlay) {
     window.StreamWeaverOverlay = {
         plugins: {
             registry: {
                 componentOverrides: {},
             },
             registerComponentOverride: (key, component) => {
                 console.log(`[Plugin Registry] Registered override for component: ${key}`);
                 window.StreamWeaverOverlay.plugins.registry.componentOverrides[key] = component;
                 // TODO: Force re-render if needed? Zustand might handle this if components
                 // indirectly depend on the registry via rendered output.
             },
         },
     };
}

function App() {
    // Select state slices needed for logic in App.tsx
    const settings = useStore((state) => state.settings);
    const displayedItems = useStore((state) => state.displayedItems);
    const plugins = useStore((state) => state.plugins);
    const isConnected = useStore((state) => state.isConnected);
    const connectionStatus = useStore((state) => state.connectionStatus);

    // Select actions needed
    const initialize = useStore((state) => state.initialize);
    const setSettings = useStore((state) => state.setSettings);
    const addEvent = useStore((state) => state.addEvent);
    const removeItemByKey = useStore((state) => state.removeItemByKey);
    const setConnectionStatus = useStore((state) => state.setConnectionStatus);
    // eslint-disable-next-line @typescript-eslint/no-unused-vars
    const setPlugins = useStore((state) => state.setPlugins); // Added for completeness
    const resetDisplayedItems = useStore((state) => state.resetDisplayedItems);

    const timeoutRefs = useRef<Map<string, number>>(new Map()); // Map<eventKey, timeoutId>
    const loadedPluginAssets = useRef(new Set<string>()); // Track loaded scripts/styles

    // --- Timeout Management Callbacks ---
    const clearAndRemoveTimeout = useCallback((eventKey: string) => {
        const timeoutId = timeoutRefs.current.get(eventKey);
        if (timeoutId !== undefined) {
            window.clearTimeout(timeoutId);
            timeoutRefs.current.delete(eventKey);
            console.log(`[Timeout] Cleared and removed timeout reference for ${eventKey}`);
        }
    }, []);

    const clearAllTimeouts = useCallback(() => {
        console.log(`[Timeout] Clearing all ${timeoutRefs.current.size} active removal timeouts...`);
        timeoutRefs.current.forEach((timeoutId, key) => {
            window.clearTimeout(timeoutId);
            console.log(`[Timeout] Cleared timeout for ${key} (clearAll)`);
        });
        timeoutRefs.current.clear();
    }, []);

    const scheduleRemoval = useCallback((item: DisplayedItem, delayMs: number) => {
        clearAndRemoveTimeout(item.key); // Ensure no duplicate timeout exists for this key

        if (delayMs <= 0) {
            console.warn(`[Timeout] Attempted to schedule removal for ${item.key} with invalid delay: ${delayMs}ms. Skipping.`);
            return;
        }

        console.log(`[Timeout] Scheduling removal for ${item.key} in ${delayMs}ms`);
        const removalTimeoutId = window.setTimeout(() => {
            console.log(`[Timeout Fired] Requesting removal of item ${item.key} via timeout`);
            // *** Crucial: Call the store action to remove the item ***
            removeItemByKey(item.key);
            // Ref deletion happens *after* the state update causes the useEffect to run again
            // and call clearAndRemoveTimeout for the removed key.
            // We no longer delete the ref directly here.
        }, delayMs);

        timeoutRefs.current.set(item.key, removalTimeoutId);
    }, [clearAndRemoveTimeout, removeItemByKey]); // Depends on store action

    // --- WebSocket Connection and Message Handling Effect ---
    useEffect(() => {
        console.log('[App Mount] Setting up WebSocket subscriptions.');

        // Subscribe to connection status
        const statusSub = webSocketService.connectionStatus$.subscribe(status => {
            console.log(`[App] Received connection status: ${status}`);
            setConnectionStatus(status);
             // Optionally reset items when connecting or disconnecting
            if (status === 'connecting' || status === 'disconnected') {
                 // resetDisplayedItems(); // Uncomment if you want to clear chat on disconnect/reconnect start
                 // clearAllTimeouts(); // Clear timeouts if resetting items
            }
        });

        // Subscribe to messages
        const messageSub = webSocketService.message$.subscribe((message: WebSocketMessage) => {
            console.log('[App] Received message from service:', message.type);
            switch (message.type) {
                case 'init':
                    // Clear existing timeouts before initializing new state
                    clearAllTimeouts();
                    initialize(message.payload as InitPayload);
                    break;
                case 'settings':
                    // Clear timeouts BEFORE applying settings, as fade rules might change
                    // The timeout effect below will reschedule if needed based on new settings
                    clearAllTimeouts();
                    setSettings(message.payload as OverlaySettings);
                    break;
                case 'event':
                    addEvent(message.payload as BaseEvent);
                    // Scheduling timeout for the new event happens in the dedicated timeout effect below
                    break;
                default:
                    console.warn('[App] Received unknown message type from service:', message.type);
            }
        });

        // Initiate connection
        webSocketService.connect();

        // Cleanup on component unmount
        return () => {
            console.log("[App Unmount] Cleaning up WebSocket subscriptions and connection.");
            statusSub.unsubscribe();
            messageSub.unsubscribe();
            webSocketService.close();
            clearAllTimeouts(); // Final timeout cleanup
        };
        // Run only once on mount
        // Actions from useStore are stable, no need to list them unless using non-zustand state manager
    }, [initialize, setSettings, addEvent, setConnectionStatus, clearAllTimeouts, resetDisplayedItems]);

    // --- Timeout Management Effect ---
    useEffect(() => {
        if (!settings) {
            console.log('[Timeout Effect] No settings, skipping timeout logic.');
            return;
        }

        const { fadeMessages, fadeDelaySeconds } = settings.chat;
        const fadeDelayMs = Math.max(fadeDelaySeconds, 0.1) * 1000; // Ensure positive delay

        console.log(`[Timeout Effect] Running. Fade: ${fadeMessages}, Delay: ${fadeDelayMs}ms, Items: ${displayedItems.length}`);

        if (!fadeMessages) {
            // Fading is disabled, clear all existing timeouts
            console.log('[Timeout Effect] Fading disabled, clearing all timeouts.');
            clearAllTimeouts();
            return;
        }

        // Fading is enabled: Ensure all current items have timeouts, clear stale ones.
        const currentItemKeys = new Set(displayedItems.map(item => item.key));

        // 1. Clear timeouts for items that are no longer displayed
        timeoutRefs.current.forEach((_, key) => {
            if (!currentItemKeys.has(key)) {
                console.log(`[Timeout Effect] Clearing timeout for removed item: ${key}`);
                clearAndRemoveTimeout(key);
            }
        });

        // 2. Schedule timeouts for newly added items or ensure existing ones are correct
        displayedItems.forEach(item => {
            if (!timeoutRefs.current.has(item.key)) {
                // Item is displayed but has no timeout, schedule one
                console.log(`[Timeout Effect] Scheduling timeout for new/existing item: ${item.key}`);
                scheduleRemoval(item, fadeDelayMs);
            }
            // Note: If fadeDelayMs changes, the `clearAllTimeouts` in the 'settings' message handler
            // or the toggle-off logic above should have cleared old timeouts, and this loop
            // will reschedule everything with the new delay.
        });

    // Dependencies: Run when fade settings change or the list of displayed items changes.
    }, [settings, displayedItems, scheduleRemoval, clearAllTimeouts, clearAndRemoveTimeout]);


    // --- Dynamic Plugin Asset Loading Effect (remains the same) ---
    useEffect(() => {
        if (!plugins || plugins.length === 0) return;
        console.log("[Plugin Loader] Processing discovered plugins...", plugins.map(p => p.id));
        plugins.forEach((plugin) => {
            // Load Entry Script
            if (plugin.entryScript) {
                const scriptUrl = `${plugin.basePath}${plugin.entryScript}`;
                if (!loadedPluginAssets.current.has(scriptUrl)) {
                    console.log(`[Plugin Loader] Loading plugin script: ${scriptUrl}`);
                    const script = document.createElement('script');
                    script.src = scriptUrl;
                    script.type = 'module'; // Assuming modern plugins
                    script.async = true;
                    script.onerror = () => console.error(`Failed to load plugin script: ${scriptUrl}`);
                    script.onload = () => console.log(`Successfully loaded plugin script: ${scriptUrl}`);
                    document.body.appendChild(script);
                    loadedPluginAssets.current.add(scriptUrl);
                }
            }
             // Load Entry Style
            if (plugin.entryStyle) {
                const styleUrl = `${plugin.basePath}${plugin.entryStyle}`;
                 if (!loadedPluginAssets.current.has(styleUrl)) {
                    console.log(`[Plugin Loader] Loading plugin style: ${styleUrl}`);
                    const link = document.createElement('link');
                    link.rel = 'stylesheet';
                    link.href = styleUrl;
                    link.onerror = () => console.error(`Failed to load plugin style: ${styleUrl}`);
                    link.onload = () => console.log(`Successfully loaded plugin style: ${styleUrl}`);
                    document.head.appendChild(link);
                    loadedPluginAssets.current.add(styleUrl);
                }
            }
            // ... rest of plugin loading logic (web components, etc.) ...
             plugin.registersWebComponents?.forEach(wc => {
                if (wc.scriptPath) {
                    const wcScriptUrl = `${plugin.basePath}${wc.scriptPath}`;
                     if (!loadedPluginAssets.current.has(wcScriptUrl)) {
                         console.log(`[Plugin Loader] Loading web component script: ${wcScriptUrl} (for <${wc.tagName}>)`);
                        const script = document.createElement('script');
                        script.src = wcScriptUrl;
                        script.type = 'module';
                        script.async = true;
                        script.onerror = () => console.error(`Failed to load web component script: ${wcScriptUrl}`);
                        script.onload = () => console.log(`Successfully loaded web component script: ${wcScriptUrl}`);
                        document.body.appendChild(script);
                        loadedPluginAssets.current.add(wcScriptUrl);
                    }
                }
            });
        });
    }, [plugins]); // Depends only on the list of plugins

    // --- Render ---
    return (
        <div className="App w-full h-full border border-purple-500">
            {/* Status Indicators */}
            {connectionStatus === 'connecting' && !settings && (
                <div className="connection-status fixed top-2 left-2 p-2 bg-yellow-600 text-white rounded text-xs z-50">
                    Connecting...
                </div>
            )}
             {connectionStatus === 'connecting' && settings && (
                <div className="connection-status fixed top-2 left-2 p-2 bg-yellow-600 text-white rounded text-xs z-50">
                    Reconnecting...
                </div>
            )}
            {connectionStatus === 'error' && (
                <div className="connection-status fixed top-2 left-2 p-2 bg-red-700 text-white rounded text-xs z-50">
                    Connection Error!
                </div>
            )}
             {!isConnected && connectionStatus === 'disconnected' && settings && (
                 <div className="connection-status fixed top-2 left-2 p-2 bg-red-600 text-white rounded text-xs z-50">
                    Disconnected.
                 </div>
             )}
            {!settings && connectionStatus !== 'connecting' && connectionStatus !== 'error' && (
                <div className="connection-status fixed top-2 left-2 p-2 bg-gray-600 text-white rounded text-xs z-50">
                    Waiting for Init...
                </div>
            )}

            {/* Pass necessary state down (or let ChatContainer read from store) */}
            {/* ChatContainer now reads directly from the store */}
            <ChatContainer />
        </div>
    );
}

export default App;