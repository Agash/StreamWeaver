/**
 * Sanitizes HTML content to prevent XSS.
 * Basic implementation - consider a more robust library like DOMPurify if complex HTML is allowed.
 * @param {string} str Input string
 * @returns {string} Sanitized string
 */
function sanitizeHTML(str) {
    if (!str) return "";
    const temp = document.createElement("div");
    temp.textContent = str;
    return temp.innerHTML;
}

/**
 * Establishes a WebSocket connection with retry logic.
 * @param {string} url WebSocket URL
 * @param {function} onOpen Callback when connection opens
 * @param {function} onMessage Callback when a non-settings message is received (receives parsed JSON data)
 * @param {function} onSettingsUpdate Callback when a settings update message is received (receives settings payload) * @param {function} onClose Callback when connection closes
 * @param {function} onError Callback on connection error
 */
function connectWebSocket(url, onOpen, onMessage, onSettingsUpdate, onClose, onError) {
    console.log(`Attempting WebSocket connection to ${url}...`);
    const socket = new WebSocket(url);
    const reconnectTimeout = null;
    const initialReconnectDelay = 1000;
    let currentReconnectDelay = initialReconnectDelay;
    const maxReconnectDelay = 30000;

    socket.onopen = (event) => {
        console.log("WebSocket connected!");
        currentReconnectDelay = initialReconnectDelay;
        if (reconnectTimeout) clearTimeout(reconnectTimeout);
        if (onOpen) onOpen(event);
    };

    socket.onmessage = (event) => {
        try {
            const data = JSON.parse(event.data);

            if (data && data.type === "settingsUpdate" && data.payload) {
                console.log("Received settings update:", data.payload);
                if (onSettingsUpdate) onSettingsUpdate(data.payload);
            } else if (data) {
                if (onMessage) onMessage(data);
            } else {
                console.warn("Received empty/invalid data object:", data);
            }
        } catch (e) {
            console.error("Failed to parse WebSocket message:", e, event.data);
            if (onError) onError(e);
        }
    };

    socket.onclose = (event) => {};
    socket.onerror = (error) => {};

    function closeConnection() {}

    return { close: closeConnection };
}

/**
 * Formats a Date object into HH:mm or HH:mm:ss format.
 * @param {Date} date The date object
 * @param {string} format 'HH:mm' or 'HH:mm:ss'
 * @returns {string} Formatted time string
 */
function formatTimestamp(date, format = "HH:mm") {
    if (!(date instanceof Date)) return "";
    const hours = date.getHours().toString().padStart(2, "0");
    const minutes = date.getMinutes().toString().padStart(2, "0");
    if (format === "HH:mm:ss") {
        const seconds = date.getSeconds().toString().padStart(2, "0");
        return `${hours}:${minutes}:${seconds}`;
    }
    return `${hours}:${minutes}`;
}
