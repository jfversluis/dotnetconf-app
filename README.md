# .NET Conf companion

[![Save the date! .NET Conf 2026, November 10-13](https://www.dotnetconf.net/img/conf-og-metadata.png)](https://www.dotnetconf.net/)

Your .NET Conf, in your pocket. An **unofficial companion app for iOS and Android**, built to help you discover talks, meet the speakers, and plan what you want to watch.

**Join us at [.NET Conf 2026](https://www.dotnetconf.net/), November 10-13!** Celebrate the launch of .NET 11, learn what's new, and connect with the community at this free virtual event. [Register now](https://aka.ms/dotnetconf2026) and save the date.

## Explore the conference

- **Find your next idea.** Browse sessions and search by title, topic, or speaker. Switch between local days with scrolling day tabs, or narrow the agenda by topic.
- **Meet the speakers.** Read their biographies and explore their talks, with easy navigation between speakers and sessions.
- **Find your community.** Discover ways to watch together, meet local presenters, or organize a gathering. No community events ship with the app, and an official event feed is not connected yet. The empty event list links to the official website; the map stays hidden without events.
- **Build your schedule.** Tap a heart to save a session. My Schedule opens on a timeline of every day: saved sessions stand out, overlaps are marked with how long they clash, topic filters fade what you don't need, and during the conference a live marker shows where you are. Pinch or use the zoom buttons to see more or less of the day, or switch to a plain list. Your choices stay on your device.
- **See times that make sense to you.** Session times use your device's local time zone.
- **Feel at home.** Follow your device's appearance or choose Light or Dark in Settings. The app supports larger text.

The agenda opens immediately from the last downloaded update. No sessions or speaker profiles ship with the app: the first launch needs an internet connection to download them. The Microsoft agenda refreshes in the background without interrupting browsing. To check for updates yourself, pull down on Home, Sessions, Speakers, or the Schedule screen. A keyboard- and screen-reader-accessible alternative is available under Settings → About → Check for agenda updates. If an update fails, choose Retry. Downloaded data and your saved choices remain available offline.

## Availability

The app is **in development and not yet available in the App Store or Google Play**. We're aiming for a first release in November 2026.

**iOS and Android are the primary targets. Linux desktop and browser support is experimental**, with known feature gaps and less validation than the mobile apps. See the [platform limitations](CONTRIBUTING.md#linux-desktop-and-browser-experimental) before trying those builds.

**Conference sessions and profiles come only from the public [Microsoft conference feed](https://dotnet.microsoft.com/blob-assets/json/conf/conference-2026.json).** It supplies the full agenda. Day numbers and times come from the feed; the published November 10 start date is explicit conference metadata because the feed has no calendar dates. There is no fixed day-count limit, and day tabs follow the downloaded sessions in your local timezone. Private organizer enrichment is not used. Successful downloads are cached for offline use; a fresh offline install shows a download prompt, not a bundled agenda. Speaker photographs are downloaded and cached, never bundled or fetched from GitHub. Missing or unfinished biographies are shown honestly, and missing or unavailable portraits use the website's bundled .NET Bot image. Only presentation assets such as branding, fonts, icons, splash screens and the Bot placeholder are bundled.

## Your data

Saved sessions and your appearance preference are stored locally on your device. There is no account or bookmark upload/cloud sync. The app downloads public agenda data from Microsoft and approved speaker portraits into local caches. Validated downloads remain available offline, and failed updates do not replace them. Saved choices remain local even when session details are unavailable or a session is withdrawn from the feed. Unreachable portraits retain their downloaded cache or show the bundled .NET Bot placeholder; a first offline launch has no speaker photographs. GitHub-hosted portraits are not used, and no GitHub credentials are required. Links to the conference website open in your browser.

Microsoft's feed blocks cross-origin browser requests. A fresh browser startup cannot download the agenda directly and shows an honest empty/download error state. A validated local cache remains readable when updates fail. Browser downloads require upstream CORS support or a separately provided same-origin relay.

The experimental browser app persists its local cache and bookmarks across reloads. This is not an offline-installable PWA. No relay or backend is included.

The map implementation uses Apple Maps on iOS and Google Maps on Android, with an internet connection for map tiles. It stays hidden until official event locations are available. The app does not request your location. The event list remains available without map tiles; Android builds without a configured Maps key, Linux and the browser use the list.

Read the [privacy policy](PRIVACY.md), also available offline under
Settings → About → Privacy policy.

Third-party dependencies, their licenses, and artwork credits are available
offline under Settings → About → Third-party dependencies.

## About the project

Built with [.NET MAUI](https://dotnet.microsoft.com/apps/maui) and .NET 11 RC2 technology, with styling inspired by the .NET Conf website. Experimental Linux desktop and browser builds are drawn by [Avalonia.Controls.Maui](https://github.com/AvaloniaUI/Avalonia.Controls.Maui). The My Schedule timeline is an Avalonia control on every platform, embedded in the native app on iOS and Android. This is an unofficial project, not an official Microsoft conference app.

The rocket-bot artwork and website Bot placeholder are redistributed with the project owner's confirmed permission. That permission does not grant a general license to reuse Microsoft artwork or trademarks; bundled third-party notices include provenance and font licenses.

Want to help build the app? See [the contributor guide](CONTRIBUTING.md) for setup, development tools, and verification details.

For security vulnerabilities, use the [private reporting instructions](SECURITY.md) rather than a public issue.

Pull requests validate the Core tests and Android/iOS Release builds. Store uploads
are manual and target Google Play internal testing and TestFlight only; see
[CI and release setup](docs/CI.md) for the required accounts, signing secrets, and
manual promotion instructions.
