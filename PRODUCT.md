# .NET Conf companion

<!-- impeccable:product-schema 1 -->

## Platform

adaptive

## Users and purpose

Developers exploring the virtual .NET Conf, its sessions and speakers, and saving a personal viewing schedule on their phone.

## Capabilities and constraints

Native .NET MAUI on .NET 11, shipping to iOS and Android first. Core features are conference information, searchable sessions/speakers, timezone-aware times, local bookmarks, and a community overview leading to full-screen native maps, an equivalent event list, and event details. No accounts or cloud synchronization. Desktop, reminders, embedded partner video, and device-local AI are later work.

## Brand commitments

An unofficial app, not an organizer product. Match the published dotnetconf.net identity while retaining native navigation, accessibility, and system-following appearance. App-owned icons use jfversluis/IconFont.Maui.FluentIcons. The project owner confirmed redistribution permission for the website's two-robot rocket on October 7, 2026; permission for other official artwork must be established separately.

## Evidence on hand

The website provides event information and a purple/magenta visual identity. Conference sessions and speaker profiles come only from Microsoft's public conference JSON, now the complete agenda. Sessionize is no longer fetched as a second agenda. The feed's day numbers determine the date range dynamically from the explicit published start-date metadata; there is no fixed day-count limit. Nothing seeds an agenda from packaged data. A fresh install needs a connection; subsequent launches immediately show validated downloaded data while checking for updates. Legacy Sessionize bookmarks resolve through conservative cached-data aliases. Unmatched previously active talks remain an offline fallback until a successful authoritative refresh, then become archives rather than duplicate agenda entries. Speaker photographs are downloaded and cached, never bundled or fetched from GitHub; unavailable photos use the local .NET Bot placeholder. Only useful public downloaded profiles can fill incomplete updates. Legacy enriched content and undownloaded seeds are removed without resetting bookmarks. Loading, download/validation failures and storage errors are visible. Fictional community gatherings are removed; Community shows an honest empty list and an official-website link until an official event source is connected.

## Principles

- Useful core functionality comes before preview showcases.
- Never present mock data as confirmed conference information.
- Preserve local schedule state across content refreshes.
- Optional capabilities must not prevent the core experience on unsupported devices.
- Development inspection is Debug-only and excluded from distribution.

## Accessibility

Respect font scaling, VoiceOver/TalkBack, native back navigation, safe areas, and light/dark contrast. Icon actions have spoken labels and gestures have visible alternatives.
