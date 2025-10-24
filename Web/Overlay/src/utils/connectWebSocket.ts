import { WebSocketMessage } from '../types';

interface WebSocketCallbacks {
    onOpen?: (event: Event) => void;
    onMessage?: (data: WebSocketMessage) => void;
    onClose?: (event: CloseEvent) => void;
    onError?: (error: Event) => void;
}

interface WebSocketConnection {
    close: () => void;
}

const initialReconnectDelay = 1000; // 1 second
const maxReconnectDelay = 30000; // 30 seconds

export function connectWebSocket(
    url: string,
    callbacks: WebSocketCallbacks
): WebSocketConnection {
    let socket: WebSocket | null = null;
    let reconnectTimeoutId: ReturnType<typeof setTimeout> | null = null;
    let currentReconnectDelay = initialReconnectDelay;
    let explicitlyClosed = false;

    const connect = () => {
        if (explicitlyClosed) {
            console.log("WebSocket: Connection explicitly closed, not reconnecting.");
            return;
        }

        console.log(`WebSocket: Attempting to connect to ${url}...`);
        socket = new WebSocket(url);

        socket.onopen = (event) => {
            console.log("WebSocket: Connected!");
            explicitlyClosed = false; // Reset flag on successful connection
            currentReconnectDelay = initialReconnectDelay; // Reset delay
            if (reconnectTimeoutId) clearTimeout(reconnectTimeoutId);
            reconnectTimeoutId = null;
            callbacks.onOpen?.(event);
        };

        socket.onmessage = (event) => {
            try {
                const data = JSON.parse(event.data) as WebSocketMessage;
                // Basic validation
                if (data && data.type && data.payload !== undefined) {
                     callbacks.onMessage?.(data);
                } else {
                    console.warn("WebSocket: Received invalid message format:", event.data);
                }
            } catch (e) {
                console.error("WebSocket: Failed to parse message:", e, event.data);
                callbacks.onError?.(new ErrorEvent("messageparseerror", { message: "Failed to parse message" }));
            }
        };

        socket.onclose = (event) => {
            console.log(`WebSocket: Closed. Code: ${event.code}, Reason: ${event.reason}, Clean: ${event.wasClean}`);
            socket = null; // Clear socket instance
            callbacks.onClose?.(event);

            if (!explicitlyClosed && event.code !== 1000) { // 1000 = Normal Closure
                console.log(`WebSocket: Attempting reconnect in ${currentReconnectDelay / 1000}s...`);
                if (reconnectTimeoutId) clearTimeout(reconnectTimeoutId); // Clear previous timer if any
                reconnectTimeoutId = setTimeout(() => {
                    connect(); // Attempt to reconnect
                    // Increase delay for next time (exponential backoff)
                    currentReconnectDelay = Math.min(currentReconnectDelay * 2, maxReconnectDelay);
                }, currentReconnectDelay);
            } else if (explicitlyClosed) {
                console.log("WebSocket: Connection closed explicitly.");
            }
        };

        socket.onerror = (error) => {
            console.error("WebSocket: Error:", error);
            callbacks.onError?.(error);
            // onclose will likely be called after error, triggering reconnect logic there
        };
    };

    const closeConnection = () => {
        console.log("WebSocket: Closing connection explicitly.");
        explicitlyClosed = true;
        if (reconnectTimeoutId) clearTimeout(reconnectTimeoutId);
        reconnectTimeoutId = null;
        socket?.close(1000, "Client closed connection"); // 1000 = Normal Closure
        socket = null;
    };

    // Initial connection attempt
    connect();

    return { close: closeConnection };
}