# .NET Conf companion

Source code for a [.NET Conf](https://dot.net/conf) companion app for iOS and Android, built with [.NET MAUI](https://dotnet.microsoft.com/apps/maui). Browse sessions and speakers, save talks, see schedule conflicts, and set reminders. Times use your local time zone.

[App Store](https://apps.apple.com/app/net-conf/id6820239033) | [Google Play](https://play.google.com/store/apps/details?id=dev.jfversluis.dotnetconf) (store versions may differ from this source)

Open session videos when available, or use [on-device AI planning](docs/AI-PLANNER.md) on supported devices.

## Featured at .NET Conf 2026

- [What's New in .NET MAUI in .NET 11](https://www.youtube.com/watch?v=fh0c-EBCifU) | [Slides](https://github.com/dotnetConf/2026/tree/main/Technical/BRK006)
- [Beyond Code Generation: Give AI Eyes and Hands for .NET MAUI](https://www.youtube.com/watch?v=-3ClD7BHbLU) | [Slides](https://github.com/dotnetConf/2026/tree/main/Technical/BRK026)
- [Less Chat, More Action: AI-Powered .NET MAUI Apps](https://www.youtube.com/watch?v=FYGBT08-N48) | [Slides](https://github.com/dotnetConf/2026/tree/main/Technical/BRK033)

Related .NET MAUI sessions:

- [Bringing dotnet run, test, and watch to .NET MAUI](https://www.youtube.com/watch?v=Lw7nN8sBZn0) | [Slides](https://github.com/dotnetConf/2026/tree/main/Technical/BRK016)
- [How We Brought CoreCLR to .NET MAUI and Mobile](https://www.youtube.com/watch?v=fa_rzHLYU_E) | [Slides](https://github.com/dotnetConf/2026/tree/main/Technical/BRK034)

## Your data

The current source downloads sessions and speaker profiles from [Microsoft's public conference feed](https://dotnet.microsoft.com/blob-assets/json/conf/conference-2026.json) and caches them for offline use. Speaker photos load from URLs in the feed. A fresh install built from this source needs an internet connection; no agenda, speaker profiles, speaker photos, or community events are bundled.

Your saved sessions, watched status, and appearance preference stay on your device. See the [privacy policy](PRIVACY.md).

## Build and contribute

Install the SDK and workloads pinned in [global.json](global.json), along with the platform prerequisites in the [contributor guide](CONTRIBUTING.md#prerequisites). Choose a command for your configured emulator or simulator:

```sh
# Android
dotnet run --project src/DotNetConf.App -f net11.0-android

# iOS on an Apple Silicon Mac
dotnet run --project src/DotNetConf.App -f net11.0-ios -p:RuntimeIdentifier=iossimulator-arm64
```

Run `dotnet test tests/DotNetConf.Core.Tests` for the core tests.

Linux desktop and browser builds are experimental. Microsoft's feed blocks cross-origin requests, so the browser cannot download a fresh agenda directly; no relay is included. See the [platform limitations](CONTRIBUTING.md#linux-desktop-and-browser-experimental).

The [contributor guide](CONTRIBUTING.md) covers setup, testing, and [DevFlow inspection](CONTRIBUTING.md#inspect-with-copilot-and-maui-devflow). Report vulnerabilities through [SECURITY.md](SECURITY.md).

## License and credits

The source is [MIT licensed](LICENSE). The .NET Conf rocket-bot artwork and Bot placeholder are used with permission; this does not grant a general license or trademark rights. [Third-party notices and artwork credits](src/DotNetConf.App/Resources/Raw/THIRD-PARTY-NOTICES.txt) are also available in the app.
