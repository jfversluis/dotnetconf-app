# Native .NET Conf design

The official website's purple/magenta identity is adapted to an agenda-first, native mobile interface. Native Shell chrome, back behavior, and controls remain platform-owned. `PRODUCT.md` records scope and product truth.

## Theme

`Resources/Styles/Colors.xaml` is the token source. Match the website's purple `#512BD4`, midnight `#190649`, magenta `#D600AA`, and lavender `#B9AAEE`. Light surfaces are neutral (`#FAFAFA` background, `#333333` text); dark surfaces use the website's charcoal (`#1F1F1F` background, `#E5E5E5` text), not a purple wash. Native interaction tint is purple/lavender.

Home has an expressive, full-width lavender-to-pink wash (midnight-to-magenta in dark appearance), Space Grotesk display type, and a short purple-to-magenta rule. The website's dark accent gradient is `#9780E5` to `#DE33BB`. Speaker initials reuse the accent gradient; list bodies stay quiet. Text uses contrast-safe solid colors, not gradient lettering. Use `AppThemeBinding` for semantic light/dark pairs.

The app defaults to system appearance; Settings offers a persisted System/Light/Dark choice. Status-bar icon contrast follows the app theme. Android opts into MAUI 11 RC1's native Material 3 controls with `UseMaterial3=true`. The website's two-robot rocket provides the launcher and light/dark splash artwork. A lavender launcher background separates the purple rocket from its surroundings. The launcher artwork is rendered at width 340 on its 456-unit canvas (up from 292); iOS uses an additional MAUI `ForegroundScale` of 1.15. Android keeps the unscaled foreground to fit its circular adaptive-icon mask. The monochrome launcher layer uses the same enlarged silhouette. Splash artwork is unchanged.

## Typography and layout

Use the website's actual fonts: bundled SIL-OFL Space Grotesk Bold for display/heading roles and Apache-licensed Open Sans Regular/Semibold for body, controls, and session titles. Roles in `Styles.xaml`: body 16, section heading 21, heading 28; Home display 44. All text scales. Compact metadata uses 12-14. Page padding is 24; reading pages have a 720-unit maximum content width. Sections use generous space and separators rather than card grids.

Session and speaker lists use full-width virtualized CollectionView, with 24-unit gutters inside headers and item templates rather than margins on the scroll viewport. Native scroll indicators therefore sit at the screen edge without moving the content or overriding platform indicator insets. Heading, search, day tabs, and the topic summary remain outside the result list so filtering never recreates the focused Entry and day navigation stays reachable; metadata scrolls with the sessions. Entire session/speaker content regions are native accessible navigation buttons with descriptive semantics; there are no separate Details buttons. Session titles have no floating chevron competing with the heart or narrowing wrapped titles; the compact speaker directory retains its trailing chevron. Session hearts occupy a separate, non-overlapping 48-unit target. Outline means not saved; filled means saved. Spoken descriptions include state and action; persistence commits before the icon changes.

Successful heart actions show a short Community Toolkit Toast ("Added to My Schedule" / "Removed from My Schedule") after persistence and schedule refresh. Confirmations do not occupy inline space or shift the agenda. The heart and count remain the persistent, accessible state indicators. Errors use native, explicitly dismissed dialogs, never inline banners or disappearing toasts. Agenda-loading failures offer Retry / Not now; other failures explain recovery with an OK action. Retry happens after releasing the load gate. The sample-data disclosure lives in About, not a repeated banner.

`AgendaSearchField` wraps a real Entry in a neutral outlined surface, with magnifier, live query binding, a 48-unit clear action, and a themed focus outline. Toolkit `UserStoppedTypingBehavior` applies the latest query after 300 ms of inactivity, including empty queries, with automatic keyboard dismissal disabled. Clear applies immediately and keeps focus; the Search keyboard action applies immediately then dismisses it. Filtered results update in one snapshot rather than Clear/Add animations; identical results cause no update. Only the `SearchEntry` subclass has its native border removed through MAUI's handler customization. Android uses `EntryHandler2`: its Material wrapper outline, floating hint, padding, and background are disabled, and the inner native edit field owns the placeholder and its themed color. Android search accessibility uses that placeholder rather than `SemanticProperties.Description`, preserving TalkBack editing actions; iOS retains its descriptive label and existing border customization.

Day navigation uses the MIT-licensed Syncfusion Toolkit `SfTabView`: All days followed by device-local dates, evenly distributed without scrolling using `SfTabItem` headers and `TabWidthMode=Default`. Each header has two centered lines (weekday / month and date, or All / days), Open Sans Semibold, and a purple/lavender underline. Bar height follows MAUI's measured, font-scaled label height rather than clipping larger text. Tabs stay visible during search and combine with the topic filter. Native detail/back navigation preserves the chosen local date.

Sessions' native Fluent filter toolbar item opens a pushed Filters page, outside
the result list. Topic choices are full-width, multi-select rows with native
checkboxes and a subtle selected surface. Sessions matching any chosen topic
appear on every day, including All days; day selection lives only in the tabs.
No topic selection means all topics. Changes apply immediately,
and Show N sessions returns to the same Sessions page. Native back keeps the
chosen filters too. Clear removes all topic choices without changing the selected
day or search. A small, borderless Clear action beside the persistent compact
summary beneath the day tabs exposes a one-tap reset without reopening Filters.
The summary exposes current
choices without repeating a picker in the scrolling agenda.

