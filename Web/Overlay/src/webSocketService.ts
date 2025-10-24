// src/webSocketService.ts
import { webSocket, WebSocketSubject, WebSocketSubjectConfig } from 'rxjs/webSocket';
import { retry, tap, catchError, map, shareReplay, Subject, BehaviorSubject, timer, EMPTY } from 'rxjs'; // Removed: delay, throwError
import { WebSocketMessage, ConnectionStatus } from './types'; // Removed: WebSocketPayload

const DEFAULT_WEBSOCKET_PORT = 5080;
const INITIAL_RECONNECT_DELAY = 1000; // 1 second
const MAX_RECONNECT_DELAY = 30000; // 30 seconds
const MAX_RETRY_ATTEMPTS = 100; // Or Infinity for endless retries

class WebSocketService {
    // Specify string as the expected raw message type
    private socket$?: WebSocketSubject<string>;
    private connectionStatusSubject = new BehaviorSubject<ConnectionStatus>('disconnected');
    private messageSubject = new Subject<WebSocketMessage>();
    private wsUrl: string = '';
    private explicitClose = false;

    public connectionStatus$ = this.connectionStatusSubject.asObservable().pipe(
        shareReplay(1) // Share the last emitted status
    );
    public message$ = this.messageSubject.asObservable(); // External consumers subscribe here

    private getWebSocketUrl(): string {
        const queryParams = new URLSearchParams(window.location.search);
        const wsPort = queryParams.get('port') || DEFAULT_WEBSOCKET_PORT;
        return `ws://localhost:${wsPort}/ws`;
    }

    connect() {
        this.wsUrl = this.getWebSocketUrl();
        console.log(`[WebSocketService] Initiating connection to ${this.wsUrl}`);
        this.explicitClose = false;
        this.connectionStatusSubject.next('connecting');
        this.connectAndSubscribe();
    }

    private connectAndSubscribe() {
        if (this.explicitClose || !this.wsUrl) {
            console.log("[WebSocketService] Connection attempt aborted (explicit close or no URL).");
            this.connectionStatusSubject.next('disconnected');
            return;
        }

        // Type the config for string messages
        const wsConfig: WebSocketSubjectConfig<string> = {
            url: this.wsUrl,
            openObserver: {
                next: () => {
                    console.log('[WebSocketService] Connection Opened.');
                    this.connectionStatusSubject.next('connected');
                }
            },
            closeObserver: {
                next: (closeEvent) => {
                    console.log(`[WebSocketService] Connection Closed (Code: ${closeEvent.code}, Reason: ${closeEvent.reason}, Explicit: ${this.explicitClose})`);
                    // Don't transition to 'disconnected' immediately if we are retrying
                    if (this.explicitClose || closeEvent.code === 1000) {
                        this.connectionStatusSubject.next('disconnected');
                    } else {
                        // Status remains 'connecting' or 'connected' while retrying is handled by retry logic
                        console.log("[WebSocketService] Non-explicit closure, retry logic will handle reconnect attempt.");
                    }
                }
            },
            // Expect raw string data, parsing happens in the pipeline
            deserializer: (e: MessageEvent): string => {
                if (typeof e.data === 'string') {
                    return e.data;
                }
                console.warn("[WebSocketService] Received non-string message data:", e.data);
                // Return empty string or handle error appropriately if non-string data is invalid
                return "";
            },
            // If you were SENDING messages, you'd use a serializer here:
            // serializer: (value: any) => JSON.stringify(value),
        };

        this.socket$ = webSocket(wsConfig);

        this.socket$.pipe(
            // Filter out empty strings that might come from the deserializer error handling
            map(data => data.trim()), // Trim whitespace just in case
            map(data => data ? this.parseAndValidateMessage(data) : null), // Parse non-empty strings
            tap(message => {
                if (message) { // Only emit valid, non-null messages
                    this.messageSubject.next(message);
                }
            }),
            retry({
                count: MAX_RETRY_ATTEMPTS,
                delay: (error, retryCount) => {
                    const delayMs = Math.min(INITIAL_RECONNECT_DELAY * Math.pow(2, retryCount - 1), MAX_RECONNECT_DELAY);
                    console.warn(`[WebSocketService] Connection error/closed unexpectedly. Retry ${retryCount}/${MAX_RETRY_ATTEMPTS} in ${delayMs}ms...`, error);
                    this.connectionStatusSubject.next('connecting'); // Signal reconnect attempt
                    return timer(delayMs);
                },
                resetOnSuccess: true // Reset retry count on successful reconnect
            }),
            catchError(error => {
                // This block is reached if retries are exhausted or if retry condition is false
                console.error('[WebSocketService] Unrecoverable WebSocket error or retries exhausted:', error);
                this.connectionStatusSubject.next('error');
                this.socket$ = undefined; // Ensure socket is cleaned up
                return EMPTY; // Stop the stream cleanly
            })
        ).subscribe({
            // We only care about errors/completion here, messages handled by tap
            error: (err) => {
                // Should ideally be handled by catchError, but good for safety
                console.error("[WebSocketService] Final subscription error handler:", err);
                if (this.connectionStatusSubject.value !== 'error' && this.connectionStatusSubject.value !== 'disconnected') {
                    this.connectionStatusSubject.next('error');
                }
            },
            complete: () => {
                 // This is called when the observable completes (e.g., via EMPTY in catchError or explicit close)
                console.log("[WebSocketService] WebSocket observable stream completed.");
                 if (this.connectionStatusSubject.value !== 'disconnected' && this.connectionStatusSubject.value !== 'error') {
                     // If not already in a terminal state, mark as disconnected
                     this.connectionStatusSubject.next('disconnected');
                 }
                 this.socket$ = undefined;
            }
        });
    }

    // Parameter 'data' is now guaranteed to be a non-empty string by the pipeline
    private parseAndValidateMessage(data: string): WebSocketMessage | null {
        try {
            const parsed: unknown = JSON.parse(data); // Parse as unknown first

            // Perform runtime validation
            if (
                parsed &&
                typeof parsed === 'object' &&
                'type' in parsed && typeof parsed.type === 'string' &&
                'payload' in parsed && parsed.payload !== undefined // Keep payload check basic
            ) {
                // console.log("[WebSocketService] Parsed message:", parsed.type); // Debug log
                // Cast to WebSocketMessage after successful validation
                return parsed as WebSocketMessage;
            } else {
                console.warn("[WebSocketService] Received invalid message format after parse:", parsed);
                return null;
            }
        } catch (e) {
            console.error("[WebSocketService] Failed to parse JSON message:", e, data);
            return null;
        }
    }


    close() {
        if (this.socket$) {
            console.log('[WebSocketService] Closing connection explicitly.');
            this.explicitClose = true;
            this.socket$.complete(); // Complete the observable stream
            this.socket$ = undefined;
            // Status will be updated via closeObserver or completion logic
        } else {
             console.log('[WebSocketService] Close called but no active connection.');
             // Ensure status is disconnected if called when already disconnected
             if (this.connectionStatusSubject.value !== 'disconnected') {
                 this.connectionStatusSubject.next('disconnected');
             }
        }
    }
}

// Export a singleton instance
export const webSocketService = new WebSocketService();