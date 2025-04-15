### ⚠️⚠️⚠️ This is largly untested as of yet, as I don't stream myself. If anyone wants to help test this, please, hit me up! Below is what's currently implemented (at least has some code for it), not all confirmed to be in a *working* status so to say.

----

# StreamWeaver 🧵 woven chats, smooth streams! ✨

[![Build Status - TBD]()](#) [![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg?style=flat-square)](LICENSE.txt)

Tired of juggling multiple chat windows? Wish you could see Twitch, YouTube, and maybe even Streamlabs events all in one place, looking *exactly* like they should? Enter **StreamWeaver!** 🎉

StreamWeaver is your friendly, free, and open-source desktop sidekick designed to wrangle the chaos of multi-platform streaming into a single, unified, and *actually useful* view. No more alt-tabbing nightmares or missing important messages!

## What's the Big Deal? 🤔

*   **One Chat to Rule Them All:** Connect multiple *distinct* Twitch and YouTube accounts. That means, multiple YouTube and multiple Twitch Accounts. See all your chats combined, but styled accurately for each platform. 👑
*   **Know Your Platforms:** Messages look like they belong – Twitch subs look like Twitch subs, YouTube Super Chats look like Super Chats (colors and all!). 🎨
*   **Event Horizon (The Good Kind):** Catches not just chat, but also subs, follows, raids, memberships, Super Chats, donations (via Streamlabs), and more! 📢
*   **Talk Back!** Send messages from any of your connected accounts right from the app. 🗣️
*   **Moderation Power:** Timeout, Ban, and Delete messages directly on YouTube (using the official API!). Twitch moderation coming soon™. 🛡️
*   **Engage Your Audience:** Create and end YouTube Polls directly from the app! (Goals coming soon™). 📊
*   **OBS Overlay Included:** A built-in browser source overlay to show your unified chat on stream. Customizable, naturally. 📺
*   **Installer & Auto-Updates:** Easy installation and automatic updates via GitHub Releases (Planned). ✨
*   **Advanced TTS Options:** Includes standard Windows TTS and plans for high-quality KokoroSharp TTS voices (Planned). 🔊
*   **Plugin Power!** Extend StreamWeaver's capabilities with C# plugins (more features planned!). 🔌
*   **Built with Modern .NET:** Crafted using C# 13, .NET 9, and WinUI 3 for a native Windows experience. 💻
*   **Free & Open Source:** Use it, peek at the code, suggest changes, make it your own! It's all MIT licensed. 💖

## ⚠️ Important: API Credentials ⚠️

StreamWeaver requires **you** to provide your own API Client ID and Client Secret for both Twitch and YouTube (via their respective developer consoles). **This is mandatory and by design.**

**Why?**

1.  **Security & Control:** You authenticate directly with Twitch/Google. StreamWeaver never sees your password. You grant permissions *only* to your own developer application, giving you full control over access to your account. Using shared credentials would be a major security risk.
2.  **API Quotas:** Platforms like Twitch and Google impose usage limits (quotas) on API calls. By using your own credentials, your usage is tied to your application, preventing the entire StreamWeaver user base from being blocked if one shared key hits its limit. This ensures fair and reliable access for everyone.
3.  **Platform Terms of Service:** Using individual API keys and user authentication aligns with the Terms of Service for Twitch and Google APIs, which generally require applications to act on behalf of authenticated users via their own approved applications.
4.  **Transparency:** You know exactly what application is accessing your data because you created it.

You can create API credentials here:
*   **Twitch:** [https://dev.twitch.tv/console/apps](https://dev.twitch.tv/console/apps)
*   **Google/YouTube:** [https://console.cloud.google.com/apis/credentials](https://console.cloud.google.com/apis/credentials)

Please refer to the setup documentation (coming soon) or the settings page within StreamWeaver for guidance on configuring the Redirect URIs needed during credential setup (`http://localhost:5081/callback/twitch` and `http://localhost:5081/callback/google`).

## Current Status (As of April 2025) 🚧

*   **Twitch:** ✅ Chat Read/Send, ✅ Event Parsing (Subs, Raids, Follows, etc.). ⏳ Moderation actions planned.
*   **YouTube:** ✅ Chat Read (via unofficial API), ✅ Chat Send (Official API), ✅ Membership/Super Chat Events, ✅ Moderation Actions (Delete, Timeout, Ban - Official API), ✅ Poll Creation/Ending (Official API). ⏳ Goal features planned.
*   **Streamlabs:** ✅ Basic Connection (Socket API), ✅ Donation Events. ⏳ Parsing for other SL event types planned.
*   **Overlays:** ✅ Basic Chat Overlay functionality.⏳ Proper display pending ⏳ Enhancements & other overlay types planned.
*   **TTS:** ✅ Basic Windows TTS implementation. ⏳ KokoroSharp (fast, local, natural TTS) integration, Queued playback, Enhanced formatting planned.
*   **Installer:** ⏳ Installer with auto-updates planned.
*   **Plugins:** ✅ Basic C# plugin system functional. ⏳ Basic JavaScript plugin system planned.

## Tech Stack 🤓

*   **Core:** C# 13 / .NET 9
*   **UI:** WinUI 3 (Windows App SDK)
*   **Architecture:** MVVM (CommunityToolkit.Mvvm), Dependency Injection
*   **Platform Libs:** TwitchLib, Google.Apis.YouTube.v3, YTLiveChat (Unofficial Reader), SocketIOClient, **KokoroSharp (Planned)**
*   **Web Server:** ASP.NET Core Kestrel
*   **Installer:** Velopack

## ⚠️ Disclaimer: The YouTube Reading Part ⚠️

StreamWeaver uses the excellent [**YTLiveChat**](https://github.com/Agash/YTLiveChat) (_cough cough self promotion_) library to *read* YouTube chat messages. This library uses the same internal methods your web browser does, which means **it doesn't need an official YouTube Data API key and doesn't consume your API quota for reading chat**.

However, this is an **unofficial** method. YouTube *could* change their internal workings at any time, which might break chat reading until the library (or StreamWeaver) is updated. Use this feature with that understanding!

**Sending messages, moderating, and creating polls/goals on YouTube *does* use the official API** and requires you to provide your own API credentials and consent via OAuth.

## Contributing 🙏

Got ideas? Found a bug? Want to help build the ultimate streamer tool? Contributions are welcome! Check out the issues tab or feel free to submit a pull request.

---

Let's weave those streams together! Happy streaming! 🚀
