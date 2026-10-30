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

The website provides event information and a purple/magenta visual identity. Conference sessions and speaker profiles are remote-only: Microsoft's conference JSON now supplies days 1-4 and the public Sessionize All feed supplies days 3-4, without private organizer enrichment. Nothing seeds a conference agenda from packaged data. A fresh install needs a connection; subsequent launches immediately show previously downloaded data while independently checking both feeds. Speaker photographs are downloaded and cached for offline use, never bundled or fetched from GitHub; unavailable photos use the local .NET Bot placeholder. Only previously downloaded approved profiles can fill incomplete updates; explicit placeholders are omitted and no unpublished profiles are invented. Legacy enriched content and undownloaded snapshot seeds are removed before display without resetting bookmarks. Loading and first-download errors are explicit, cached refresh is quiet, and valid remote data remains browsable with a storage warning if offline persistence fails. Fictional community gatherings and venues are removed, including from legacy cache metadata. Until an official event source is connected, Community shows an honest empty list and an official-website link rather than invented gatherings or an empty map.

## Principles

- Useful core functionality comes before preview showcases.
- Never present mock data as confirmed conference information.
- Preserve local schedule state across content refreshes.
- Optional capabilities must not prevent the core experience on unsupported devices.
- Development inspection is Debug-only and excluded from distribution.

## Accessibility

Respect font scaling, VoiceOver/TalkBack, native back navigation, safe areas, and light/dark contrast. Icon actions have spoken labels and gestures have visible alternatives.
