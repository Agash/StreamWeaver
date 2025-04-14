document.addEventListener("DOMContentLoaded", () => {
    const chatContainer = document.getElementById("chat-container");
    if (!chatContainer) {
        console.error("FATAL: chat-container element not found!");
        return;
    }

    const queryParams = new URLSearchParams(window.location.search);
    const wsPort = queryParams.get("port") || window.WEBSOCKET_PORT || 5080;
    const wsUrl = `ws://localhost:${wsPort}/ws`;

    console.log(`Chat Overlay: Using WebSocket URL: ${wsUrl}`);

    const config = {
        maxMessages: 20,
        fadeMessages: true,
        fadeDelaySeconds: 30,
        timestampFormat: "HH:mm",
        showBadges: true,
        showPlatformIcons: true,
        usePlatformColors: true,
        font: "Segoe UI",
        fontSize: "14px",
        textColor: "#FFFFFF",
        bgColor: "rgba(0, 0, 0, 0.5)",
        timestampColor: "#AAAAAA",
        highlightBgColor: "rgba(255, 215, 0, 0.3)",
        highlightBorderColor: "#FFD700",
        subBgColor: "rgba(138, 43, 226, 0.3)",
        subBorderColor: "#8A2BE2",
        donationBgColor: "rgba(30, 144, 255, 0.3)",
        donationBorderColor: "#1E90FF",
    };

    function applySettings(settingsData) {
        if (!settingsData || !settingsData.chat) {
            console.warn("ApplySettings called with invalid data:", settingsData);
            return;
        }
        console.log("Applying overlay settings:", settingsData.chat);
        const chatSettings = settingsData.chat;

        config.maxMessages = chatSettings.maxMessages ?? config.maxMessages;
        config.fadeMessages = chatSettings.fadeMessages ?? config.fadeMessages;
        config.fadeDelaySeconds = chatSettings.fadeDelaySeconds ?? config.fadeDelaySeconds;
        config.timestampFormat = chatSettings.timestampFormat ?? config.timestampFormat;
        config.showBadges = chatSettings.showBadges ?? config.showBadges;
        config.showPlatformIcons = chatSettings.showPlatformIcons ?? config.showPlatformIcons;
        config.usePlatformColors = chatSettings.usePlatformColors ?? config.usePlatformColors;
        config.font = chatSettings.font ?? config.font;
        config.fontSize = `${chatSettings.fontSize ?? 14}px`;
        config.textColor = chatSettings.textColor ?? config.textColor;
        config.bgColor = chatSettings.backgroundColor ?? config.bgColor;
        config.timestampColor = chatSettings.timestampColor ?? config.timestampColor;
        config.highlightBgColor = chatSettings.highlightBgColor ?? config.highlightBgColor;
        config.highlightBorderColor = chatSettings.highlightColor ?? config.highlightBorderColor;
        config.subBgColor = chatSettings.subBgColor ?? config.subBgColor;
        config.subBorderColor = chatSettings.subColor ?? config.subBorderColor;
        config.donationBgColor = chatSettings.donationBgColor ?? config.donationBgColor;
        config.donationBorderColor = chatSettings.donationColor ?? config.donationBorderColor;

        const bodyStyle = document.body.style;
        bodyStyle.setProperty("--chat-font-family", config.font);
        bodyStyle.setProperty("--chat-font-size", config.fontSize);
        bodyStyle.setProperty("--chat-text-color", config.textColor);
        bodyStyle.setProperty("--chat-bg-color", config.bgColor);
        bodyStyle.setProperty("--chat-timestamp-color", config.timestampColor);
        bodyStyle.setProperty("--chat-timestamp-display", config.timestampFormat && config.timestampFormat.length > 0 ? "inline-block" : "none");
        bodyStyle.setProperty("--chat-platform-icon-display", config.showPlatformIcons ? "inline-block" : "none");
        bodyStyle.setProperty("--chat-badge-display", config.showBadges ? "inline-block" : "none");
        bodyStyle.setProperty("--chat-highlight-bg-color", config.highlightBgColor);
        bodyStyle.setProperty("--chat-highlight-border-color", config.highlightBorderColor);
        bodyStyle.setProperty("--chat-sub-bg-color", config.subBgColor);
        bodyStyle.setProperty("--chat-sub-border-color", config.subBorderColor);
        bodyStyle.setProperty("--chat-donation-bg-color", config.donationBgColor);
        bodyStyle.setProperty("--chat-donation-border-color", config.donationBorderColor);
        console.log("CSS variables applied.");
    }

    function addEventToChat(eventData) {
        if (!eventData || typeof eventData !== "object" || !eventData.platform || !eventData.id) {
            console.warn("Ignoring invalid event data:", eventData);
            return;
        }

        if (document.querySelector(`.chat-message[data-event-id="${eventData.id}"]`)) {
            return;
        }

        const messageElement = document.createElement("div");
        messageElement.classList.add("chat-message");
        messageElement.classList.add(`platform-${eventData.platform.toLowerCase()}`);
        if (eventData.originatingAccountId) {
            messageElement.dataset.originatingAccountId = eventData.originatingAccountId;
        }
        messageElement.dataset.eventId = eventData.id;

        const eventType = getEventType(eventData);
        if (eventType) messageElement.classList.add(`event-${eventType}`);

        let headerHTML = "";
        if (config.timestampFormat && config.timestampFormat.length > 0) {
            try {
                const timestamp = formatTimestamp(new Date(eventData.timestamp), config.timestampFormat);
                headerHTML += `<span class="timestamp">${timestamp}</span>`;
            } catch (e) {
                console.error("Error formatting timestamp", eventData.timestamp, e);
            }
        }
        if (config.showPlatformIcons) {
            headerHTML += `<span class="platform-icon ${eventData.platform.toLowerCase()}-icon" title="${eventData.platform}"></span>`;
        }

        let contentHTML = "";
        try {
            switch (eventType) {
                case "chat":
                    if (eventData.isHighlight) messageElement.classList.add("highlight");
                    if (eventData.isActionMessage) messageElement.classList.add("action");
                    contentHTML = buildChatMessageHTML(eventData, headerHTML);
                    break;
                case "donation":
                    messageElement.classList.add("highlight");
                    contentHTML = buildDonationMessageHTML(eventData, headerHTML);
                    break;
                case "subscription":
                    messageElement.classList.add("highlight");
                    contentHTML = buildSubscriptionMessageHTML(eventData, headerHTML);
                    break;
                case "membership":
                    messageElement.classList.add("highlight");
                    contentHTML = buildMembershipMessageHTML(eventData, headerHTML);
                    break;
                case "follow":
                    contentHTML = buildFollowMessageHTML(eventData, headerHTML);
                    break;
                case "raid":
                    messageElement.classList.add("highlight");
                    contentHTML = buildRaidMessageHTML(eventData, headerHTML);
                    break;
                default:
                    console.warn(`Unhandled event type: ${eventType}`, eventData);
                    contentHTML = headerHTML + `<span class="event-text">Received ${eventType || "unknown"} event.</span>`;
            }
        } catch (err) {
            console.error(`Error building content HTML for event type ${eventType}:`, err, eventData);
            contentHTML = headerHTML + '<span class="event-text error">[Error displaying event]</span>';
        }

        messageElement.innerHTML = contentHTML;

        chatContainer.insertBefore(messageElement, chatContainer.firstChild);

        manageMessageLimitAndFade(messageElement);
    }

    function manageMessageLimitAndFade(newMessageElement) {
        const messages = chatContainer.children;
        if (messages.length > config.maxMessages) {
            const oldestMessage = messages[messages.length - 1];
            if (config.fadeMessages && !oldestMessage.classList.contains("fade-out")) {
                startFadeOut(oldestMessage, 0);
            } else if (!config.fadeMessages) {
                chatContainer.removeChild(oldestMessage);
            }
        }
        if (config.fadeMessages) {
            startFadeOut(newMessageElement, config.fadeDelaySeconds * 1000);
        }
    }

    function startFadeOut(element, delay) {
        element.fadeTimer = setTimeout(() => {
            element.classList.add("fade-out");
            element.addEventListener(
                "animationend",
                () => {
                    if (element.parentNode === chatContainer) {
                        chatContainer.removeChild(element);
                    }
                },
                { once: true }
            );
        }, delay);
    }

    function getEventType(eventData) {
        if (eventData.eventType && typeof eventData.eventType === "string") {
            return eventData.eventType.toLowerCase();
        }

        if (eventData.type === "SuperChat" || eventData.type === "SuperSticker") return "donation";
        if (eventData.parsedMessage !== undefined && eventData.username !== undefined) return "chat";
        if (eventData.amount !== undefined && eventData.currency !== undefined) return "donation";
        if (eventData.tier !== undefined && eventData.months !== undefined) return "membership";
        if (eventData.cumulativeMonths !== undefined) return "subscription";
        if (eventData.raiderUsername !== undefined) return "raid";
        if (
            eventData.followerCount !== undefined ||
            (eventData.userId !== undefined && eventData.username !== undefined && eventData.message === undefined && eventData.amount === undefined)
        )
            return "follow";
        if (eventData.message !== undefined && eventData.level !== undefined) return "system";
        if (eventData.isHosting !== undefined) return "host";
        if (eventData.action !== undefined) return "moderation";
        if (eventData.botSenderDisplayName !== undefined) return "commandinvocation";
        if (eventData.senderDisplayName !== undefined && eventData.target !== undefined) return "botmessage";

        console.warn("Could not reliably determine event type:", eventData);
        return null;
    }

    function buildChatMessageHTML(data, headerHTML) {
        let badgesHTML = "";
        if (config.showBadges && data.badges && data.badges.length > 0) {
            badgesHTML = '<span class="badges">';
            data.badges.forEach((badgeId) => {
                const badgeUrl = getBadgeUrl(badgeId);
                if (badgeUrl) {
                    const altText = badgeId.split("/").slice(1).join(" ") || badgeId;
                    badgesHTML += `<img src="${badgeUrl}" class="chat-badge" alt="${sanitizeHTML(altText)}" title="${sanitizeHTML(badgeId)}">`;
                }
            });
            badgesHTML += "</span>";
        }

        const userColorStyle = config.usePlatformColors && data.userColor ? `style="color: ${sanitizeHTML(data.userColor)};"` : "";
        const usernameHTML = `<span class="username" ${userColorStyle}>${sanitizeHTML(data.username)}</span>`;
        const colon = data.isActionMessage ? "" : '<span class="message-colon">:</span>';

        let messageContentHTML = '<span class="message-content">';
        if (data.parsedMessage && Array.isArray(data.parsedMessage)) {
            data.parsedMessage.forEach((segment) => {
                if (segment.text !== undefined && segment.text !== null) {
                    messageContentHTML += sanitizeHTML(segment.text).replace(/\n/g, " ");
                } else if (segment.imageUrl !== undefined && segment.imageUrl !== null) {
                    const platformClass = segment.platform ? `${segment.platform.toLowerCase()}-emote` : "";
                    messageContentHTML += `<img src="${sanitizeHTML(segment.imageUrl)}" class="chat-emote ${platformClass}" alt="${sanitizeHTML(
                        segment.name
                    )}" title="${sanitizeHTML(segment.name)}">`;
                } else {
                    console.warn("Unknown message segment type:", segment);
                    messageContentHTML += "[?]";
                }
            });
        } else {
            messageContentHTML += "???";
        }
        messageContentHTML += "</span>";

        return `${headerHTML}${badgesHTML}${usernameHTML}${colon}${messageContentHTML}`;
    }

    function buildDonationMessageHTML(data, headerHTML) {
        let amountHTML = "";
        let bodyHTML = "";
        let cssClass = "donation";

        if (data.type === "SuperChat") {
            cssClass = "superchat";
            amountHTML = `<span class="${cssClass}-amount">${sanitizeHTML(data.formattedAmount)}</span>`;

            if (data.parsedMessage && Array.isArray(data.parsedMessage)) {
                bodyHTML += `<div class="${cssClass}-body">`;
                data.parsedMessage.forEach((segment) => {
                    if (segment.text !== undefined && segment.text !== null) {
                        bodyHTML += sanitizeHTML(segment.text).replace(/\n/g, " ");
                    }
                });
                bodyHTML += "</div>";
            }
        } else {
            amountHTML = `<span class="donation-amount">${sanitizeHTML(data.formattedAmount)}</span>`;
            if (data.parsedMessage && Array.isArray(data.parsedMessage)) {
                bodyHTML += '<span class="donation-message">';
                data.parsedMessage.forEach((segment) => {
                    if (segment.text !== undefined && segment.text !== null) {
                        bodyHTML += ` "${sanitizeHTML(segment.text).replace(/\n/g, " ")}"`;
                    }
                });
                bodyHTML += "</span>";
            }
        }

        const usernameHTML = `<span class="${cssClass}-username">${sanitizeHTML(data.username)}</span>`;

        return `<div class="${cssClass}-header">${headerHTML}${usernameHTML}${amountHTML}</div>${bodyHTML}`;
    }

    function buildMembershipMessageHTML(data, headerHTML) {
        const usernameHTML = `<span class="membership-username">${sanitizeHTML(data.username)}</span>`;
        const tierHTML = `<span class="membership-tier">${sanitizeHTML(data.tier)}</span>`;
        let statusText = "";
        if (data.months > 0) {
            statusText = ` (${data.months}-Month Milestone)`;
        } else {
            statusText = " became a new member!";
        }
        const statusHTML = `<span class="membership-status">${statusText}</span>`;

        let bodyHTML = "";
        if (data.months > 0 && data.parsedMessage && Array.isArray(data.parsedMessage)) {
            bodyHTML += '<div class="membership-body">';
            data.parsedMessage.forEach((segment) => {
                if (segment.text !== undefined && segment.text !== null) {
                    bodyHTML += sanitizeHTML(segment.text).replace(/\n/g, " ");
                }
            });
            bodyHTML += "</div>";
        }

        return `<div class="membership-header">${headerHTML}${usernameHTML}${statusHTML}</div>${bodyHTML}`;
    }

    function buildSubscriptionMessageHTML(data, headerHTML) {
        let messageText = "";
        if (data.isGift) {
            if (data.giftCount > 1) messageText = `${sanitizeHTML(data.username)} gifted ${data.giftCount} ${sanitizeHTML(data.tier)} subs!`;
            else messageText = `${sanitizeHTML(data.username)} gifted a ${sanitizeHTML(data.tier)} sub to ${sanitizeHTML(data.recipientUsername)}!`;
        } else {
            if (data.cumulativeMonths > 1)
                messageText = `${sanitizeHTML(data.username)} subscribed for ${data.cumulativeMonths} months (${sanitizeHTML(data.tier)})!`;
            else messageText = `${sanitizeHTML(data.username)} subscribed (${sanitizeHTML(data.tier)})!`;
            if (data.message) messageText += ` "${sanitizeHTML(data.message)}"`;
        }
        return `${headerHTML}<span class="event-text">${messageText}</span>`;
    }

    function buildFollowMessageHTML(data, headerHTML) {
        return `${headerHTML}<span class="event-text">${sanitizeHTML(data.username)} just followed!</span>`;
    }

    function buildRaidMessageHTML(data, headerHTML) {
        return `${headerHTML}<span class="event-text">${sanitizeHTML(
            data.raiderUsername
        )} is raiding with ${data.viewerCount.toLocaleString()} viewers!</span>`;
    }

    function getBadgeUrl(badgeIdentifier) {
        const badgeMap = {
            "twitch/moderator/1": "https://static-cdn.jtvnw.net/badges/v1/3267646d-33f0-4b17-b3df-f923a41db1d0/1",
            "twitch/subscriber/0": "https://static-cdn.jtvnw.net/badges/v1/5d9f2208-5dd8-11e7-8513-2ff4adfae661/1",
            "twitch/subscriber/1": "https://static-cdn.jtvnw.net/badges/v1/5d9f2208-5dd8-11e7-8513-2ff4adfae661/1",
            "twitch/subscriber/3": "https://static-cdn.jtvnw.net/badges/v1/0913b3e9-8758-4173-9000-16996b4e9ce6/1",
            "twitch/vip/1": "https://static-cdn.jtvnw.net/badges/v1/b817aba4-fad8-49e2-b88a-7cc744dfa6ec/1",
            "twitch/broadcaster/1": "https://static-cdn.jtvnw.net/badges/v1/5527c58c-fb7d-422d-b71b-f309dcb85cc1/1",
            "twitch/partner/1": "https://static-cdn.jtvnw.net/badges/v1/d12a2e27-16f6-41d0-ab77-b780518f00a3/1",
            "youtube/moderator":
                "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAA4AAAAOCAYAAAAfSC3RAAAABGdBTUEAALGPC/xhBQAAAAlwSFlzAAAOwQAADsEBuJFr7QAAABh0RVh0U29mdHdhcmUAcGFpbnQubmV0IDQuMS42/U4J6AAAAPNJREFUOE+lU7sNAkEMnC/sZYqegh4GBgYGDgYGDgYeQg8zgh4KBgaCHgpGgoHgIeAReAkeAj+OkzQLSwvTh96ZvN/udndsQ8hBADeY7aN0IAgIt2AEs6wPAcY4xhhTQiIK4XUQATAAwBDg4AChBQjwsAwwqArXh0/NJAdGEH4AApcEVRMyz/8xI0w0AcY9L/M++MMYkwdNCOg/jCFNEPb95QoZpUmks43szwZEwC0wQ7ABYQBKAFeAdQAWIBvAQgBDgEwAJwAPuAY8A54BPwDtAU/AN+AnwL/AP8BnwJ8AR8BH4C/AEfAZ8AX4ALwCfge/Ak8/AHC7prS34QOtAAAAAElFTkSuQmCC",
        };
        return badgeMap[badgeIdentifier];
    }

    connectWebSocket(
        wsUrl,
        () => {
            console.log("Chat overlay connected.");
        },
        (data) => {
            try {
                addEventToChat(data);
            } catch (e) {
                console.error("Error processing message event:", e, data);
            }
        },
        (settings) => {
            try {
                applySettings(settings);
            } catch (e) {
                console.error("Error applying settings:", e, settings);
            }
        },
        () => {
            console.log("Chat overlay disconnected.");
        },
        (error) => {
            console.error("Chat overlay WebSocket error:", error);
        }
    );
});
