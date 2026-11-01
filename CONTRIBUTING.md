# Contributing to the .NET Conf companion

An **unofficial**, native .NET MAUI app for iOS and Android. The same project also has **experimental Linux desktop and browser targets**, where [Avalonia.Controls.Maui](https://github.com/AvaloniaUI/Avalonia.Controls.Maui) draws the controls. The app provides five Shell tabs, session/speaker details, search and day/topic filters, local-time agenda display, persistent My Schedule bookmarks, overlap warnings, a community overview, and website links. Native maps and event details are implemented but need an official event source before they can display gatherings.

**Conference sessions and speaker profiles are remote-only**, downloaded from the public Microsoft conference feed and cached for offline use. None ship in the app. A first install needs an internet connection; private organizer enrichment and GitHub-hosted portraits are not used. Branding and the Bot placeholder remain local. No community-event examples are bundled; the event list stays empty until an official source is connected. The app is in development and has not been released.

Report security vulnerabilities privately using [SECURITY.md](SECURITY.md), not a public issue or pull request.

## Prerequisites

Versions are deliberately pinned:

| Component | Version |
|---|---|
| .NET SDK | `11.0.100-rc.2.26504.105` |
| Mobile workload set | `11.0.100-rc.2.26513.2` |
| MAUI Controls | `11.0.0-rc.2.26505.5` |
| DevFlow CLI / agent | `0.1.0-preview.13.26512.5` |
| Avalonia.Controls.Maui, Desktop, Essentials | `11.0.0-rc.2.26287.618` |
| Avalonia.Browser | `12.1.4` |
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

The iOS head uses the scene lifecycle: `Info.plist` registers the single-window `SceneDelegate`, which inherits `MauiUISceneDelegate`. MAUI owns window creation through `App.CreateWindow`; `AppDelegate` only builds the app. Window sizing and safe areas use MAUI and platform controls, not fixed offsets.

The current RC2/Xcode 27.0 build pairing does not claim Xcode 27.1 build support. The scene-forwarding paths for App Actions, authentication URL returns, and user activities still need validation with this workload before adding deep links or authentication.

The current RC2 build was inspected on iPhone and iPhone Duo simulators. Settings and detail/document pages respect system chrome through shared safe-area controls, not fixed offsets. Local validation used installed Xcode 27.1 with `-p:ValidateXcodeVersion=false`, bypassing the workload-version check; this is not the workload's supported pairing or release certification. Physical-device execution and folding transitions remain release gates.

### Adaptive session and speaker details

Sessions and Speakers use the shared `AdaptiveConferencePage` Grid and platform
division regions. Above 640 device-independent units of window width, an
unobstructed window uses a 42:58 list/detail split. A vertical division gets a
reserved column; a horizontal division stacks the list above the detail with a
reserved row. The gap disappears when the system no longer reports a division.
Short phone landscape windows stay compact.

The list needs 150 units of content width per measured text-scale unit after
48 units of horizontal padding. Side-by-side layouts also require a window height
of at least 600 units per scale unit; stacked layouts instead require both panes
to have at least 200 units per scale unit. If they do not fit, the app uses the
existing native full-width detail routes. The native fallback is not partitioned
around a fold. A non-interactive, accessibility-hidden monitor compares scaled
and unscaled line heights without platform-specific APIs, including on native
details so changing text size can restore the pane. When height is constrained,
the list headings are hidden and the Sessions day/topic controls move into the
collection header. Search remains pinned outside the virtualized collection so
filtering does not dismiss the keyboard. `SessionDetailView` and
`SpeakerDetailView` supply the same content in either host.

Android uses MAUI's resize input mode and page-level `SoftInput` safe area so
the content viewport ends above the software keyboard. Adaptive pane grids use
`None` at the bottom on Android to avoid applying that inset twice; on iOS they
use `SoftInput` at the bottom. When compact search hides Shell chrome, Android
compares the pane's actual position with the window's IME inset and applies only
any remaining overlap as bottom padding. It removes that padding when compact
search ends, so a stale navigation-bar inset cannot leave the last row under
the keyboard or create a blank strip afterward. When a
focused landscape search would leave less
than 120 units of result space, the host temporarily hides Shell navigation and
shows a headerless compact result list. Android also prepares that layout
before the keyboard opens in short landscape windows when no hardware keyboard
is reported, hiding both navigation and tab bars to preserve focus. The previous
chrome visibility is restored on blur, rotation, or disappearance; Android
Back dismisses the keyboard and unfocuses search after the keyboard has been
shown. On Android the pinned Shell handler's native tab view is temporarily
collapsed without changing Shell's tab visibility, avoiding overlapping fragment
removal and reattachment during rapid fold or navigation transitions. If folding
causes search to lose focus,
Android also dismisses the orphaned keyboard while retaining the query and
selection. `SearchEntry`
requests a non-fullscreen Android IME after MAUI's initial
handler mappings, preserving the Search action; an IME may ignore the request.
Active day/topic filters still apply, and a compact error/Retry control
preserves update-failure feedback. Both virtualized lists stay laid out while
the inactive one is transparent, input-transparent, and excluded from
accessibility. This avoids changing a native collection header or data source
during iOS keyboard transitions; recheck it when upgrading the MAUI items
handler. On Android, `SearchResultLayerHandler` keeps each input-transparent
layer's native container in place so switching search modes does not reparent a
RecyclerView while its items are animating.

On iOS, the pinned MAUI RC2 can treat a zero-size keyboard notification during
rotation as a visible keyboard, restoring an unwanted bottom inset. A
page-scoped workaround refreshes the affected soft-input policies only for
that empty frame and cancels a pending refresh when a newer keyboard event
arrives. Recheck the workaround when upgrading MAUI.

`DetailNavigationService` keeps one selected detail per master tab, not a detail
history. List selections and speaker/session cross-links replace that detail;
the pane has no Back or Close controls. Resizing carries only the current detail
between the pane and a single native detail route. In compact mode, cross-links
replace that route so native Back returns directly to the master list.
Returning to the list retains the selection for the next expansion. The empty
prompt appears only before the tab has a selection. Filters and other non-detail
routes remain native; Home and My Schedule keep their normal native navigation.
Shell's navigation stack starts with a null sentinel, not the tab's root page:
adaptive hosts are matched to the current ShellContent instead.
Window-size synchronization updates every cached host's layout policy. Preserve
the selection when a pane or tab is hidden, but release its native detail
controls and recreate them on return. An unloaded host's measured size and fold
coordinates can be stale, so use the current window until it is loaded again.
On iOS and Avalonia, initialize `DualScreenInfo` only after the pane has positive
dimensions. Use actual region bounds rather than `TwoPaneView`/`SpanMode`: the
published RC2 can retain an incorrect mode after a cached tab slides onscreen.
Android instead observes separating AndroidX Window Manager folding features
directly while the activity is started and converts window coordinates to pane
coordinates. The listener uses the platform-owned main-thread executor because
its callbacks can outlive activity stop.

Avalonia uses `Microsoft.Maui.Controls.Foldable` pinned to MAUI RC2. That
package does not yet include iPhone Duo division-region support. Until a
compatible release is available, iOS builds use the source backport in
`src/DotNetConf.Foldable.iOS`, based on
[dotnet/maui@a561eaec4aacbe962416c64491aa5ffbc6bde9ae](https://github.com/dotnet/maui/tree/a561eaec4aacbe962416c64491aa5ffbc6bde9ae)
and adapted to `IFoldableServiceCompat` because the pinned MAUI Core lacks the
newer interface. `src/DotNetConf.Foldable.iOS.Bindings` contains the small
public UIKit bindings needed by the pinned workload, adapted from
[dotnet/macios@eb45b2d9f615586fdcd19c90be6ce78aeb9004b3](https://github.com/dotnet/macios/tree/eb45b2d9f615586fdcd19c90be6ce78aeb9004b3).
The selectors are bound as `NSObject`/`UIView` extensions and the UIKit class
factories are resolved at runtime; binding those system classes directly would
make the NativeAOT registrar redeclare their Objective-C interfaces. The UIKit
APIs are guarded for iOS 27.1 and later; older systems use the normal
width-based layout. Preserve the upstream license notices. When an official
Foldable package and workload support the same API, follow
[Replacing temporary Foldable support](#replacing-temporary-foldable-support)
below rather than deleting all adaptive-layout code.

The Duo simulator reported a 40-point active division in both orientations:
landscape separated the panes horizontally and portrait stacked them vertically;
fully opening removed the gap. The Android emulator did not report a folding
region. A temporary Debug-only test injected synthetic AndroidX callbacks and
verified separating regions in both orientations and the absence of a gap for
flat/non-separating features; this is not physical hinge evidence. Physical Duo
and Android foldables and a genuinely wide iPad remain release checks; a 2D
simulator screenshot does not establish physical crease clearance. At maximum
accessibility text size the native full-width fallback can cross a division.
After changing the scene manifest, clean and rebuild the iOS target and inspect the generated app bundle's `Info.plist` before reinstalling. Confirm the installed manifest and runtime scene behavior rather than relying on compilation alone.

### Replacing temporary Foldable support

This is the removal checklist for a future session or agent upgrading
`Microsoft.Maui.Controls.Foldable`. The platform backport is temporary; the
app's master/detail experience is not. A newer package version alone is not
permission to remove every workaround. Migrate iOS and Android independently
if the release replaces only one platform's implementation.

#### Prerequisites and replacement contract

Before deleting source, inspect the chosen release's APIs, implementation, and
resolved dependencies. Record the package/workload versions and the upstream
changes that replace each local implementation in the upgrade PR. Update
`global.json` and the app's MAUI package pins together as required by that
release; confirm compatibility with the experimental Avalonia targets too.

For iOS, the compatible workload must provide the UIKit division-region
bindings, and the MAUI Core/Foldable packages must provide the service
interface currently replaced by `IFoldableServiceCompat`. Preserve the
older-iOS width-based fallback and availability guards. Compare upstream
behavior with the local backport,
including active-region filtering, monitor subscription/disposal, and changes
while views are unloaded; do not assume every local fix is in the release.

For Android, the replacement must deliver separating folding-region bounds
and change notifications through activity start/stop and repeated folding.
Establish whether bounds are window-relative or view-relative and whether they
use pixels or device-independent units. For both platforms, translate into
the actual pane coordinate space exactly once and preserve current-window
fallbacks when cached page measurements are stale. Do not switch to
`TwoPaneView`/`SpanMode` alone unless the cached-tab regression described above
is demonstrably fixed.

#### Exact cleanup points

Paths below are repository-relative; app paths are under `src/DotNetConf.App/`.
Wire and validate the replacement before removing its predecessor.

| Replacement | Files and references to change |
|---|---|
| Official iOS Foldable support plus compatible UIKit bindings | Replace the conditional iOS `ProjectReference` in `src/DotNetConf.App/DotNetConf.App.csproj` with the official Foldable package and adjust its existing platform condition to include iOS. Remove `src/DotNetConf.Foldable.iOS/` and `src/DotNetConf.Foldable.iOS.Bindings/` once no references remain. The backport uses the official assembly name: do not load the local and official assemblies together. Keep the official Foldable startup registration in `MauiProgram.cs` and adapt it to the actual released API. |
| Tests tied to the iOS backport | Remove the linked `FoldableRegionHelper.cs` compile item from `tests/DotNetConf.App.Tests/DotNetConf.App.Tests.csproj`. `FoldableRegionTests.cs` directly tests the local region helper and monitor registry; retain applicable behavior coverage through replacement adapter tests or explicitly referenced upstream tests, and retire local implementation-only tests with the deleted source. Do not keep a copy of the backport solely to make those tests compile. Preserve the app-level layout and selection regression tests. |
| Equivalent official Android region delivery | Remove `Platforms/Android/AndroidFoldableRegionSource.cs`, its two DI registrations, its `OnStart`/`OnStop` callbacks, and `GetFoldableRegionSource()` from `MauiProgram.cs`. Include Android in the official Foldable package/startup registration and namespace import; Android currently excludes them. Remove the direct `Xamarin.AndroidX.Window.WindowJava` reference only when app code no longer uses it. Inspect transitive AndroidX dependencies before changing notices. |
| Android-specific adapter plumbing becomes unused | Replace the Android branches and source subscription in `Services/FoldableViewport.cs`. Remove `Services/IFoldableRegionSource.cs` only if it has no remaining consumers. Remove the corresponding constructor parameter/property from `Services/DetailNavigationService.cs` and update viewport construction in `Pages/AdaptiveConferencePage.cs`, `Pages/SessionDetailPage.xaml.cs`, and `Pages/SpeakerDetailPage.xaml.cs`. Keep the shared viewport's layout policy, positive-size initialization, and stale-measurement protection unless upstream demonstrably replaces them. |
| Attribution and documentation | Update `Pages/ThirdPartyPage.xaml.cs`, `Resources/Raw/THIRD-PARTY-NOTICES.txt`, and this guide's backport/version description. Review `LICENSE-MaciOS.txt`, `LICENSE-AndroidXBindings.txt`, and `NOTICE-AndroidXWindow.txt` against the new resolved and bundled dependency graph. Remove a legal resource only if no remaining component needs it. Retain shared MAUI/.NET Foundation license text and all unrelated artwork/font notices. Update relevant `DESIGN.md` explanations if the implementation changes. |

Search for residual references after migration, including test compile items:

```sh
rg -n 'DotNetConf\.Foldable|IFoldableServiceCompat|IFoldableRegionSource|AndroidFoldableRegionSource|FoldableRegionHelper|WindowJava' src tests
```

Classify each remaining match; an official package's transitive dependency is
not necessarily an obsolete dependency. Perform clean, target-specific restores
and builds so old local assemblies or generated binding outputs cannot make
the migration appear successful.

#### Keep app behavior; assess MAUI workarounds separately

Keep `AdaptiveConferencePage`, `DetailLayout`, `DetailSelection`,
`DetailNavigationService`, the shared session/speaker detail views, row
selection styling, and `TextScaleMonitor`. They implement responsive sizing,
accessible text scaling, retained selection, and native compact navigation,
not missing Foldable APIs. Replacing the platform observer does not authorize
changing those behaviors.

These pieces are also **not automatically removable with a Foldable update**:

| Code | Removal requires separate evidence |
|---|---|
| `Platforms/iOS/EmptyKeyboardFrameWorkaround.cs`, `Services/KeyboardFrameRefreshGuard.cs`, its linked test compile item and `KeyboardFrameRefreshGuardTests.cs`, and the installation in `AdaptiveConferencePage` | A MAUI soft-input fix must prevent zero-height rotation notifications from restoring the blank bottom strip while still avoiding a real keyboard. Verify that a newer keyboard event supersedes an older deferred update before removing the guard and its tests. |
| `HideSearchTabBar`/`RestoreSearchTabBar` and orphan-keyboard cleanup in `AdaptiveConferencePage` | The pinned Android Shell handler asynchronously removes tab fragments when Shell visibility changes. Waiting for detachment fixed blur but still failed during rapid navigation. The current code collapses the attached native tab view without changing Shell tab visibility. Remove or replace this only after rapid focus/fold/navigation sequences restore all five tabs and preserve pages that intentionally hide tabs. |
| `Platforms/Android/SearchKeyboardInset.cs`, the measured-overlap logic in `AdaptiveConferencePage`, and `SearchEntry`/`AgendaSearchField` focus and IME handling | Short landscape search must retain usable results, apply only remaining IME overlap, and avoid an orphaned keyboard. Preserve the non-fullscreen request and Search action unless equivalent platform behavior is established; IMEs may ignore that request. |
| The full/compact result-list strategy in `SessionsPage.xaml`, `SpeakersPage.xaml`, and `AdaptiveConferencePage`, plus `Controls/SearchResultLayer.cs`, `Platforms/Android/SearchResultLayerHandler.cs`, and its registration in `MauiProgram.cs` | Both virtualized lists currently remain laid out, with the inactive layer transparent, input-transparent, and accessibility-hidden. Recheck the MAUI items handler before simplifying this: changing collection headers/data sources during iOS keyboard transitions previously required avoiding native view churn. On Android, toggling input transparency directly on an updating list reparented its RecyclerView and crashed; verify repeated search/filter updates and keyboard transitions before removing the stable layers. |
| `Controls/SafeAreaFooter.cs` and pane safe-area policies | A MAUI items/inset fix must keep the actual final rows and links above native tabs/home indicators without restoring a blank bottom viewport margin or applying keyboard insets twice. Foldable region support alone does not fix these insets. |

Keep `RejectDebugDevFlowAssetsInRelease` in the app project and the Debug-only
DevFlow package, registration, logging, and network policy. These are release
safety measures, not foldable compatibility code.

#### Migration regression matrix

Run the Core and App tests from [Prerequisites](#prerequisites), then the
relevant platform builds from [Run](#run) and Release publishes from
[NativeAOT builds](#nativeaot-builds). Run builds/tests serially because targets
share restore assets. A Release publish must restore Release dependencies;
do not reuse Debug assets with `--no-restore`.

Reproduce the following at runtime after the replacement and again after
removing any separately fixed workaround. Use the repo-local
[DevFlow inspection workflow](#inspect-with-copilot-and-maui-devflow), stable
AutomationIds, actual screenshots, and settled UI-tree geometry. Keep generated
evidence outside the repository and identify the exact installed build.

| Scenario | Required outcome and learned failure mode |
|---|---|
| Cold start open, half-folded, or closed; rotate between portrait and both supported landscape directions | Both Sessions and Speakers start correctly before positive pane measurements exist. An active vertical division separates left/right panes; an active horizontal division stacks them. The gap follows reported bounds and disappears when the region becomes inactive. Do not hard-code the simulator's 40 points; inactive, non-separating, or out-of-view regions must not create a reserved gap. |
| Repeated open/half/closed transitions with no selection and with either detail type selected | The empty prompt appears only before selection. Only the latest detail moves between split and compact modes. Native Back returns directly to the master list; cross-links replace detail rather than accumulating routes. Reopening restores the retained selection. |
| Switch cached Sessions/Speakers tabs while folded, change posture while a tab is hidden, open Filters, background/resume, and recreate views | Bounds and mode are current on return, without relying on an unloaded page's old measurements. Hidden hosts release native detail controls and recreate them on return. Monitor callbacks do not crash after activity stop or duplicate subscriptions. Filters and non-detail routes retain their intended chrome. |
| Focused search during fold/rotation; show/dismiss keyboard; rapid blur/refocus and navigate before chrome restoration completes | Query, filters, and selection survive. Matching and empty results plus error/Retry remain usable above the keyboard. Android Back dismisses/unfocuses search, orphan keyboards do not cover the reopened screen, and all five tabs return when intended. Verify tabs are clickable, not merely present in the tree. |
| Keyboard absent, zero-height iOS rotation notification, real keyboard visible, and a newer keyboard event during a pending refresh | Pane/list/detail scroll surfaces reach their intended bottom content edge without a blank strip, double inset, or keyboard overlap. Scroll to the true final item/link and visually verify it above native chrome; a correct outer Grid bound alone is not rendering proof. |
| Ordinary phones, older iOS, wide tablet/desktop/browser windows, dark/light/system appearance, large and maximum accessibility text | Preserve compact native navigation on short/narrow windows and readable split/stacked panes when they fit. Preserve text-driven fallback, font scaling, selection semantics, and honest feed/cache error states. A hardware keyboard should not unnecessarily trigger software-keyboard chrome suppression. Check screen-reader focus and ensure inactive compact/full results are not announced twice. |
| Clean Android and iOS Release NativeAOT artifacts and device execution | No duplicate Objective-C registrations, stale backport assembly, unguarded newer UIKit selector, DevFlow metadata/agent, or development-only logging. An unsigned iOS device publish is compile-only evidence, not physical-device execution. Rebuild and inspect the generated `Info.plist` if scene configuration changes. |

The existing evidence is simulator/emulator evidence: Duo reported 40-point
active regions and zero gap fully open; the Android emulator did not deliver
a real hinge region. Synthetic AndroidX callbacks exercised layout but did not
establish physical hinge clearance or OEM delivery. Physical Duo/Android
foldables, a genuinely wide iPad, hardware keyboards, and real screen readers
remain validation gaps. Gboard was the tested Android IME; other keyboards may
choose fullscreen mode. Android dark-mode status-bar contrast is a pre-existing
issue, and short landscape Sessions filters can initially leave little result
space until scrolled. Report these limitations rather than claiming universal
hardware coverage.

The upgrade handoff should state which implementations were deleted, which
workarounds remain and why, the package/workload/OS/device versions tested, and
link the new screenshots and geometry. This checklist is complete only after
the replacement behavior has been verified, not merely after compilation.

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

If incremental XAML generation retains stale resource styles or reports `Style.Setters` as read-only after a style edit, use a targeted `dotnet clean src/DotNetConf.App -f net11.0-android -p:RuntimeIdentifier=android-arm64` (or the corresponding iOS target/RID), followed by the normal build. Do not disable incremental Hot Reload or edit generated sources to work around preview-tooling state.

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

Allow these commands to restore Release dependencies. Reusing a Debug restore
with `--no-restore` can otherwise import DevFlow build targets into Release; the
project rejects that configuration.

Android's signed local APK is under
`src/DotNetConf.App/bin/Release/net11.0-android/android-arm64/publish/`.
It uses the local debug signing key unless release signing is configured.
An installable iOS publish requires device signing; simulator builds are not a
substitute for a NativeAOT device publish. For compile-only validation without
signing, add `-p:EnableCodeSigning=false -p:BuildIpa=false` to the device publish.
That produces native device code but does not verify execution on a physical iPhone.

The pinned Android workload requests NativeAOT runtime
`11.0.0-rc.2.26504.105`, which is available on NuGet.org. `NuGet.Config` uses
NuGet.org for all packages, including `Microsoft.NETCore.App.Runtime.NativeAOT.android-*`.
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
successful builds do not establish UI parity or release readiness.

`net11.0` and `net11.0-browser` use Avalonia.Controls.Maui in full-hosting mode; Android and iOS keep their native handlers and embed Avalonia only for the schedule planner. Both Avalonia targets define `AVALONIA`. Their setup lives in `Platforms/Avalonia` (`UseAvaloniaHost`), which the project registers as a `MauiPlatformSpecificFolder`. The browser entry point is `src/DotNetConf.App.Browser`: the WebAssembly SDK cannot be combined with the MAUI single project, so that small head only starts the app, which `DotNetConf.App` builds as a `net11.0-browser` library.

Linux requires the usual Avalonia native GUI dependencies, ICU and system timezone data (`tzdata` on Ubuntu). Missing Pacific timezone data produces a visible agenda-validation failure rather than substituting a fixed UTC offset.

Differences from the mobile apps:

- IconFont.Maui.FluentIcons 1.1.1 brings its fonts to every target but has no platform-neutral assembly. `src/DotNetConf.FluentIcons` supplies the missing types with the same assembly name and API, limited to the glyphs in use. Add a glyph there when `Icons.xaml` starts using one, and remove the project once the package targets plain .NET.
- DevFlow, MAUI Maps and the CommunityToolkit.Maui initializer are mobile-only. `UserStoppedTypingBehavior` still drives search. The save toast does nothing, so the heart and saved count are the confirmation, and tab and toolbar badges are not drawn.
- Avalonia.Controls.Maui has no Map handler yet, so Community events open as the event list, as on Android builds without a Maps key.
- Syncfusion.Maui.Toolkit 1.0.11 has native views only for Android, iOS, Mac Catalyst and Windows. `Platforms/Avalonia/AvaloniaSyncfusion.cs` hosts its views with Avalonia's layout handler and forwards pointer input to the views that listen to their own touches. Their own drawing, such as ripple effects and scroll arrows, is not rendered. Check toolkit visuals and pointer input before using controls on these targets.
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

`AvaloniaHost` and the app project also carry workarounds for Avalonia.Controls.Maui `11.0.0-rc.2.26287.618`, each commented in place: font icons on buttons and Shell tabs, Shell toolbar items that do not change with the tab or page, `EmptyView` overlapping a `CollectionView` header, an empty `FileSystem.AppDataDirectory` when `~/.config` does not exist, and fonts and raw assets missing from the assembly when the target has no images. Recheck them when updating the package.

## Schedule planner

My Schedule opens on a timeline of saved sessions only, drawn by Avalonia. Pages use `Planner/SchedulePlanner`, an ordinary MAUI view, and `SchedulePlannerHandler` (an `AvaloniaControlHandler`) passes its properties to the Avalonia control, `Planner/PlannerView`. Android and iOS embed that control in the native app through `UseAvaloniaEmbedding<PlannerApplication>()`, and the Linux desktop and browser draw it like everything else, so Avalonia.Controls.Maui is referenced on every target. The List button shows the same saved sessions in a native list. `SavedTimelineSessions` is the planner's single membership input, an immutable snapshot published by `RefreshSchedule`; the layout, counts, overlap bands, route, and overview all use that snapshot. Days come from the published session timestamps rather than a fixed conference date or day count. With no available saved sessions, both modes show the shared loading/download/empty state and Find a session action. Withdrawn favorites remain available, as in the list.

The layout comes from `DotNetConf.Core.TimelineLayout`: each local day to scale, overlapping sessions side by side, and long gaps shortened to a break. Cards retain their topic-colored dot, time text, and subtle surface tint to match the filter chips; a filled accent-colored heart indicates a saved session, without replacing the topic colors. Only what's on screen gets controls, so it stays smooth even with very large agendas. It zooms with pinch, trackpad, Ctrl + wheel, the keyboard or its zoom buttons, and supports keyboard navigation, screen readers and both themes. On the phones it also follows the system text size and reduce motion.

When changing it:

- The handler reads its colors from `Resources/Styles/Colors.xaml` by key (see `MapTheme`); keep those keys when renaming tokens.
- The app's fonts are embedded as Avalonia resources on every target by the `ConvertMauiResourcesForAvalonia` target in the project file.
- The Avalonia controls put their `using` directives inside the namespace, so Avalonia types win over MAUI's implicit usings with the same names, such as `Color` and `Animation`.

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

Stable AutomationIds include `home-sessions`, `session-search`, `session-filters`, `session-filters-clear`, `filter-topics`, `filter-check-{topic}`, `filter-reset`, `filter-show-sessions`, `save-{sessionId}`, `detail-save`, `speaker-search`, `schedule-timeline-mode`, `schedule-list-mode`, `schedule-planner`, and `saved-count` (in My Schedule's List view). DevFlow sees the planner as one element; what's inside it is drawn by Avalonia. Reusable status controls should be scoped to their page. To avoid mutation lease conflicts between short-lived CLI processes, use one batch for a sequence:

```sh
printf 'MAUI tap Tab_IMPL_sessions\nMAUI fill session-search ".NET"\nMAUI tree --depth 14\n' \
  | dotnet tool run maui -- devflow batch --agent-port PORT --delay 500
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

Skills are project-local, with source revisions recorded in each skill's `.skill-version` file. A current CLI's `maui ai status skill --env CopilotCli` inspects the inventory. Its GitHub downloads support `GITHUB_TOKEN` if unauthenticated API rate limits are reached; never store tokens in this repository.

## Structure and data

- `src/DotNetConf.App`: native pages, shared view models, theme/resources, platform assets, Debug-only DevFlow.
- `src/DotNetConf.App.Browser`: WebAssembly entry point and static page for the browser build.
- `src/DotNetConf.Core`: conference contract/validation, search/timezone/conflict logic, and atomic local bookmark storage.
- `src/DotNetConf.FluentIcons`: Fluent icon glyphs for the Avalonia targets.
- `tests/DotNetConf.Core.Tests`: contract, search, date-boundary/DST, overlap, persistence, concurrency, and failure-path tests.

Microsoft's [conference JSON](https://dotnet.microsoft.com/blob-assets/json/conf/conference-2026.json) is the sole live agenda source. It supplies the full agenda, including published abstracts, topics and biographies. The feed contains ordinal `day` numbers but no calendar dates. `conference.json` therefore declares the published conference `startDate` as event metadata, not agenda content. The adapter adds `day - 1` calendar days in America/Los_Angeles. Positive days are accepted whenever their dates are representable; there is no fixed day-count ceiling. Day tabs are derived from the downloaded session instants in the user's timezone, including local days beyond the source's Pacific dates. Invalid/ambiguous local times, non-PST timezone labels, invalid intervals and broken references are rejected.

Contract tests use the Microsoft fixture; cache tests construct records with explicitly synthetic prose. Fixture counts are not first-download baselines. Do not use private organizer issues, unpublished fields or inferred profile data. The Microsoft adapter preserves published wording while filtering explicit unfinished placeholders and the "*No response*" pseudo-speaker. Speaker photographs are never bundled; approved public URLs support background downloading and offline caching. Missing, unavailable or invalid portraits use the bundled `speaker_placeholder.png`, the exact .NET Bot image from https://www.dotnetconf.net/img/speakers/dotnet-bot.png. Preserve its artwork provenance and redistribution terms.

GitHub-hosted images are prohibited even when public: the policy blocks GitHub, its subdomains, GitHub user-content/assets/Pages domains and GitHub-named AWS buckets. It also rejects non-HTTPS and credential-bearing URLs. The same shared policy applies during parsing, profile retention, cache validation, cached-file lookup and before following each portrait redirect. The dedicated portrait HTTP client disables automatic redirects; at most four approved redirects are followed manually. A browser may hide redirect details, in which case the download fails safely to the last approved cache or Bot.

A missing or unreachable photo is not a reason to add credentials or bundled photographs. HTTP 200 alone does not establish that an image passes format and size limits.

Session timestamps retain their absolute instants. The Microsoft adapter interprets published clock times in **America/Los_Angeles**, never the device timezone, and rejects invalid or ambiguous DST wall times. Only display/day filtering converts instants to the device timezone.

Session and speaker IDs are the published Microsoft feed IDs, unchanged. There are no numeric-ID conversions, alias tables or cross-source identity matching. Bookmarks identify sessions independently of mutable display fields, and unknown bookmarks remain untouched when agenda details are unavailable.

`Resources/Raw/conference.json` holds only app-owned presentation metadata, never conference sessions, speaker profiles, community events or photo mappings. Both mock flags are false. `agendaNotice` explains the Microsoft source, first-download requirement and lack of an official community-event feed in About. `ConferenceMetadataDataSource` loads this metadata through `IConferenceDataSource`; the repository rejects metadata containing an agenda or event examples. Fictional agenda/community fixtures live only in `tests/DotNetConf.Core.Tests/Fixtures/conference.mock.json`, preserving search, overlap, topic, timezone and map-contract edge cases without shipping fictional content.

Multiple speakers and overlapping sessions are supported. Bookmarks are stored by stable ID in private app storage, independently of the agenda. Invalid data or storage failures are shown rather than silently discarded; corrupt bookmark files are never overwritten.

Session/speaker links reuse a matching detail already in the active Shell tab's navigation stack, popping back to that page rather than building circular history. New IDs still push normal details; the other tabs' histories remain independent. Native list scroll viewports span the page, while content templates retain 24-unit gutters, so scrollbars stay at the outer edge.

Sessions uses the MIT-licensed Syncfusion Toolkit `SfTabView`, registered with
`ConfigureSyncfusionToolkit()`, for equal-width, two-line local-day headers.
All days is always available. The index adapter maps to the `SelectedDay`
filter and ignores transient selection changes while local dates are rebuilt.
The page maps the dynamic Days collection to `SfTabItem.HeaderContent` with
`TabWidthMode=Default`; avoid `HeaderItemTemplate`/SizeToContent,
which skips iOS selected-header auto-scroll and clips the last day.
Search and topic filtering use Core agenda logic. Header height is measured
from scaled MAUI labels; do not replace it with a fixed height or disable font
scaling. Verify all headers are visible, native day taps, combined filters,
clear-filter selection, detail/back preservation, and larger-text themes on both
platforms when changing this surface. The toolkit license is bundled offline.

The Sessions filter page is `session-filters`, a registered,
DI-created native page sharing `ConferenceViewModel`. Choices apply immediately;
native back and Show N sessions keep them. Native CheckBoxes bind to typed topic
view models, and whole-row taps toggle the same state. Core search matches any
selected topic, then intersects the selected local day and search terms. The
same topics apply to every day tab and All days. Clear (also available beside the
main summary) removes all topics but preserves search and day. The main toolbar
binds MAUI 11 `BadgeText`, `BadgeColor`, and `BadgeTextColor`. The count is the
number of selected topics, not days, matching sessions or query terms; zero maps
to null. The page recreates the empty filter toolbar entry using shared MAUI
collection APIs to prevent iOS retaining a stale native badge; do not map
zero to an empty string, which means an indicator badge. iOS renders toolbar badges
only on iOS 26+, so retain the visible selection summary. Native toolbar labels
and hints may retain their initial values when bindings change; don't rely on
them alone to announce the active count. Do not add a custom badge renderer for
older iOS.

### Remote updates and offline boundaries

`IConferenceRepository` loads a validated, versioned JSON cache without making HTTP requests. Without a usable cache, it returns metadata with an empty agenda, then the ViewModel initiates a first download. Refresh requests only [Microsoft's conference JSON](https://dotnet.microsoft.com/blob-assets/json/conf/conference-2026.json). The website's `/api/agenda` omits stable Microsoft IDs and speaker profiles, so it is not the application's source.

The Microsoft adapter preserves source session/speaker IDs. Its day numbers and clock times are interpreted against the explicit start-date metadata in America/Los_Angeles. The adapter rejects invalid references and filters explicit placeholders; incomplete profiles retain only useful validated cached source data, with portrait fallbacks rechecked against the shared URL policy.

Agenda cache schema 8 stores only the validated Microsoft agenda, its withdrawn records and remote provenance. The cache envelope rejects unsupported schemas and unrecognized fields; there is no multi-source import or session-ID translation. Rejected caches show a visible issue and an empty download state until a valid Microsoft response replaces them. Bookmarks remain untouched. Bundled metadata cannot contain sessions, profiles or community events. An agenda that has never downloaded is genuinely empty. Cached startup makes no HTTP requests and reads bundled event metadata once; start-date metadata changes rebase Microsoft's published clock times. Invalid metadata corrections preserve the last-good cache with a visible issue. Cache persistence failures surface a storage issue without changing bookmarks. Unreferenced portrait files are pruned only once an agenda is available.

Agenda/profile data is read-only cached reference data. Bookmarks and appearance remain separate local user state and are never uploaded. Public conference data needs no secret or database encryption; if sensitive data is added later, encrypt it and protect keys with `SecureStorage`. Never embed organizer/GitHub credentials.

Cache writes use temporary files and atomic replacement. Valid remote data remains browsable in memory if persistence fails, with a visible storage warning; the preceding file is not replaced. Bad JSON, empty feeds, duplicate IDs, broken references and invalid dates cannot replace the last-good data, hash, validators or check/update timestamps. A drop of more than 25% requires two identical complete validated responses; the pending confirmation survives restart, preventing both transient truncation and indefinite rejection of legitimate changes. Exactly 25% removal is accepted immediately. A first download has no packaged count baseline. Removals are archived separately, so saved withdrawn sessions remain understandable without keeping their profiles in the current directory or deleting bookmarks. A corrupt bookmark file disables saving with a visible error but does not block agenda browsing or overwrite that file. Retry rereads unavailable local schedule data even offline and reconciles existing session save flags after recovery.

`LastUpdatedAt` tracks an accepted Microsoft payload revision, not local metadata corrections or timezone display changes. `LastCheckedAt` advances on a successful source check.

The Microsoft response advertises a one-year cache lifetime, so refresh explicitly revalidates using `Cache-Control: no-cache` and ETags. Unchanged payloads/304s retain content. Requests have bounded timeouts, size limits, cancellation and transient-only retries. Cached data renders before the silent background refresh. Home, Sessions, Speakers and My Schedule expose native pull-to-refresh without refresh toolbar items. Failure-only inline Retry actions remain available. Gesture progress is separate from background work; automatic checks are quiet when data exists, while a genuinely empty agenda shows small first-download progress. Repeat requests join an existing refresh rather than starting duplicate work. Offline/update/storage problems show a compact inline Retry message. Expected lifecycle cancellation stops progress without creating a new error notice or clearing an existing failure. In-place ViewModel/collection reconciliation preserves identities, open details, searches, topics, local-day selection and scroll offsets.

Browser fetches omit custom revalidation headers and use a unique query value to bypass browser HTTP-cache reuse while retaining the app's validated offline cache. Microsoft's feed does not grant browser CORS access; a fresh browser startup cannot download its agenda and shows an explicit failure, while validated Microsoft caches remain readable. No relay/backend is included; browser downloads require upstream CORS support or a separately designed same-origin service. A test interceptor can exercise successful responses but does not resolve the upstream restriction. Browser caches use bounded local storage (1 MiB agenda, 256 KiB per portrait, 700 KiB aggregate encoded portraits) and report storage failures. Browser builds require the pinned `wasm-tools` workload and native WASM linking for Skia/HarfBuzz.

The headless App suite references the ordinary desktop assembly without starting MAUI; it disables transitive asset/bootstrap generation only in its test host. It covers saved-state recovery online/offline, withdrawn saved details, and stop/resume cancellation races. Core tests cover source validation, dynamic calendar boundaries, repeated contraction confirmation, cache recovery, identity handling and persistence without bookmark resets.

Agenda cache files live in MAUI `FileSystem.AppDataDirectory`; portrait files use `FileSystem.CacheDirectory` and may be evicted by the OS. Photos download in background batches of three, with a 30-second batch budget, a 4 MiB download cap and a 64 MiB newest-download-first disk budget. Only static JPEG, PNG and WebP images up to 2048 pixels per axis and 1,048,576 total pixels are accepted; GIF/APNG/animated WebP are rejected before native decoding. A shared layered MAUI portrait keeps the bundled website placeholder behind the downloaded cached image, including while loading and when a native decoder returns an empty result without a failure event. There are no bundled speaker photographs or platform image handlers. Missing/unreachable images retain a cached portrait or show the placeholder. Only UI changes marshal through MAUI `MainThread`.

BCL `System.Text.Json` source generation and atomic files provide persistence without a native database. All serialization supplies generated type metadata; keep the Core AOT/trim analyzers enabled.

Reminders, local AI, and Avalonia-owned video integration remain deferred. PR build validation and manual Google Play internal-testing/TestFlight release lanes are described in [CI and release setup](docs/CI.md); production promotion remains manual and store credentials must be configured by the owner.

App-owned icons use [IconFont.Maui.FluentIcons](https://github.com/jfversluis/IconFont.Maui.FluentIcons). Website-matched Space Grotesk headings and Open Sans body fonts, gradients, Entry-based search, and heart save actions are documented in `DESIGN.md`. Adaptive pane grids protect the top and side container insets and draw through the bottom container inset, with keyboard avoidance applied as described above. On iOS, reading ScrollViews use native automatic insets and CollectionViews use an inset-aware footer so their last item remains reachable beneath the system tab material without fixed offsets. The speaker-detail root also respects the top container safe area so its header does not begin under the Duo's navigation chrome; its bottom remains edge-to-edge.

Launcher and light/dark splash screens use the website's two-robots-on-a-rocket graphic, with redistribution permission confirmed by the project owner. Android's themed launcher uses a matching monochrome silhouette. Artwork provenance and font licenses/notices are bundled under `Resources/Raw`; the artwork permission is not a general open-source license. The app follows the system theme and preserves native navigation. Its Android package name and iOS bundle ID are both `dev.jfversluis.dotnetconf`.

iOS caches launch-screen snapshots across same-version reinstalls. Increment `ApplicationVersion` when changing splash artwork; rebuilding or restarting the simulator alone may still show a cached image. Install the new build rather than deleting the app and its saved schedule.

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

The schema-version-1 contract has an optional `communityEvents` list that defaults
to empty. Each event requires a stable `id`, `name`, `kind`
(`watch-party` or `community-conference`), `venue`, `city`, `country`, finite
`latitude`/`longitude` within geographic bounds, and an absolute HTTPS `websiteUrl`.
No community events are bundled or supplied by the connected Microsoft agenda feed.
The empty list links to the official website and does not construct a map.
Synthetic events exist only in the Core test fixture, with `example.com`
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

Configured Android map tiles and marker interactions require an owner-supplied
key. Verify world-spanning and date-line datasets when an official event feed
becomes available; the synthetic regional fixture is not a complete map-data
acceptance test.

## Verification and release gates

Run the Core and App tests and the relevant platform build after changes. Keep
screenshots, logs and validation reports outside the repository; this guide
describes the app and contributor workflow, not a chronological test report.

- Exercise cold startup without agenda data, cached startup, offline browsing, first download, refresh, invalid updates, corrupt-cache recovery and storage failures. Confirm saved choices survive and no fictional data appears.
- Check all five tabs, speaker/session details and native back navigation, combined search/day/topic filters, clear/no-match states, independent heart actions, overlap notices and saved-count consistency across restart.
- Verify light/dark appearance, larger text, narrow viewports, safe areas, portrait fallbacks and last-item reachability on mobile. About's agenda-update action provides a keyboard- and screen-reader-accessible alternative to pull-to-refresh.
- On Linux, verify required libraries and timezone data, persistence and website launching. In the browser, verify the real CORS failure separately from controlled successful responses, bounded storage, reload persistence and visible failures. The browser is not an offline-installable PWA, and neither target is a substitute for mobile release validation.

Physical-device execution, VoiceOver/TalkBack, minimum-supported-OS behavior,
folding transitions, configured Android maps and signed NativeAOT runtime checks
remain release gates. Simulator/emulator builds and unsigned cross-publishes do
not establish store-signing or store-approval readiness.
