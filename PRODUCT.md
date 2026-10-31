# .NET Conf companion

<!-- impeccable:product-schema 1 -->

## Platform

adaptive

## Users and purpose

Developers exploring the virtual .NET Conf, its sessions and speakers, and saving a personal viewing schedule on their phone.

## Capabilities and constraints

Native .NET MAUI on .NET 11, with iOS and Android as the primary targets and experimental Linux desktop and browser builds. The app is in development and has not been released. Core features are conference information, searchable sessions/speakers, timezone-aware times, local bookmarks, and a community overview with an event list. Native maps and event details are implemented but need an official event source before they can display gatherings. No accounts or cloud synchronization. Reminders, embedded partner video, and device-local AI are deferred.

## Brand commitments

An unofficial app, not an organizer product. Match the published dotnetconf.net identity while retaining native navigation, accessibility, and system-following appearance. App-owned icons use jfversluis/IconFont.Maui.FluentIcons. Bundled website artwork is redistributed with the project owner's confirmed permission; preserve its provenance and do not treat that permission as a general artwork or trademark license.

## Evidence on hand

The website provides event information and a purple/magenta visual identity. Conference sessions and speaker profiles come only from Microsoft's public conference JSON, which supplies the full agenda. The feed's day numbers determine the date range dynamically from explicit published start-date metadata; there is no fixed day-count limit. No agenda is packaged with the app. A fresh install needs a connection; cached launches immediately show validated downloaded data while checking for updates. Saved choices survive content refreshes, and withdrawn saved talks retain an explanation. Speaker photographs are downloaded and cached, never bundled or fetched from GitHub; unavailable photos use the local .NET Bot placeholder. Only useful public downloaded profiles can fill incomplete updates. Loading, download/validation failures and storage errors are visible. Community shows an honest empty list and an official-website link until an official event source is connected. The experimental browser cannot download directly from the Microsoft feed because of its upstream CORS policy; no relay is included.

## Principles

- Useful core functionality comes before preview showcases.
- Never present mock data as confirmed conference information.
- Preserve local schedule state across content refreshes.
- Optional capabilities must not prevent the core experience on unsupported devices.
- Development inspection is Debug-only and excluded from distribution.

## Accessibility

Respect font scaling, VoiceOver/TalkBack, native back navigation, safe areas, and light/dark contrast. Icon actions have spoken labels and gestures have visible alternatives.