The toolbar uses MAUI 11's native `ToolbarItem.BadgeText`, counting selected
topics, not days, search terms, or results. Zero hides the badge. Its semantic
hint identifies the filter action; the visible summary is also available on
pre-iOS-26 devices where native toolbar badges are unsupported. Badge colors
use purple/white in light appearance and lavender/midnight in dark appearance.

On iOS, pages use `SafeAreaEdges=None`, scrolling reading pages retain `ScrollView.SafeAreaEdges=Default` (native automatic content insets), and their content layouts do not reintroduce container insets. Shell's tab background is transparent so the platform-owned material can show scrolling content beneath it. The pinned MAUI iOS CollectionView explicitly disables automatic content insets in `ItemsViewController2.ViewDidLoad`; `SafeAreaFooter` therefore uses the public MAUI page `SafeAreaInsets.Bottom`, observing inset changes, to keep the last item reachable. There is no bottom viewport margin, fixed tab-bar padding, negative margin, or custom renderer. Android uses page `SafeAreaEdges=All` and an opaque native tab surface; its footer has zero height.

## Icons and navigation

Use only `IconFont.Maui.FluentIcons` for app-owned UI icons. `Icons.xaml` holds typed Regular/Filled glyph resources with theme-aware colors. Search uses the same icon family; back controls remain native. Launcher and splash icons are image assets, not font glyphs. Permission to redistribute the official two-robots-on-a-rocket graphic was confirmed by the project owner, a Microsoft employee, on October 7, 2026. Preserve its provenance notice; this is not a general open-source artwork license.

Five Shell destinations: Home, Sessions, Speakers, My Schedule, Community. Android's compact tab label is "Schedule" to avoid truncation; the page is still My Schedule. Sessions and speakers use ID-based details. A detail link returns to the matching page already in the active tab's navigation stack, preserving that page and its scroll position; otherwise it pushes a new page. Links to the current detail are no-ops, and navigation is serialized across session/speaker commands. Other tabs' histories are not searched or disturbed. My Schedule displays a live saved-session badge; zero hides it. Counts and saved state are also visible text, never badge-only information.

## Content and states

Sessions keeps search clearing in the Entry and topic clearing beside the summary
and in Filters. Clearing topics leaves day selection and search unchanged.
Neither changes saved sessions. A zero-match result still leaves search, day
tabs, and the toolbar filter action reachable.

Community replaces the About tab; Home's Fluent settings gear opens Settings.
Settings includes appearance, device-time-zone information, and an About action
preserving access to disclosure and app information through native back navigation.
Community's overview leads with a restrained branded introduction, gathering
formats, an honest fictional-data disclosure, and one primary Explore event map
action. Editorial sections explain the accessible list alternative and how to
start a local gathering; no invented community statistics or organizer workflow.
List-only builds label the primary action Browse community events instead of
promising a map. Fictional event website actions explicitly say Open example website.
The pushed explorer hides tabs with MAUI Shell, retains native back navigation,
and gives the map the full available width and height. A compact opaque legend
and bottom Show all / Event list controls leave the provider attribution visible.
On iOS, the map background extends through the bottom safe area; a separate
safe-area-aware layer keeps the legend, controls, list, and preview above system chrome.
Unused overlay space does not intercept map gestures. Native counted clusters
keep dense locations legible. White-outlined teardrop pins use typed Fluent video and
presenter symbols to distinguish purple watch parties from deep-magenta in-person
talks. A matching text legend explains both types without relying on color.
The 24 fictional locations include four dense, mixed-type city hubs and seven
standalone city examples. All event locations are framed initially; “Show all”
restores that view. Pin taps select details without forced recentering, while
list selections focus their location for a subsequent map switch. A themed,
dismissible preview exposes the event name, kind, and city. Event details opens
a native reading page with the venue, website, and explicit fiction disclosure;
back preserves the explorer's viewport and selection. Preview and legend heights
are bounded relative to the viewport and scroll for larger text. The list supplies
a non-map selection path, also directly reachable from the overview.
Unconfigured Android builds omit the map switch and show an honest availability
statement alongside the usable list, not API-key setup instructions.

About discloses fictional sample data while `IsMock` is true. Fictional people use initials instead of implied real photographs. Loading, empty results, no bookmarks, and invalid IDs retain their content states; operation errors are presented in native dialogs. Overlaps are persistent session attributes, not notification banners or restrictions. Optional showcases must not replace these core affordances.

About's Third-party dependencies page uses simple, full-width reading rows with
names and license/version details. Tapping a row opens a bundled, offline license;
the final action exposes all notices and artwork credits. Native back navigation
preserves the reading stack.

## Review basis

This refinement used separate model consultations as UX expert, UI art director, and a simulated panel (busy attendee, first-time user, low-vision/screen-reader user). These are design heuristics, not empirical usability testing. Shared acceptance criteria: discoverable navigation, independent heart tap behavior, spoken saved state, one-tap search clearing, large-text reachability, and last-item visibility above native bars.

Rendered-screen review accepted the visual direction and identified Android's truncated tab label (shortened to "Schedule") and the need for scaled-text checks. Live inspection confirmed independent heart actions, cleared query/focus, speaker/session navigation, under-bar content, and measured last-item clearance. Real VoiceOver/TalkBack user testing remains a release requirement.
