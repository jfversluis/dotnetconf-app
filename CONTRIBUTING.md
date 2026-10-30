# Contributing to the .NET Conf companion

An **unofficial**, native .NET MAUI app for iOS and Android. The same project also has **experimental Linux desktop and browser targets**, where [Avalonia.Controls.Maui](https://github.com/AvaloniaUI/Avalonia.Controls.Maui) draws the controls. This first slice includes five Shell tabs, session/speaker details, search and day/topic filters, local-time agenda display, persistent My Schedule bookmarks, overlap warnings, community-event maps, and website links.

**Conference sessions and speaker profiles are remote-only**, downloaded from the public Microsoft and Sessionize conference feeds and cached for offline use. None ship in the app. A first install needs an internet connection; private organizer enrichment and GitHub-hosted portraits are not used. Branding and the Bot placeholder remain local. Fictional community-event examples are removed; the event list stays empty until an official source is connected. This is a development foundation, not a store-ready release.

Report security vulnerabilities privately using [SECURITY.md](SECURITY.md), not a public issue or pull request.

## Prerequisites

Versions are deliberately pinned:

| Component | Version |
|---|---|
| .NET SDK | `11.0.100-rc.2.26504.105` |
| Mobile workload set | `11.0.100-rc.2.26513.2` |
| MAUI Controls | `11.0.0-rc.2.26505.5` |
| DevFlow CLI / agent | `0.1.0-preview.13.26512.5` |
| Avalonia.Controls.Maui, Desktop, Essentials | `11.0.0-rc.1.26252.343` |
| Avalonia.Browser | `12.1.2` |
| IconFont.Maui.FluentIcons | `1.1.1` |
| Syncfusion.Maui.Toolkit | `1.0.11` |

Install the SDK selected by `global.json`, then:

```sh
dotnet workload install maui-android maui-ios wasm-tools --version 11.0.100-rc.2.26513.2
dotnet tool restore
dotnet test tests/DotNetConf.Core.Tests
dotnet test tests/DotNetConf.App.Tests -c Release -p:MobileTargetFramework=net11.0
```

On Linux, omit `maui-ios`; the project drops the iOS target there.

iOS builds require a Mac and Xcode 27.0, as required by the pinned RC2 workload's iOS 27 bindings. Android requires the workload's Android SDK dependencies, including API 37.0 tooling, and JDK 21. Deployment minimums are iOS 15 and Android API 24. These are different from the build SDK versions. See [MAUI release requirements](https://github.com/dotnet/maui/wiki/Release-Versions).

The iOS head uses the scene lifecycle: `Info.plist` registers the single-window `SceneDelegate`, which inherits `MauiUISceneDelegate`. MAUI owns window creation through `App.CreateWindow`; `AppDelegate` only builds the app. This opts out of the legacy application lifecycle used by the initial scaffold without adding custom window sizing or safe-area offsets.

The current RC2/Xcode 27.0 build pairing does not claim Xcode 27.1 build support. The scene-forwarding paths for App Actions, authentication URL returns, and user activities still need validation with this workload before adding deep links or authentication.

The current RC2 build was inspected on an iPhone 18 Pro and iPhone Duo simulator. The Duo uses its full 466 x 678 scene, and Settings and detail/document pages respect its side chrome through shared safe-area controls, not fixed offsets. Local validation used installed Xcode 27.1 with `ValidateXcodeVersion=false`, which is not the workload's supported pairing. Physical-device execution and folding transitions still need release validation.

After changing the scene manifest, clean and rebuild the iOS target and inspect the generated app bundle's `Info.plist` before reinstalling. During the RC1 migration, an incremental build retained the previous manifest; a successful compilation alone did not prove that the installed app used scenes.

## Run

```sh
dotnet run --project src/DotNetConf.App -f net11.0-ios -p:RuntimeIdentifier=iossimulator-arm64
dotnet run --project src/DotNetConf.App -f net11.0-android
dotnet run --project src/DotNetConf.App -f net11.0
dotnet run --project src/DotNetConf.App.Browser
```

The .NET 11 CLI can prompt for a target device. `net11.0` is the Linux desktop app; the browser head serves the WebAssembly build at `http://localhost:5235`. For a build without launching:

```sh
dotnet build src/DotNetConf.App -f net11.0-ios -p:RuntimeIdentifier=iossimulator-arm64
dotnet build src/DotNetConf.App -f net11.0-android
dotnet build src/DotNetConf.App -f net11.0
dotnet build src/DotNetConf.App.Browser
```

The app uses source-generated XAML with compiled bindings and Debug-only incremental XAML Hot Reload. Runtime selection is left to the SDK, with `PublishAot=true` in the project and no configuration-specific overrides. MAUI 11 RC2's Android Material 3 controls are explicitly enabled with `UseMaterial3=true`; scoped search and picker customizations use `EntryHandler2` and `PickerHandler2`, while iOS keeps its existing handlers. The pinned MAUI Controls package is updated with the workload. `dotnet watch --project src/DotNetConf.App -f net11.0-ios` uses the Debug-only no-linking setting; release linking is not disabled.

The RC1 incremental XAML generator was observed to retain stale resource-style updates and report `Style.Setters` as read-only after a style edit. If this recurs, a targeted `dotnet clean src/DotNetConf.App -f net11.0-android -p:RuntimeIdentifier=android-arm64` (or the corresponding iOS target/RID), followed by the normal build, clears that preview state. Do not disable incremental Hot Reload or edit generated sources to work around it.

The pinned Android Material 3 handler skips applying explicit toolbar backgrounds.
`Platforms/Android/ShellToolbarAppearance.cs` narrowly maps that existing color
to the native toolbar, preserving Material 3 and shared theme bindings. Recheck
this workaround when updating MAUI. Runtime light/dark switching and per-control
save-heart colors are covered by native screenshot checks; retain those checks
when changing themes. Platform values in XAML use typed `OnPlatform<T>` rather
than the reflection-based, trim-unsafe markup extension.

### NativeAOT builds

The app enables `PublishAot=true` by default for both mobile targets, without
conditioning it on configuration. `DotNetConf.Core` declares
`IsAotCompatible=true` to run its trimming and AOT analyzers independently;
the unit-test project does not need that property.

Use a Release publish to exercise native compilation and full trimming:

```sh
dotnet publish src/DotNetConf.App -c Release -f net11.0-android \
  -p:MobileTargetFramework=net11.0-android -r android-arm64 -p:AndroidPackageFormats=apk
dotnet publish src/DotNetConf.App -c Release -f net11.0-ios \
  -p:MobileTargetFramework=net11.0-ios -r ios-arm64
```

Android's signed local APK is under
`src/DotNetConf.App/bin/Release/net11.0-android/android-arm64/publish/`.
It uses the local debug signing key unless release signing is configured.
An installable iOS publish requires device signing; simulator builds are not a
substitute for a NativeAOT device publish. For compile-only validation without
signing, add `-p:EnableCodeSigning=false -p:BuildIpa=false` to the device publish.
That produces native device code but does not verify execution on a physical iPhone.

The pinned Android workload requests NativeAOT runtime
`11.0.0-rc.2.26504.105`, which is available on NuGet.org. `NuGet.Config` uses
NuGet.org for all packages, including `Microsoft.NETCore.App.Runtime.NativeAOT.android-*`;
the RC1-only daily-feed mapping is no longer needed.
Android NativeAOT is experimental in this workload (`XA1040`) and is not yet
suitable for production use. Do not suppress compatibility warnings. A Debug
build is not a substitute for a Release NativeAOT publish and runtime verification.
The pinned Syncfusion toolkit also reports an iOS `IL2070` trim warning
(`IL2104` in the default summary) in `SfHorizontalContent`'s reflective tap
handling. Use `-p:TrimmerSingleWarn=false` to inspect it. Do not suppress the
warning or treat a successful cross-publish as proof that vendor controls are
trim-safe at runtime.

## Linux desktop and browser (experimental)

These targets are experimental, not production-ready alternatives to the
primary iOS and Android apps. They have known feature gaps and less validation;
the historical verification notes below do not establish parity or current
release readiness.

`net11.0` and `net11.0-browser` use Avalonia.Controls.Maui in full-hosting mode; Android and iOS keep their native handlers and package graph. Both Avalonia targets define `AVALONIA`. Their setup lives in `Platforms/Avalonia` (`UseAvaloniaHost`), which the project registers as a `MauiPlatformSpecificFolder`. The browser entry point is `src/DotNetConf.App.Browser`: the WebAssembly SDK cannot be combined with the MAUI single project, so that small head only starts the app, which `DotNetConf.App` builds as a `net11.0-browser` library.

Differences from the mobile apps:

- IconFont.Maui.FluentIcons 1.1.1 brings its fonts to every target but has no platform-neutral assembly. `src/DotNetConf.FluentIcons` supplies the missing types with the same assembly name and API, limited to the glyphs in use. Add a glyph there when `Icons.xaml` starts using one, and remove the project once the package targets plain .NET.
- DevFlow, MAUI Maps and the CommunityToolkit.Maui initializer are mobile-only. `UserStoppedTypingBehavior` still drives search. The save toast does nothing, so the heart and saved count are the confirmation, and tab and toolbar badges are not drawn.
- Avalonia.Controls.Maui has no Map handler yet, so Community events open as the event list, as on Android builds without a Maps key.
- Syncfusion.Maui.Toolkit 1.0.11 has native views only for Android, iOS, Mac Catalyst and Windows. `Platforms/Avalonia/AvaloniaSyncfusion.cs` hosts its views with Avalonia's layout handler and forwards pointer input to the views that listen to their own touches. Their own drawing, such as ripple effects and scroll arrows, is not rendered; the Sessions day tabs (`SfTabView`) draw nothing visible as configured. Check other toolkit controls before using them on these targets.
- Saved sessions are kept in `~/.config/DotNetConf.App/schedule.json` on Linux and in the browser's local storage on the web, one `dotnetconf.saved.<id>` item per session, so tabs don't overwrite each other. If the browser blocks storage, `wwwroot/main.js` keeps them in memory for that visit.
- Website links go through Avalonia's launcher: `xdg-open` on Linux, a new tab in the browser.
- The Linux window icon is `Resources/Splash/splash.png`. The browser's page icon and loading screen use `wwwroot/splash.png`, a copy of that file with the `MauiSplashScreen` colors; update both together.

Avalonia dependency licenses and upstream notices are bundled under `Resources/Raw`
and linked from About's Third-party dependencies page on these targets. This
includes the Inter font's SIL Open Font License, the Avalonia.Controls.Maui
Apache-licensed MIME mappings, and the complete native SkiaSharp/HarfBuzzSharp
notices; an MIT package declaration does not replace those embedded-component
licenses. `THIRD-PARTY-NOTICES.txt` records the package versions and pinned legal-text
sources. When updating Avalonia or its resolved dependencies, refresh the
corresponding legal texts from those exact packages/source revisions and keep
the in-app list aligned. Preserve upstream notices verbatim.

`AvaloniaHost` and the app project also carry workarounds for Avalonia.Controls.Maui `11.0.0-rc.1.26252.343`, each commented in place: font icons on buttons and Shell tabs, Shell toolbar items that do not change with the tab or page, `EmptyView` overlapping a `CollectionView` header, an empty `FileSystem.AppDataDirectory` when `~/.config` does not exist, and fonts and raw assets missing from the assembly when the target has no images. Recheck them when updating the package.

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
- `src/DotNetConf.App.Browser`: WebAssembly entry point and static page for the browser build.
- `src/DotNetConf.Core`: conference contract/validation, search/timezone/conflict logic, and atomic local bookmark storage.
- `src/DotNetConf.FluentIcons`: Fluent icon glyphs for the Avalonia targets.
- `tests/DotNetConf.Core.Tests`: contract, search, date-boundary/DST, overlap, persistence, concurrency, and failure-path tests.

The community feed uses the Sessionize **All data** (`/api/v2/{endpoint}/view/All`) JSON shape, documented in the [Sessionize API playbook](https://sessionize.com/playbook/api#what-does-the-json-look-like-4). Microsoft's [conference JSON](https://dotnet.microsoft.com/blob-assets/json/conf/conference-2026.json) now supplies days 1-4, including published abstracts, topics and biographies. Its `day` values 1-4 map to November 10-13 in Pacific time; days outside that range and non-PST timezones remain invalid. **https://sessionize.com/api/v2/di6h1fo3/view/All** supplies days 3-4 and their public profiles. A contract fixture derived from the earlier October 30, 2026 public-feed structure is retained only in `tests/DotNetConf.Core.Tests/Fixtures/sessionize.json`, never as an app resource or runtime fallback. Its long descriptions, biographies and taglines are explicitly synthetic test prose; factual names, titles and identifiers remain for stable-identity coverage. It has 70 sessions and 70 speakers; these are fixture counts, not a required baseline for a first download. Do not use private organizer issues, unpublished fields or inferred profile data. Runtime adapters preserve published wording while filtering explicit unfinished bio/abstract placeholders and the "*No response*" pseudo-speaker.

That test capture has 66 supplied bios, 36 permitted portrait URLs, and 18 topic labels. All 36 community biographies and portraits are supplied by Sessionize. At capture time Microsoft published 30 useful bios; Matt Soucoup, Paul Yuknewicz, Scott Hanselman and Tanner Gooding had no finished public-feed bio. Speaker photographs are not bundled in `Resources/Images`; approved `profilePicture` HTTPS URLs support background downloading and offline caching. Missing, unavailable, or invalid portraits use the bundled `speaker_placeholder.png`, the exact .NET Bot image from https://www.dotnetconf.net/img/speakers/dotnet-bot.png. A first offline launch has no sessions or profiles and explains how to download them. Verify reuse rights for supplied photos/artwork before public release. Days 1-2 have no supplied taglines or room assignments; days 3-4 retain those fields from Sessionize.

Remote-only photos intentionally do not preserve identical photo availability to older bundled-portrait builds. GitHub-hosted images are prohibited even when public: the policy blocks GitHub, its subdomains, GitHub user-content/assets/Pages domains and GitHub-named AWS buckets. It also rejects non-HTTPS and credential-bearing URLs. The same shared policy applies during parsing, profile retention, cache validation, cached-file lookup and before following each portrait redirect. The dedicated portrait HTTP client disables automatic redirects; at most four approved redirects are followed manually. A browser may hide redirect details, in which case the download fails safely to the last approved cache or Bot.

The October 30 refresh removes organizer-only Tanner Gooding biography content and all ten Microsoft/organizer portrait URLs from the earlier snapshot. Even ordinary GitHub avatars for Bruno Capuano and Mads Torgersen are excluded. Microsoft's public Chet Husk and Damian Edwards biographies remain; the incomplete "Chet is" and Copilot-request note are filtered. A missing or unreachable photo is not a reason to add credentials or bundled photographs. HTTP 200 alone does not establish that an image passes format and size limits.

The Sessionize root has `sessions`, `speakers`, `questions`, `categories`, and `rooms`. Sessions use string `id`, `speakers` ID arrays, numeric `categoryItems`, and nullable numeric `roomId`; speaker objects use `fullName`, optional `firstName`/`lastName`, `bio`, **`tagLine`**, and `profilePicture`. Upstream may also provide numeric speaker `sessions` arrays. There is no app-specific wrapper around this payload. `SessionizeJson` uses source-generated serialization and maps this wire contract to the existing Core presentation/search models; unused fields (questions, links, service/plenum flags, etc.) are tolerated. Category item names supply topic filters when a feed publishes them. Unscheduled sessions with both times null stay out of the timed agenda; partially missing times, broken references, duplicate IDs, malformed/insecure profile-picture URLs, and invalid intervals fail explicitly. Otherwise valid GitHub portrait URLs are omitted rather than rejecting their sessions.

The test capture retains the agenda page's explicit Pacific offsets (`-08:00`). Remote timestamps with `Z` or an explicit offset retain their instant; offsetless timestamps are interpreted in the configured conference timezone, **America/Los_Angeles**, never the device timezone. Invalid/ambiguous DST wall times require an explicit offset and are rejected. Only display/day filtering converts instants to the device timezone.

Microsoft website session codes are mapped deterministically to local numeric IDs: `KEY001` -> `20260001`, `KEY002` -> `20260002`, `BRK001`-`BRK032` -> `20261001`-`20261032`, and the newly published days 3-4 codes `BRK100`-`BRK135` -> `20261100`-`20261135`. The original Microsoft speaker aliases are UUIDv5 values in the URL namespace, based on `https://dotnet.microsoft.com/en-us/conf/2026/agenda#` plus the exact published name; new speakers keep their published slugs. These are **app-generated session IDs, not organizer-issued Sessionize IDs**. Keep them stable when updating the remote adapter or test capture, even though the website URL changed. Sessionize retains its original session and speaker IDs. Both feeds are still ingested independently; overlapping days are not deduplicated by title or speaker name. Before replacing Microsoft IDs with live Sessionize IDs, provide an explicit ID/bookmark migration; neither names nor titles alone guarantee identity.

`Resources/Raw/conference.json` holds only app-owned presentation metadata, never conference sessions, speaker profiles, community events or photo mappings. Both mock flags are false. `agendaNotice` explains the remote sources, first-download requirement and lack of an official community-event feed in About. `ConferenceMetadataDataSource` loads this metadata through `IConferenceDataSource`; the repository rejects metadata containing an agenda or event examples. Fictional agenda/community fixtures live only in `tests/DotNetConf.Core.Tests/Fixtures/conference.mock.json`, preserving search, overlap, topic, timezone and map-contract edge cases without shipping fictional content.

Multiple speakers and overlapping sessions are supported. Bookmarks are stored by stable ID in private app storage, independently of the agenda. Invalid data or storage failures are shown rather than silently discarded; corrupt bookmark files are never overwritten.

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
to null. The page recreates the empty filter toolbar entry using shared MAUI
collection APIs to clear the stale native badge seen on iOS 27.1; do not map
zero to an empty string, which means an indicator badge. iOS renders toolbar badges
only on iOS 26+, so retain the visible selection summary. Native toolbar labels
and hints may retain their initial values when bindings change; don't rely on
them alone to announce the active count. Do not add a custom badge renderer for
older iOS.

### Remote updates and offline boundaries

`IConferenceRepository` loads a validated, versioned JSON cache without making HTTP requests. Without a usable cache, it returns metadata with an empty agenda, then the ViewModel initiates a first download. A separate refresh merges two independent HTTPS sources: [Microsoft's conference JSON](https://dotnet.microsoft.com/blob-assets/json/conf/conference-2026.json) and the [Sessionize All feed](https://sessionize.com/api/v2/di6h1fo3/view/All). The website's `/api/agenda` combines all sessions but omits Microsoft IDs and speaker profiles, so it is not the application's source.

The Microsoft adapter maps event codes to the existing session IDs and explicit source-slug aliases to existing speaker UUIDs. Display-name/title edits cannot change bookmark identity. Its day numbers and clock times are interpreted against the conference dates in America/Los_Angeles. The adapter rejects invalid references and filters explicit placeholders; incomplete profiles retain only useful previously downloaded source data, with portrait fallbacks rechecked against the shared URL policy. The Sessionize contract remains the community interchange format and test-fixture format.

Agenda cache schema 5 allows only downloaded conference data and separates withdrawn speaker profiles from the current directory. Schema-1 merged fields have untraceable provenance and are discarded before display. Schema-2/3/4 approved caches retain only validated sources with a remote payload hash and check time; ambiguous legacy profiles require source revalidation, and unvalidated snapshot seeds and all agenda/community-event fields in metadata are removed. A source that has never downloaded is genuinely empty. Only the agenda cache is atomically replaced; stable IDs and independent bookmark/appearance stores remain intact, including saved IDs whose details are not downloaded yet. A failed migration write surfaces a storage issue without displaying discarded content. Warm caches retain fast no-metadata/no-network startup. Unreferenced portrait files are pruned only once an agenda is available, avoiding destructive pruning on an empty first load.

Agenda/profile data is read-only cached reference data. Bookmarks and appearance remain separate local user state and are never uploaded. Public conference data needs no secret or database encryption; if sensitive data is added later, encrypt it and protect keys with `SecureStorage`. Never embed organizer/GitHub credentials.

Cache writes use temporary files and atomic replacement. Valid remote data remains browsable in memory if persistence fails, with a visible storage warning; the preceding file is not replaced. Completed sources are published and persisted independently, including when a sibling request is slow or canceled. Each source retains its last-good data, hash, validators and check/update timestamps; a failed source never removes another source's sessions. Bad JSON, empty feeds, duplicate IDs, broken references and invalid dates cannot replace the last-good source. A drop of more than 25% requires two identical complete validated responses; the pending confirmation survives restart, preventing both transient truncation and indefinite rejection of legitimate changes. Exactly 25% removal is accepted immediately. A first download has no packaged count baseline. Removals are archived separately, so saved withdrawn sessions remain understandable without keeping their profiles in the current directory or deleting bookmarks. A corrupt bookmark file disables saving with a visible error but does not block agenda browsing or overwrite that file. Retry rereads unavailable local schedule data even offline and reconciles existing session save flags after recovery.

The Microsoft response advertises a one-year cache lifetime, so refresh explicitly revalidates using `Cache-Control: no-cache` and ETags. Unchanged payloads/304s retain content. Requests have bounded timeouts, size limits, cancellation and transient-only retries. Cached data renders before the silent background refresh. Home, Sessions, Speakers and My Schedule expose native pull-to-refresh without refresh toolbar items. Failure-only inline Retry actions remain available. Gesture progress is separate from background work; automatic checks are quiet when data exists, while a genuinely empty agenda shows small first-download progress. Repeat requests join an existing refresh rather than starting duplicate work. Offline/update/storage problems show a compact inline Retry message. Expected lifecycle cancellation stops progress without creating a new error notice or clearing an existing failure. In-place ViewModel/collection reconciliation preserves identities, open details, searches, topics, local-day selection and scroll offsets.

Browser fetches omit custom revalidation headers, which Sessionize does not allow in CORS preflights. A unique query value bypasses browser HTTP-cache reuse while the app still compares payload hashes and retains its own offline cache. Sessionize can therefore populate and refresh a fresh browser install. Microsoft's feed does not grant browser CORS access at all; a fresh browser install cannot download its sessions, and its failure is disclosed without blocking community sessions. No relay/backend is included; full browser coverage requires upstream CORS support or a separately designed same-origin service.

The October 30 review-remediation pass has 366 passing Core/helper tests and seven actual App ViewModel tests. The headless App suite references the ordinary desktop assembly without starting MAUI; it disables transitive asset/bootstrap generation only in its test host. It covers saved-state recovery online/offline, withdrawn saved details, and stop/resume cancellation races. Repository tests cover independent source completion, repeated contraction confirmation, malformed-cache recovery, and migrations without bookmark resets.

Earlier DevFlow checks on the dedicated Duo and Android covered fresh offline startup, slow first-download progress, each source failing independently, partial-cache restarts/recovery, legacy migration, preserved bookmarks/counts, and real public portraits. The browser now starts without duplicate font registration. Real Edge verification loaded 36 community sessions and ten portraits, retained them and a bookmark across reload with external requests blocked, handled online/offline events, and reported no page exceptions. The app-shell host stayed reachable; this does not establish offline PWA bootstrap. Browser caches use bounded local storage (1 MiB agenda, 256 KiB per portrait, 700 KiB aggregate encoded portraits) and report storage failures. Browser builds require the pinned `wasm-tools` workload and native WASM linking for Skia/HarfBuzz.

The UI follow-up removed all four refresh toolbar items. Native pulls on all four surfaces were verified on the Duo and Android, including an empty saved schedule on iOS. About also exposes a keyboard/screen-reader-friendly refresh action. All three filter-clear paths clear the native badge while preserving day/search; actual screenshots verify the previously stale iOS badge disappears. The Duo speaker portrait moved from y=74 (under chrome ending at y=82) to y=98, retaining its full 96-unit height, using the shared top safe-area setting. Android portraits remain fully visible.

Final local validation passed Android/iOS Debug builds, ARM64 Android NativeAOT AAB publication, unsigned iOS-device NativeAOT publication, and Linux ARM64/browser builds. App-authored `OnPlatformExtension` trim warnings were removed using typed platform values and a typed iOS search-description binding. The existing Syncfusion iOS trim warning and Android experimental-NativeAOT warning remain. iOS used installed Xcode 27.1 with the workload-version check explicitly disabled. Simulator/emulator UI checks are not physical-device NativeAOT, store-signing, or store-approval evidence.

Agenda cache files live in MAUI `FileSystem.AppDataDirectory`; portrait files use `FileSystem.CacheDirectory` and may be evicted by the OS. Photos download in background batches of three, with a 30-second batch budget, a 4 MiB download cap and a 64 MiB newest-download-first disk budget. Only static JPEG, PNG and WebP images up to 2048 pixels per axis and 1,048,576 total pixels are accepted; GIF/APNG/animated WebP are rejected before native decoding. A shared layered MAUI portrait keeps the bundled website placeholder behind the downloaded cached image, including while loading and when a native decoder returns an empty result without a failure event. There are no bundled speaker photographs or platform image handlers. Missing/unreachable images retain a cached portrait or show the placeholder. Only UI changes marshal through MAUI `MainThread`.

`nikneem/sessionize-api-client` (`Sessionize.Api.Client` 2.1.3, MIT) was evaluated but is not used. Its reflection-based JSON path and incomplete category/nullable-schedule modeling would require keeping our source-generated adapter, while it provides neither the Microsoft source nor offline persistence. Retain the current typed Sessionize contract and bounded transport instead of adding an unused dependency.

Akavache v13 and its source-generated `System.Text.Json` path were evaluated. Its Rx/Splat APIs and SQLite/native persistence dependencies add no value for this small read-only snapshot. BCL `System.Text.Json` source generation plus atomic files provides the required AOT-safe persistence without another runtime package or native database. All new serialization supplies generated type metadata; keep the Core AOT/trim analyzers enabled.

Microsoft's endpoint currently has no browser CORS permission. Native iOS/Android and Linux can fetch both feeds; browser failures retain any previously downloaded Microsoft data and remain visible. Full browser refresh needs upstream CORS support or a same-origin relay, not a platform-specific bypass. Do not silently skip that source.

Reminders, local AI, and Avalonia-owned video integration remain deferred. PR build validation and manual Google Play internal-testing/TestFlight release lanes are described in [CI and release setup](docs/CI.md); production promotion remains manual and store credentials must be configured by the owner.

App-owned icons use [IconFont.Maui.FluentIcons](https://github.com/jfversluis/IconFont.Maui.FluentIcons). Website-matched Space Grotesk headings and Open Sans body fonts, gradients, Entry-based search, and heart save actions are documented in `DESIGN.md`. iOS uses MAUI safe-area controls and native automatic scroll insets to scroll beneath the system tab material without fixed offsets. The speaker-detail root also respects the top container safe area so its header does not begin under the Duo's navigation chrome; its bottom remains edge-to-edge.

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
RC2 version as Controls. `UseMauiMaps()` enables native MapKit/Google Maps handlers.
MAUI 11's `IsClusteringEnabled` and `Pin.ImageSource` APIs supply native clustering,
default cluster-tap zoom, and custom pin images; no custom map handler or
third-party clustering implementation is used. MAUI's `MapSpan.FromLocations`
frames available event locations, with extra zoom-out padding to keep marker
images away from the viewport edges. Map and list selections expose the
same event details and website action. The map starts with all event locations in
view; selecting from the list focuses that location.

`communityEvents` is an optional addition to schema version 1 (older documents
default to an empty list). Each event requires a stable `id`, `name`, `kind`
(`watch-party` or `community-conference`), `venue`, `city`, `country`, finite
`latitude`/`longitude` within geographic bounds, and an absolute HTTPS `websiteUrl`.
No community events are bundled or supplied by the currently connected agenda feeds.
The empty list links to the official website and does not construct a map.
The 24 fictional events remain only in the Core test fixture, with `example.com`
links and mixed/dense locations for contract and clustering examples.
`community-conference` is the data
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
platform provider's network services; an event list does not make the basemap
available offline. Currently no event dataset is connected.

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

## Linux and browser verification

The October 8, 2026 pass ran a `linux-arm64` build in an Ubuntu 24.04 container under Xvfb, and the WebAssembly build in Chrome. Both covered all five tabs, session/speaker details, search-as-you-type, clear/no-match states, the day filter, heart actions, the saved count, and bookmark persistence across a restart or page reload. The website link opened a new tab in the browser; the container has no `xdg-open`, so Linux showed the "Couldn't open the website" alert instead. Light and dark themes and the loading screen were checked in the browser only. A clean checkout also built and launched inside the container with the target frameworks limited to `net11.0`. The Android build and the 23 core tests passed; the iOS simulator build passed only with `ValidateXcodeVersion=false` on Xcode 27.0, which is not the supported pairing. Neither mobile app was relaunched.
