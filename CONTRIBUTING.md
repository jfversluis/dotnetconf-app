# Contributing to the .NET Conf companion

An **unofficial**, native .NET MAUI app for iOS and Android. This first slice includes five Shell tabs, session/speaker details, search and day/topic filters, local-time agenda display, persistent My Schedule bookmarks, overlap warnings, community-event maps, and website links.

**All bundled speakers and sessions are fictional sample data.** The real lineup and the authoritative event date range still need to be confirmed. This is a development foundation, not a store-ready release.

## Prerequisites

Versions are deliberately pinned:

| Component | Version |
|---|---|
| .NET SDK | `11.0.100-rc.1.26425.128` |
| Mobile workload set | `11.0.100-rc.1.26460.1` |
| MAUI Controls | `11.0.0-rc.1.26451.6` |
| DevFlow CLI / agent | `0.1.0-preview.12.26421.1` |
| IconFont.Maui.FluentIcons | `1.1.1` |
| Syncfusion.Maui.Toolkit | `1.0.11` |

Install the SDK selected by `global.json`, then:

```sh
dotnet workload install maui-android maui-ios --version 11.0.100-rc.1.26460.1
dotnet tool restore
dotnet test tests/DotNetConf.Core.Tests
```

iOS builds require a Mac and the selected workload's supported Xcode. The RC1 workload contains iOS 26.5 bindings; this foundation was built against Xcode 26.6. Android requires the workload's Android SDK/JDK dependencies, including API 37 tooling. Deployment minimums are iOS 15 and Android API 24. These are different from the build SDK versions. See [MAUI release requirements](https://github.com/dotnet/maui/wiki/Release-Versions).

The iOS head uses the scene lifecycle: `Info.plist` registers the single-window `SceneDelegate`, which inherits `MauiUISceneDelegate`. MAUI owns window creation through `App.CreateWindow`; `AppDelegate` only builds the app. This opts out of the legacy application lifecycle used by the initial scaffold without adding custom window sizing or safe-area offsets.

Keep the supported RC1/Xcode 26.6 build pairing distinct from running that binary on iOS 27 simulators. As checked on October 7, the .NET 11 RC2 Apple release is still a draft; this migration does not upgrade the build SDK to Xcode 27 or claim Xcode 27.1 build support. RC1 also lacks later scene forwarding fixes for App Actions, authentication URL returns, and user activities. Revisit the published .NET 11 workload and those paths before adding deep links or authentication.

The scene-enabled RC1 build was launched on an iPhone 18 Pro with iOS 27.0 and an iPhone Duo with iOS 27.1. Ordinary iPhone startup and search work. On Duo, the app still occupies a 375 x 667 compatibility window inside a 466 x 678 native scene, leaving a black right margin; adding the scene manifest alone does not fix this older-SDK geometry. Do not force window dimensions or disguise the margin with layout offsets. Full Duo geometry and folding remain release gates: rebuild with a published .NET 11 Apple workload supporting Xcode 27, then verify both postures.

After changing the scene manifest, clean and rebuild the iOS target and inspect the generated app bundle's `Info.plist` before reinstalling. The RC1 incremental build retained the previous manifest during this migration; a successful compilation alone did not prove that the installed app used scenes.

## Run

```sh
dotnet run --project src/DotNetConf.App -f net11.0-ios -p:RuntimeIdentifier=iossimulator-arm64
dotnet run --project src/DotNetConf.App -f net11.0-android
```

The .NET 11 CLI can prompt for a target device. For a build without launching:

```sh
dotnet build src/DotNetConf.App -f net11.0-ios -p:RuntimeIdentifier=iossimulator-arm64
dotnet build src/DotNetConf.App -f net11.0-android
```

Android Debug APKs embed their managed assemblies so they also work when installed directly by test tools. The app uses CoreCLR defaults, source-generated XAML with compiled bindings, and Debug-only incremental XAML Hot Reload. MAUI 11 RC1's Android Material 3 controls are explicitly enabled with `UseMaterial3=true`; scoped search and picker customizations use `EntryHandler2` and `PickerHandler2`, while iOS keeps its existing handlers. The pinned MAUI Controls package remains unchanged. `dotnet watch --project src/DotNetConf.App -f net11.0-ios` uses the Debug-only no-linking setting; release linking is not disabled.

The pinned RC1 incremental XAML generator can retain stale resource-style updates and report `Style.Setters` as read-only after a style edit. A targeted `dotnet clean src/DotNetConf.App -f net11.0-android -p:RuntimeIdentifier=android-arm64` (or the corresponding iOS target/RID), followed by the normal build, clears that preview state. Do not disable incremental Hot Reload or edit generated sources to work around it.

## Inspect with Copilot and MAUI DevFlow

Both the agent package and registration are **Debug-only**. Release builds contain no DevFlow agent. Android's Debug-only network policy permits local HTTP to the inspection broker (localhost, 127.0.0.1, and the emulator host 10.0.2.2); the policy is excluded from Release. The repo-local CLI avoids updating anyone's global tooling.

```sh
dotnet tool run maui -- devflow broker start
# Launch a Debug app using one of the commands above.
dotnet tool run maui -- devflow list
dotnet tool run maui -- devflow ui status
dotnet tool run maui -- devflow ui tree --depth 12
dotnet tool run maui -- devflow ui screenshot --output /tmp/dotnetconf.png
```

On Android, prepare the selected device's forwarding with the documented wait command while launching the app:

```sh
dotnet tool run maui -- devflow --device emulator-5554 wait --wait-platform Android
```

Use `devflow list` to identify the intended platform and agent port, then pass `--agent-port PORT` explicitly when more than one app is connected. Never drive an arbitrary first agent.

This preview can time out during Android discovery without returning a failing exit code. Confirm the connection with `list` or `ui status`. If discovery is unavailable, forward the selected app's agent port directly and inspect explicitly:

```sh
adb -s emulator-5554 forward tcp:10224 tcp:9223
dotnet tool run maui -- devflow --agent-port 10224 ui status
```

Use an unused host port and the actual agent port reported by the app. Remove only the forwarding you created when finished.

The preview's Android theme command also ignores device selection when multiple emulators are connected. For theme checks, target the test device explicitly with `adb -s SERIAL shell cmd uimode night yes` (dark) or `no` (light).

Stable AutomationIds include `home-sessions`, `session-search`, `session-filters`, `session-filters-clear`, `filter-topics`, `filter-check-{topic}`, `filter-reset`, `filter-show-sessions`, `save-language-features`, `detail-save`, `speaker-search`, and `saved-count`. Reusable status controls should be scoped to their page. To avoid mutation lease conflicts between short-lived CLI processes, use one batch for a sequence:

```sh
printf 'ui navigate //sessions\nui fill --automationId session-search Avalonia\nui tree --depth 14\n' \
  | dotnet tool run maui -- devflow --agent-port PORT batch --delay 500
```

Copilot can inspect and interact through these CLI commands. The CLI also exposes `devflow mcp` for hosts that support a stdio MCP server. The visual [Copilot Inspector canvas](https://github.com/dotnet/maui-labs/blob/main/docs/DevFlow/inspector.md) is a separate, source-preview integration in maui-labs; it is not automatically installed by the agent package. No custom canvas extension is bundled here.

Keep inspection local and use only test data. Stop a broker you started when finished, unless another app is using it:

```sh
dotnet tool run maui -- devflow broker stop
```

### Recommended MAUI agent skills

[MAUI Labs' plugin catalog](https://github.com/dotnet/maui-labs/blob/main/plugins/README.md) provides `dotnet-maui` for app architecture, current APIs, UI patterns, accessibility, testing, performance, and release guidance, plus `dotnet-maui-tooling` for DevFlow and workload/platform diagnostics. Both complement Impeccable: MAUI skills guide framework implementation; Impeccable guides the visual and interaction design. Architecture, UI patterns, controls, accessibility, and DevFlow are the most relevant areas for this app.

The repository includes 31 skills under `.github/skills`: the MAUI app-development plugin's 23 skills, five specialist tooling skills, and the three DevFlow skills installed with the pinned MAUI CLI:

```sh
dotnet tool run maui -- devflow init --scope project --target github
dotnet tool run maui -- devflow skills check
```

The app-development and specialist skills were installed using an isolated nightly MAUI CLI `0.1.0-preview.12.26479.1`, with `maui ai init --env CopilotCli --skill NAME ... --branch c2c5f0d0a46031f22976c13964052e928509cd5b --yes`. The source is pinned in each skill's `.skill-version` file; all skill destinations are project-local. No agents or MCP registrations were installed, and the repository's pinned CLI/DevFlow agent versions are unchanged. A current CLI's `maui ai status skill --env CopilotCli` inspects the inventory. Its GitHub downloads support `GITHUB_TOKEN` if unauthenticated API rate limits are reached; never store tokens in this repository.

## Structure and data

- `src/DotNetConf.App`: native pages, shared view models, theme/resources, platform assets, Debug-only DevFlow.
- `src/DotNetConf.Core`: conference contract/validation, search/timezone/conflict logic, and atomic local bookmark storage.
- `tests/DotNetConf.Core.Tests`: contract, search, date-boundary/DST, overlap, persistence, concurrency, and failure-path tests.

`Resources/Raw/conference.mock.json` is versioned, explicitly marked `isMock`, and loaded through `IConferenceDataSource`. Times are offset-aware instants converted using the device timezone. Multiple speakers and overlapping sessions are supported. Bookmarks are stored by stable ID in private app storage, independently of the agenda. Invalid data or storage failures are shown rather than silently discarded; corrupt bookmark files are never overwritten.

Session/speaker links reuse a matching detail already in the active Shell tab's navigation stack, popping back to that page rather than building circular history. New IDs still push normal details; the other tabs' histories remain independent. Native list scroll viewports span the page, while content templates retain 24-unit gutters, so scrollbars stay at the outer edge.

Sessions uses the MIT-licensed Syncfusion Toolkit `SfTabView`, registered with
`ConfigureSyncfusionToolkit()`, for equal-width, two-line local-day headers.
All days remains available. The index adapter maps to the existing `SelectedDay`
filter and ignores transient selection changes while local dates are rebuilt.
The page maps the dynamic Days collection to `SfTabItem.HeaderContent` with
`TabWidthMode=Default`; do not reintroduce `HeaderItemTemplate`/SizeToContent,
which skips iOS selected-header auto-scroll and clips the last day.
Search and topic filtering still use Core agenda logic. Header height is measured
from scaled MAUI labels; do not replace it with a fixed height or disable font
scaling. Verify all headers are visible, native day taps, combined filters,
clear-filter selection, detail/back preservation, and larger-text themes on both
platforms when changing this surface. The toolkit license is bundled offline.

The Sessions filter spike moves topics to `session-filters`, a registered,
DI-created native page sharing `ConferenceViewModel`. Choices apply immediately;
native back and Show N sessions keep them. Native CheckBoxes bind to typed topic
view models, and whole-row taps toggle the same state. Core search matches any
selected topic, then intersects the selected local day and search terms. The
same topics apply to every day tab and All days. Clear (also available beside the
main summary) removes all topics but preserves search and day. The main toolbar
binds MAUI 11 `BadgeText`, `BadgeColor`, and `BadgeTextColor`. The count is the
number of selected topics, not days, matching sessions or query terms; zero maps
to null. iOS renders toolbar badges
only on iOS 26+, so retain the visible selection summary. Native toolbar labels
and hints may retain their initial values when bindings change; don't rely on
them alone to announce the active count. Do not add a custom badge renderer for
older iOS.

The next data stage replaces the bundled source with a validated HTTPS feed and last-known-good offline cache. Do not just flip `isMock` on fictional content. Reminders, local AI, Avalonia-owned video integration, and desktop targets are intentionally deferred. PR build validation and manual Google Play internal-testing/TestFlight release lanes are described in [CI and release setup](docs/CI.md); production promotion remains manual and store credentials must be configured by the owner.

App-owned icons use [IconFont.Maui.FluentIcons](https://github.com/jfversluis/IconFont.Maui.FluentIcons). Website-matched Space Grotesk headings and Open Sans body fonts, gradients, Entry-based search, and heart save actions are documented in `DESIGN.md`. iOS uses MAUI safe-area controls and native automatic scroll insets to scroll beneath the system tab material without fixed offsets.

Launcher and light/dark splash screens use the website's two-robots-on-a-rocket graphic, with redistribution permission confirmed by the project owner on October 7, 2026. Android's themed launcher uses a matching monochrome silhouette. Artwork provenance and font licenses/notices are bundled under `Resources/Raw`; the artwork permission is not a general open-source license. The app follows the system theme and preserves native navigation. Its Android package name and iOS bundle ID are both `dev.jfversluis.dotnetconf`.

iOS caches launch-screen snapshots across same-version reinstalls. Increment `ApplicationVersion` when changing splash artwork (the rocket replacement was build 2; the enlarged launcher is build 3); rebuilding or restarting the simulator alone may still show the previous image. Install the new build rather than deleting the app and its saved schedule.

## Community maps

Community is the fifth Shell tab. Home's typed Fluent settings gear opens Settings,
which contains System/Light/Dark appearance, device-time-zone information, and a
link to the separate About page. Appearance persists in platform Preferences and
is restored before window creation; System maps to `AppTheme.Unspecified`.
Changing appearance uses MAUI's effective `RequestedTheme` and existing theme
events so status-bar styling and tab icons follow the user selection. Map pin
colors stay consistent with their legend in both themes.
The Community overview explains gathering formats and links to the pushed
`community-map` explorer, optionally with `mode=list`. The explorer and
`community-event` detail route hide the tab bar through `Shell.TabBarIsVisible`;
native back restores the overview. Trim-safe `IQueryAttributable` handles route
parameters. Event details return to the existing explorer, retaining its viewport,
list mode, and selection. The edge-to-edge map has a labeled legend, Show all and
Event list controls, and a compact dismissible preview. The legend sizes to its
content and wraps at larger text sizes; the preview scrolls within viewport-relative
bounds. Map actions occupy a separate, safe-area-aware bottom row, outside the
native map viewport, so Google Maps zoom controls and provider attribution stay
unobstructed on both platforms. Android pages retain keyboard avoidance without
adding another container inset on top of native Shell chrome.
`Microsoft.Maui.Controls.Maps` is pinned to the same
RC1 version as Controls. `UseMauiMaps()` enables native MapKit/Google Maps handlers.
MAUI 11's `IsClusteringEnabled` and `Pin.ImageSource` APIs supply native clustering,
default cluster-tap zoom, and custom pin images; no custom map handler or
third-party clustering implementation is used. MAUI's `MapSpan.FromLocations`
frames the bundled regional examples, with extra zoom-out padding to keep marker
images away from the viewport edges. Map and list selections expose the
same event details and website action. The map starts with all event locations in
view; selecting from the list focuses that location.

`communityEvents` is an optional addition to schema version 1 (older documents
default to an empty list). Each event requires a stable `id`, `name`, `kind`
(`watch-party` or `community-conference`), `venue`, `city`, `country`, finite
`latitude`/`longitude` within geographic bounds, and an absolute HTTPS `websiteUrl`.
The 24 bundled events and venues are fictional; their websites intentionally
use `example.com`. Mixed watch-party/presentation hubs in Amsterdam, London,
Paris, and Berlin exercise clustering at different zoom levels. Seven isolated
city examples keep individual pins visible. `community-conference` is the data
contract name for in-person talks presenting the latest .NET Conf materials.

`CommunityPinImages` draws a white-outlined teardrop using MAUI Graphics and typed
Fluent video/presenter glyphs. Density-aware PNGs are cached in app cache storage,
with retina file naming on iOS. Purple identifies watch parties; deep magenta
identifies in-person talks. Distinct symbols and a labeled legend avoid relying
on color alone. No separate graphics dependency or map sizing override is used.

About links to Third-party dependencies. Dependency rows open the bundled license
in a native reading page; notices and artwork credits are also available offline.
Keep this list and `THIRD-PARTY-NOTICES.txt` aligned with package/font updates.
DevFlow and Debug logging entries are compiled out of the Release dependency list.

iOS needs no map API key. Android builds accept `GoogleMapsApiKey` as an MSBuild
property or environment variable, substituted into `com.google.android.geo.API_KEY`.
Without it, the app does not create a native map and presents the event list
instead. Configure a Maps SDK for Android key restricted to
`dev.jfversluis.dotnetconf` (or the build's overridden application ID) and the
appropriate signing certificate SHA-1. Prefer an environment variable populated
by your secret manager over a command-line literal. Never commit keys to the
project, manifest, or launch settings. For local development, an `android-maps.env`
file at the repository root can contain `export GoogleMapsApiKey='YOUR_KEY'`.
Files ending in `.env` are Git-ignored; restrict local access with
`chmod 600 android-maps.env`, then load it in the shell used to build or launch:

```sh
source ./android-maps.env
dotnet run --project src/DotNetConf.App -f net11.0-android
```

The file is not loaded automatically by MSBuild or an IDE. The key is embedded
in the Android app, so Git exclusion does not replace package/signing-certificate
and Maps SDK for Android API restrictions. Avoid diagnostic logs and binary logs
when building with the key, as they can capture environment variables and properties.
For the local SHA-1 restriction, inspect the certificate of the actual signed APK;
.NET Android's default debug keystore can differ from `~/.android/debug.keystore`:

```sh
keytool -printcert -jarfile src/DotNetConf.App/bin/Debug/net11.0-android/android-arm64/dev.jfversluis.dotnetconf-Signed.apk
```

Adjust the APK path to match the build's runtime identifier. Use the reported SHA-1
with the build's application ID, not a fingerprint from an unrelated keystore.
Test-track builds accept the optional
`GOOGLE_MAPS_ANDROID_API_KEY` environment secret described in [docs/CI.md](docs/CI.md).
An invalid/restricted key can still prevent map tiles from loading; the Event list
control remains available. A configured Android map and its clustering must be
verified before release.

No GPS permission is requested and `IsShowingUser` is false. Map tiles use the
platform provider's network services; offline availability applies to the bundled
event list, not the basemap.

The October 8 verification covered the Community overview, full-screen explorer,
direct list entry, compact selection preview, and event-detail/back navigation.
iOS native cluster zoom and custom-pin selection were checked through the map's
overlay-free canvas; Android's keyless build used its honest list fallback.
Both platforms were checked in light/dark themes with larger text, including
native overview scrolling and footer reachability. Prior checks covered event
website opening, native About navigation, and retained session-search focus.
Configured Android map tiles and marker interactions remain
pending the owner-supplied key. The current regional fixture fits MAUI's
`MapSpan.FromLocations` bounds; verify world-spanning and date-line datasets when
the real event feed becomes available.

## Android verification

The October 7, 2026 pass used an Android 16 / API 36 ARM64 emulator. Debug checks covered all five tabs, session/speaker details, debounced search with native keystrokes and retained focus, clear/no-match states, day/topic filters, heart actions and toasts, saved-count badges, overlaps, bookmark persistence across relaunch/reinstall, and native load-error Retry recovery. Light/dark themes, rocket splash screens, 1.5x text scaling, and a 320 dp-wide viewport were also checked. Release was installed separately and cold-started in both themes, with search and existing bookmarks confirmed; its DevFlow endpoint was unavailable as intended. The 18 core tests passed.

Native pickers can temporarily publish a null selection when their items change. The view model models that lifecycle explicitly and restores the day selection after rebuilding local dates; topic selection is initialized after populating its items. Loading controls collapse when idle instead of reserving empty header space. Physical-device, TalkBack, and minimum-supported-Android checks remain release gates.
