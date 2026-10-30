# .NET Conf companion

[![Save the date! .NET Conf 2026, November 10-13](https://www.dotnetconf.net/img/conf-og-metadata.png)](https://www.dotnetconf.net/)

Your .NET Conf, in your pocket. An **unofficial companion app for iOS and Android**, built to help you discover talks, meet the speakers, and plan what you want to watch.

**Join us at [.NET Conf 2026](https://www.dotnetconf.net/), November 10-13!** Celebrate the launch of .NET 11, learn what's new, and connect with the community at this free virtual event. [Register now](https://aka.ms/dotnetconf2026) and save the date.

## Explore the conference

- **Find your next idea.** Browse sessions and search by title, topic, or speaker. Switch between local days with scrolling day tabs, or narrow the agenda by topic.
- **Meet the speakers.** Read their biographies and explore their talks, with easy navigation between speakers and sessions.
- **Find your community.** Discover ways to watch together, meet local presenters, or organize a gathering. Open the full-screen event map or browse the event list, then see an event's location and website. Distinct pins and numbered clusters help you explore. The 24 community events and venues are currently fictional examples.
- **Build your schedule.** Tap a heart to save a session. Your choices stay on your device, and overlapping sessions are highlighted.
- **See times that make sense to you.** Session times use your device's local time zone.
- **Feel at home.** Follow your device's appearance or choose Light or Dark in Settings. The app supports larger text.

The bundled agenda is available offline, so you can browse it and manage your saved sessions without an account.

## Availability

The app is **in development and not yet available in the App Store or Google Play**. We're aiming for a first release in November 2026.

**iOS and Android are the primary targets. Linux desktop and browser support is experimental**, with known feature gaps and less validation than the mobile apps. See the [platform limitations](CONTRIBUTING.md#linux-desktop-and-browser-experimental) before trying those builds.

**The current sessions and speakers are fictional examples**, not the announced .NET Conf lineup. The app's agenda will be updated when the official schedule is available. For official announcements and event information, visit [dotnetconf.net](https://www.dotnetconf.net/).

## Your data

Saved sessions and your appearance preference are stored locally on your device. There is no account or cloud sync in the current app. Links to the conference website open in your browser.

Community maps use Apple Maps on iOS and Google Maps on Android, with an internet connection for map tiles. The app does not request your location. The event list remains available without map tiles; Android builds without a configured Maps key, Linux and the browser use the list.

Read the [privacy policy](https://jfversluis.dev/dotnetconf-privacy-policy.html),
also available under Settings → About.

Third-party dependencies, their licenses, and artwork credits are available
offline under Settings → About → Third-party dependencies.

## About the project

Built with [.NET MAUI](https://dotnet.microsoft.com/apps/maui) and .NET 11 RC2 technology, with styling inspired by the .NET Conf website. Experimental Linux desktop and browser builds are drawn by [Avalonia.Controls.Maui](https://github.com/AvaloniaUI/Avalonia.Controls.Maui). This is an unofficial project, not an official Microsoft conference app.

The rocket-bot artwork is used with permission. That permission does not grant a general license to reuse the artwork; bundled third-party notices include its provenance and font licenses.

Want to help build the app? See [the contributor guide](CONTRIBUTING.md) for setup, development tools, and verification details.

For security vulnerabilities, use the [private reporting instructions](SECURITY.md) rather than a public issue.

Pull requests validate the Core tests and Android/iOS Release builds. Store uploads
are manual and target Google Play internal testing and TestFlight only; see
[CI and release setup](docs/CI.md) for the required accounts, signing secrets, and
manual promotion instructions.
