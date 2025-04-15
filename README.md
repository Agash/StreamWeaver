# StreamWeaver 🧵 woven chats, smooth streams! ✨

[![Build Status - TBD]()](#) [![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg?style=flat-square)](LICENSE.txt)

Tired of juggling multiple chat windows? Wish you could see Twitch, YouTube, and maybe even Streamlabs events all in one place, looking *exactly* like they should? Enter **StreamWeaver!** 🎉

StreamWeaver is your friendly, free, and open-source desktop sidekick designed to wrangle the chaos of multi-platform streaming into a single, unified, and *actually useful* view. No more alt-tabbing nightmares or missing important messages!

## What's the Big Deal? 🤔

*   **One Chat to Rule Them All:** Connect multiple *distinct* Twitch and YouTube accounts. See all your chats combined, but styled accurately for each platform. 👑
*   **Know Your Platforms:** Messages look like they belong – Twitch subs look like Twitch subs, YouTube Super Chats look like Super Chats (colors and all!). 🎨
*   **Event Horizon (The Good Kind):** Catches not just chat, but also subs, follows, raids, memberships, Super Chats, donations (via Streamlabs), and more! 📢
*   **Talk Back!** Send messages from any of your connected accounts right from the app. 🗣️
*   **Moderation Power:** Timeout, Ban, and Delete messages directly on YouTube (using the official API!). Twitch moderation coming soon™. 🛡️ (*YouTube Pinning currently unavailable due to API limits*).
*   **Engage Your Audience:** Create YouTube Polls directly from the app! (Goals coming soon™). 📊
*   **OBS Overlay Included:** A built-in browser source overlay to show your unified chat on stream. Customizable, naturally. 📺
*   **Plugin Power!** Extend StreamWeaver's capabilities with C# plugins (more features planned!). 🔌
*   **Built with Modern .NET:** Crafted using C# 13, .NET 9, and WinUI 3 for a native Windows experience. 💻
*   **Free & Open Source:** Use it, peek at the code, suggest changes, make it your own! It's all MIT licensed. 💖

## Current Status (As of April 2025 - Adjusted) 🚧

*   **Twitch:** ✅ Chat Read/Send, ✅ Event Parsing (Subs, Raids, Follows, etc.). ⏳ Moderation actions planned.
*   **YouTube:** ✅ Chat Read (via unofficial API), ✅ Chat Send (Official API), ✅ Membership/Super Chat Events, ✅ Moderation Actions (Delete, Timeout, Ban - Official API), ✅ Poll Creation/Ending (Official API). ⏳ Goal features planned. ⏳ Refine Poll Display planned.
*   **Streamlabs:** ✅ Basic Connection (Socket API), ✅ Donation Events. ⏳ Parsing for other SL event types planned.
*   **Overlays:** ✅ Basic Chat Overlay. ⏳ Enhancements & other overlay types planned.
*   **Plugins:** ✅ Basic C# plugin system functional.
*   **TTS:** ✅ Windows TTS implemented. ⏳ Replacing with Piper TTS planned.
*   **Installer:** ⏳ Installer creation planned (Non-MSIX).

## ⚠️ Important: API Credentials & Security ⚠️

StreamWeaver requires you to provide your **own API credentials** (Client ID and Client Secret) for both Twitch and YouTube official API interactions (sending messages, moderation, polls, etc.).

**Why Your Own Credentials?**

1.  **Security:** Using your own credentials ensures that authentication happens directly between you and the platform (Twitch/Google). StreamWeaver only stores the *tokens* it receives after you authenticate, and it stores them securely using the Windows Credential Manager. It **never** sees your platform password.
2.  **API Quotas:** Platforms like YouTube and Twitch have API usage limits (quotas). If StreamWeaver used a single, shared set of credentials, the application could quickly hit these limits for *all* users. By using your own credentials, your usage is tied to your specific application setup, giving you control and transparency.
3.  **Terms of Service:** Platforms often require applications using their APIs to have their own identifiable credentials. This helps them track API usage and enforce their terms. Sharing developer credentials is generally discouraged or prohibited.

You can generate these credentials for free from the [Twitch Developer Console](https://dev.twitch.tv/console/apps) and the [Google Cloud Console](https://console.cloud.google.com/apis/credentials). StreamWeaver provides links and guidance within the application's settings.

## ⚠️ Disclaimer: The YouTube Reading Part ⚠️

StreamWeaver uses the excellent [**YTLiveChat**](https://github.com/Agash/YTLiveChat) (_cough cough self promotion_) library to *read* YouTube chat messages. This library uses the same internal methods your web browser does, which means **it doesn't need an official YouTube Data API key and doesn't consume your API quota for reading chat**.

However, this is an **unofficial** method. YouTube *could* change their internal workings at any time, which might break chat reading until the library (or StreamWeaver) is updated. Use this feature with that understanding!

**Sending messages, moderating, and creating polls on YouTube *does* use the official API** and requires you to provide your own API credentials and consent via OAuth.

## Tech Stack 🤓

*   **Core:** C# 13 / .NET 9
*   **UI:** WinUI 3 (Windows App SDK)
*   **Architecture:** MVVM (CommunityToolkit.Mvvm), Dependency Injection
*   **Platform Libs:** TwitchLib, Google.Apis.YouTube.v3, YTLiveChat (Unofficial Reader), SocketIOClient
*   **Web Server:** ASP.NET Core Kestrel
*   **TTS (Current):** System.Speech
*   **TTS (Planned):** Piper TTS / ONNX Runtime

## Contributing 🙏

Got ideas? Found a bug? Want to help build the ultimate streamer tool? Contributions are welcome! Check out the issues tab or feel free to submit a pull request.

---

Let's weave those streams together! Happy streaming! 🚀