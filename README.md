# .NET Conf companion

[![Save the date! .NET Conf 2026, November 10-13](https://www.dotnetconf.net/img/conf-og-metadata.png)](https://www.dotnetconf.net/)

Your .NET Conf, in your pocket. An **unofficial companion app for iOS and Android**, built to help you discover talks, meet the speakers, and plan what you want to watch.

**Join us at [.NET Conf 2026](https://www.dotnetconf.net/), November 10-13!** Celebrate the launch of .NET 11, learn what's new, and connect with the community at this free virtual event. [Register now](https://aka.ms/dotnetconf2026) and save the date.

## Explore the conference

- **Find your next idea.** Browse sessions and search by title, topic, or speaker. Switch between local days with scrolling day tabs, or narrow the agenda by topic.
- **Meet the speakers.** Read their biographies and explore their talks, with easy navigation between speakers and sessions.
- **Find your community.** Discover ways to watch together, meet local presenters, or organize a gathering. No community events ship with the app, and an official event feed is not connected yet. The empty event list links to the official website; the map stays hidden without events.
- **Build your schedule.** Tap a heart to save a session. Your choices stay on your device, and overlapping sessions are highlighted.
- **See times that make sense to you.** Session times use your device's local time zone.
- **Feel at home.** Follow your device's appearance or choose Light or Dark in Settings. The app supports larger text.

The agenda opens immediately from the last downloaded update. No sessions or speaker profiles ship with the app: the first launch needs an internet connection to download them. Microsoft and community sessions refresh independently in the background without interrupting browsing. To check for updates yourself, pull down on Home, Sessions, Speakers, or the Schedule screen. A keyboard- and screen-reader-accessible alternative is available under Settings → About → Check for agenda updates. If an update fails, choose Retry. Downloaded data and your saved choices remain available offline.

## Availability

The app is **in development and not yet available in the App Store or Google Play**. We're aiming for a first release in November 2026.

**iOS and Android are the primary targets. Linux desktop and browser support is experimental**, with known feature gaps and less validation than the mobile apps. See the [platform limitations](CONTRIBUTING.md#linux-desktop-and-browser-experimental) before trying those builds.

**Conference sessions and profiles come only from remote public feeds:** the [Microsoft conference feed](https://dotnet.microsoft.com/blob-assets/json/conf/conference-2026.json) for days 1-2 and the [public Sessionize All feed](https://sessionize.com/api/v2/di6h1fo3/view/All) for days 3-4. Private organizer enrichment is not used. Successful downloads are cached for later offline use; a fresh offline install shows a download prompt, not a bundled agenda. Speaker photographs are downloaded and cached, never bundled or fetched from GitHub. Missing or unfinished biographies are shown honestly, and missing or unavailable portraits use the website's bundled .NET Bot image. Only presentation assets such as branding, fonts, icons, splash screens and the Bot placeholder remain local. Fictional community-event examples have also been removed.

## Your data

Saved sessions and your appearance preference are stored locally on your device. There is no account or bookmark upload/cloud sync. The app downloads public agenda data from Microsoft and Sessionize, and supplied public speaker portraits, into local caches. Unreachable portraits retain their downloaded cache or show the bundled .NET Bot placeholder; a first offline launch has no speaker photographs. No GitHub credentials are required or shipped. Links to the conference website open in your browser.

On upgrade, untraceable legacy agenda content, undownloaded snapshot seeds and old fictional community-event metadata are discarded before display. Validated downloads remain available offline; missing sources need a connection. Bookmarks and appearance are untouched, even while saved session details are unavailable. GitHub-hosted portraits, including ordinary GitHub avatars, are prohibited even when publicly reachable. Obsolete portrait-cache entries are pruned once downloaded agenda data is available. Biographies come only from the public feeds.

The browser target can refresh community sessions, but Microsoft's feed currently blocks cross-origin browser requests. It retains any previously downloaded Microsoft sessions and shows that update failure; a fresh browser install has only community sessions. Full browser refresh requires a same-origin relay or upstream CORS support.

The experimental browser app starts successfully and persists downloaded community sessions, portraits, and bookmarks across reloads. Offline reload was checked with the app host still reachable; this is not an offline-installable PWA. No relay or backend is included.

The map implementation uses Apple Maps on iOS and Google Maps on Android, with an internet connection for map tiles. It stays hidden until official event locations are available. The app does not request your location. The event list remains available without map tiles; Android builds without a configured Maps key, Linux and the browser use the list.

Read this version's [privacy policy](PRIVACY.md), also available offline under
Settings → About → Privacy policy. The separately hosted policy for older builds
does not describe this remote-data version.

Third-party dependencies, their licenses, and artwork credits are available
offline under Settings → About → Third-party dependencies.

## About the project

Built with [.NET MAUI](https://dotnet.microsoft.com/apps/maui) and .NET 11 RC2 technology, with styling inspired by the .NET Conf website. Experimental Linux desktop and browser builds are drawn by [Avalonia.Controls.Maui](https://github.com/AvaloniaUI/Avalonia.Controls.Maui). This is an unofficial project, not an official Microsoft conference app.

The rocket-bot artwork and website Bot placeholder are redistributed with the project owner's confirmed permission. That permission does not grant a general license to reuse Microsoft artwork or trademarks; bundled third-party notices include provenance and font licenses.

Want to help build the app? See [the contributor guide](CONTRIBUTING.md) for setup, development tools, and verification details.

For security vulnerabilities, use the [private reporting instructions](SECURITY.md) rather than a public issue.

Pull requests validate the Core tests and Android/iOS Release builds. Store uploads
are manual and target Google Play internal testing and TestFlight only; see
[CI and release setup](docs/CI.md) for the required accounts, signing secrets, and
manual promotion instructions.
