document.addEventListener("DOMContentLoaded", () => {
    const timerDisplay = document.getElementById("timer-display");
    const container = document.getElementById("subtimer-container");
    const wsUrl = `ws://localhost:${window.WEBSOCKET_PORT || 5080}/ws`;

    let timerInterval = null;
    let remainingSeconds = 0;
    let isRunning = false;

    function updateDisplay() {
        if (remainingSeconds < 0) remainingSeconds = 0;
        const hours = Math.floor(remainingSeconds / 3600);
        const minutes = Math.floor((remainingSeconds % 3600) / 60);
        const seconds = remainingSeconds % 60;

        timerDisplay.textContent = `${hours.toString().padStart(2, "0")}:${minutes.toString().padStart(2, "0")}:${seconds
            .toString()
            .padStart(2, "0")}`;

        if (isRunning && remainingSeconds > 0) {
            container.classList.remove("stopped");
            container.classList.add("running");
        } else {
            container.classList.remove("running");
            container.classList.add("stopped");
        }
    }

    function startTimerTick() {
        if (timerInterval) clearInterval(timerInterval);
        if (!isRunning || remainingSeconds <= 0) {
            updateDisplay();
            return;
        }

        updateDisplay();

        timerInterval = setInterval(() => {
            remainingSeconds--;
            updateDisplay();
            if (remainingSeconds <= 0) {
                console.log("Subathon timer reached zero (client-side).");
                isRunning = false;
                clearInterval(timerInterval);
                timerInterval = null;
                updateDisplay();
            }
        }, 1000);
    }

    function handleTimerUpdate(data) {
        const typeIdentifier = data.eventType || (data.$type ? data.$type.split(",")[0].split(".").pop() : null);

        if (typeIdentifier === "SubTimerUpdateEvent") {
            console.log("Received SubTimer Update:", data);
            remainingSeconds = data.remainingSeconds;
            const serverIsRunning = data.isRunning;

            if (serverIsRunning !== isRunning || !timerInterval) {
                isRunning = serverIsRunning;
                if (isRunning) {
                    startTimerTick();
                } else {
                    if (timerInterval) clearInterval(timerInterval);
                    timerInterval = null;
                    updateDisplay();
                }
            } else {
                updateDisplay();
            }
        } else if (data.type === "settingsUpdate") {
            console.log("SubTimer received settings:", data.payload);
        }
    }

    connectWebSocket(
        wsUrl,
        () => console.log("SubTimer overlay connected."),
        (data) => handleTimerUpdate(data),
        (settings) => {},
        () => {
            console.log("SubTimer overlay disconnected.");
            if (timerInterval) clearInterval(timerInterval);
            isRunning = false;
            updateDisplay();
        },
        (error) => console.error("SubTimer overlay WebSocket error:", error)
    );

    updateDisplay();
});
