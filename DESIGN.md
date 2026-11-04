# Native .NET Conf design

The official website's purple/magenta identity is adapted to an agenda-first, native mobile interface. Native Shell chrome, back behavior, and controls remain platform-owned. `PRODUCT.md` records scope and product truth.

## Theme

`Resources/Styles/Colors.xaml` is the token source. The Avalonia-drawn schedule planner reads the same tokens by key in `SchedulePlannerHandler`. Match the website's purple `#512BD4`, midnight `#190649`, magenta `#D600AA`, and lavender `#B9AAEE`. Light surfaces are neutral (`#FAFAFA` background, `#333333` text); dark surfaces use the website's charcoal (`#1F1F1F` background, `#E5E5E5` text), not a purple wash. Native interaction tint is purple/lavender.

Home has an expressive, full-width lavender-to-pink wash (midnight-to-magenta in dark appearance), Space Grotesk display type, and a short purple-to-magenta rule. The website's dark accent gradient is `#9780E5` to `#DE33BB`. Speaker portraits use quiet rounded surfaces; list bodies stay quiet. Text uses contrast-safe solid colors, not gradient lettering. Use `AppThemeBinding` for semantic light/dark pairs.

The app defaults to system appearance; Settings offers a persisted System/Light/Dark choice. Status-bar icon contrast follows the app theme. Android opts into MAUI 11 RC2's native Material 3 controls with `UseMaterial3=true`. The website's two-robot rocket provides the launcher and light/dark splash artwork. A lavender launcher background separates the purple rocket from its surroundings. The launcher artwork is rendered at width 340 on its 456-unit canvas; iOS uses an additional MAUI `ForegroundScale` of 1.15. Android keeps the unscaled foreground to fit its circular adaptive-icon mask. The monochrome launcher layer uses the same silhouette.

## Typography and layout

Use the website's actual fonts: bundled SIL-OFL Space Grotesk Bold for display/heading roles and Apache-licensed Open Sans Regular/Semibold for body, controls, and session titles. Roles in `Styles.xaml`: body 16, section heading 21, heading 28; Home display 44. All text scales. Compact metadata uses 12-14. Page padding is 24; reading pages have a 720-unit maximum content width. Sections use generous space and separators rather than card grids.

Session and speaker lists use full-width virtualized CollectionView, with 24-unit gutters inside headers and item templates rather than margins on the scroll viewport. Native scroll indicators therefore sit at the screen edge without moving the content or overriding platform indicator insets. Heading, search, day tabs, and the topic summary remain outside the result list when space permits. In short windows, stacked panes, or at larger text sizes with constrained heights, the decorative heading gives way to the list and the session day/topic controls move into its collection header; metadata always scrolls with the sessions. Search stays pinned outside the virtualized collection so it remains focused while results change. Entire session/speaker content regions are native accessible navigation buttons with descriptive semantics; there are no separate Details buttons. Session titles have no floating chevron competing with the heart or narrowing wrapped titles; the compact speaker directory retains its trailing chevron. Session hearts occupy a separate, non-overlapping 48-unit target. Outline means not saved; filled means saved. Spoken descriptions include state and action; persistence commits before the icon changes.

In a master/detail layout, the selected list row uses an inset, rounded themed surface with a slim leading marker. Its content stays aligned to the 24-unit header gutter, and its navigation button announces the selected state. Compact and embedded detail lists do not show this selection treatment.

Successful heart actions show a short Community Toolkit Toast ("Added to My Schedule" / "Removed from My Schedule") after persistence and schedule refresh. Confirmations do not occupy inline space or shift the agenda. The heart and count remain the persistent, accessible state indicators. Blocking operation errors use explicitly dismissed native dialogs. Successful background agenda updates are invisible: no success card, timestamp, or progress panel. Home and the agenda, speaker and saved-session lists support native pull-to-refresh without refresh toolbar items. Only user-requested updates show the native refresh indicator. Offline/update/storage failures use a compact, unboxed inline message with a 48-unit Retry action. The existing agenda stays usable, including while refreshing. No community-event examples are bundled.

`AgendaSearchField` wraps a real Entry in a neutral outlined surface, with magnifier, live query binding, a 48-unit clear action, and a themed focus outline. Toolkit `UserStoppedTypingBehavior` applies the latest query after 300 ms of inactivity, including empty queries, with automatic keyboard dismissal disabled. Clear applies immediately and keeps focus; the Search keyboard action applies immediately then dismisses it. Filtered results reconcile observable collections by stable identity without clearing them; identical results cause no update. Existing session/speaker ViewModels and open details receive remote edits in place. Only the `SearchEntry` subclass has its native border removed through MAUI's handler customization. Android uses `EntryHandler2`: its Material wrapper outline, floating hint, padding, and background are disabled, and the inner native edit field owns the placeholder and its themed color. Android search accessibility uses that placeholder rather than `SemanticProperties.Description`, preserving TalkBack editing actions; iOS retains its descriptive label and existing border customization.

When a focused landscape search would leave less than 120 units for results, a compact, headerless result list and a shorter error/Retry indicator take priority over navigation chrome and decorative headings. On Android, short landscape windows prepare this layout and hide both navigation and tab bars before the software keyboard opens, avoiding a transient loss of focus; their prior visibility is restored after search. Android Back dismisses the keyboard and restores the normal layout. The Android search field requests an inline keyboard while retaining its Search action, although individual keyboards may ignore that request. Session rows show the title and time; search, saving, selection, and honest empty states remain available. Day/topic controls return when search loses focus, while their selections stay applied. A reported hardware keyboard that leaves enough result space retains the normal layout.

Day navigation uses the MIT-licensed Syncfusion Toolkit `SfTabView`: All days followed by device-local dates, with equal-width `SfTabItem` headers when they fit and scrolling headers when they do not. Each header has two centered lines (weekday / month and date, or All / days), Open Sans Semibold, and a purple/lavender underline. Bar height follows MAUI's measured, font-scaled label height rather than clipping larger text. Tabs combine with the topic filter and remain available except during the constrained compact search described above. Native detail/back navigation preserves the chosen local date.

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

On iOS, pages use `SafeAreaEdges=None` and scrolling reading pages retain `ScrollView.SafeAreaEdges=Default` (native automatic content insets). Agenda screens consume horizontal container edges once at their root layout (`Container,None,Container,None`), keeping notices and content clear of the Duo's trailing native tab chrome without hard-coded widths or changing vertical scrolling. The adaptive pane grids protect their top and side container insets and draw through the bottom container inset. On iOS their bottom edge requests `SoftInput` so the panes resize above the keyboard. Shell's tab background is transparent so the platform-owned material can show scrolling content beneath it. The pinned MAUI iOS CollectionView explicitly disables automatic content insets in `ItemsViewController2.ViewDidLoad`; `SafeAreaFooter` therefore uses the public MAUI page `SafeAreaInsets.Bottom`, observing inset changes, to keep the last item reachable. There is no bottom viewport margin, fixed tab-bar padding, negative margin, or custom renderer. Android pages use `SafeAreaEdges=SoftInput` and resize input mode for keyboard avoidance; the adaptive pane grids use `None` at the bottom to avoid applying that keyboard inset a second time. When compact search hides Shell chrome, the panes add only any remaining measured IME overlap and clear it when search ends. Android retains an opaque native tab surface and a zero-height collection footer. System gesture protection is retained where controls meet the screen edge, not as a blank inset around every page.

My Schedule keeps the heading, view switch and timezone in a content-sized header. Topic filters have a 16-unit top inset inside the planner gradient. Native Shell owns safe areas; the embedded Avalonia host does not apply window insets again. The timeline fills its viewport, with 16 units of scroll-content padding plus the iOS page's bottom inset. Android's opaque tab bar needs no extra inset. Native-bar clearance stays in device points when text is scaled.

## Icons and navigation

Use only `IconFont.Maui.FluentIcons` for app-owned UI icons. `Icons.xaml` holds typed Regular/Filled glyph resources with theme-aware colors. iOS and Android use the actual NuGet library. The small `DotNetConf.FluentIcons` project is an Avalonia-only compatibility shim: version 1.1.1 has no plain .NET/browser assembly, so those targets otherwise cannot resolve its typed glyphs. Search uses the same icon family; back controls remain native. Launcher and splash icons are image assets, not font glyphs. Bundled website artwork is redistributed with the project owner's confirmed permission. Preserve its provenance notice; this is not a general open-source artwork license.

Five Shell destinations: Home, Sessions, Speakers, My Schedule, Community. Android's compact tab label is "Schedule" to avoid truncation; the page is still My Schedule. Sessions and speakers use ID-based details. In the Sessions and Speakers tabs, selections and cross-links replace the current detail, with no pane Back or Close; compact Back returns directly to the list, and expanding restores the latest selection. Elsewhere, a detail link returns to a matching page in the active tab's navigation stack, preserving that page and its scroll position; otherwise it pushes a new page. Links to the current detail are no-ops, and navigation is serialized across session/speaker commands. Other tabs' histories are not searched or disturbed. My Schedule displays a live saved-session badge; zero hides it. Clearing the topic count also recreates the filter toolbar entry through shared MAUI APIs, because iOS can retain a stale native badge after its bound value becomes null. Counts and saved state are also visible text, never badge-only information.

## Content and states

First-run onboarding is a separate full-screen modal carousel without a
navigation bar: welcome, personal schedule introduction, and optional session
reminders. Space Grotesk headings, typed Fluent icons and short explanatory
copy focus each step on one task. Swipe and persistent Next/Back controls move
between steps; Skip setup sits alongside each hero icon.
The middle step mounts the shared discovery view immediately and checks local-model
readiness before showing either discovery path. A ready model opens the AI editor,
without mix/shuffle controls. Otherwise it offers up to six compatible, varied random
sessions from the downloaded agenda, without AI controls. A failed readiness retry
preserves the reviewed random preview; a successful retry switches to AI only.
No cached agenda means an
honest download state, never bundled session examples. Continue does not save:
advancing prompts if nothing was added or checked suggestions remain unsaved.
All three steps share a 34-unit heading, 48-unit Fluent icon, and the
incumbent hero wash, with 24-unit gutters aligned to the builder.
Random discovery uses the typed Fluent agenda glyph; AI mode uses a sparkle.
The welcome's agenda and reminder's bell remain.
On iOS and Android the onboarding hero background extends behind the status bar.
A root MAUI `SafeAreaEdges` layout protects the carousel content; a separate
background layer paints behind the protected region. No native inset measurement
or platform-specific hero padding is needed. Bottom protection remains page-owned,
and the regular in-app builder retains native scroll insets.
Supporting copy returns to the neutral reading surface. Skip setup sits beside
each onboarding hero icon, directly on the gradient without a separate bar.
The hero content follows the actual top safe-area inset. The footer is a
single compact row: quiet Back, progress dots/count, and a content-width Next
button (Done on the final step). There is no full-width Continue action.
Action columns size to their text so large fonts do not split short labels.
Both action slots reserve matching widths, keeping progress centered even when
Back is hidden on the first slide.
Idle reminder status is hidden; permission
results and recovery remain visible. Labels wrap and content scrolls at larger
text sizes without disabling font scaling.
Completion and skipping persist locally only after successful modal dismissal;
failed dismissal leaves setup retryable, while an interrupted launch remains
eligible. Reminder subscriptions attach/detach with page visibility.
Android back moves to the previous step or dismisses from welcome.
My Schedule opens the regular native planner, which shares the builder view
but has no onboarding Skip or explore actions.
Horizontally scrolling example chips above the input fill the editor without
generating or saving. A small spinner replaces the strip during loading, and an
atomic list replacement reveals the whole batch together. The local model
asynchronously selects three to five distinct, real agenda topics per planner
instance from a shuffled subset of agenda topics. This encourages variety, not
guaranteed uniqueness. Playful topic labels retain literal topic requests;
a randomly chosen, unambiguously named scheduled speaker gets a "Team [name]"
chip that requests all their sessions.
Invalid output, cancellation or timeout keeps the curated options with explicit
status rather than blocking input. Provider-specific local
model availability, privacy boundaries, and the agenda's data source are stated
explicitly. Suggestions appear progressively as a reviewable shortlist with
native checkboxes. Selected, alternative, blocked, and already-saved states are
labelled, with overlap explanations beside affected rows. A live selected count
and Add N action explain that checking is not saving; no redundant confirmation
checkbox is required. Matching details are collapsed behind a text action, not an
AI-debug section ahead of the sessions. Warnings and blocking validation stay
visible; successful validation and idle-ready boilerplate do not compete with
the task. Shortlist rows keep title and speaker prominent, followed by one
compact line of local start date/time and duration, without per-talk matching
explanations. The accessible checkbox description retains the end date/time.
Watching hours sit in a single summary row with a Change action; native clock
pickers and guidance expand only when needed. Any assumed time boundary stays
visible above the shortlist as a short factual note. Users review the selected
sessions and times before confirming.
After saving, View My Schedule provides the next step. The same semantic theme
tokens, scalable text, 48-unit actions, and typed Fluent icons apply throughout.
The planner editor has an explicit iOS accessibility label; Android uses matching
placeholder/hint text rather than a description, preserving TalkBack editing actions.
Shortlist checkboxes expose the session title, speakers, time and any blocking
warning. User-initiated planning and save outcomes are announced once, including
blocking validation; readiness and individual selection changes are not repeatedly
announced. Announcements are subscribed only while the view is loaded and gated
on its active page/slide, so leaving during inference cannot announce over the
reminder step. Saving freezes the shortlist and confirmation, persists a snapshot
of the confirmed selection, and reports a reminder-refresh failure separately
from a failed save.

My Schedule's quiet, full-width Fluent agenda action sits after the timezone
and beside the existing Timeline/List workflow. Empty schedules open the personal
builder; saved schedules open gap planning with "Make room for discovery."
The existing scrollable builder, shortlist and confirmation controls are reused.
Free time is bounded by the real agenda on saved-session days, not assumed office
hours. Optional topics, speakers and clock limits narrow those slots. Retained
withdrawn bookmarks reserve their last-known times until removed; they are never
proposals. Changed bookmarks or agenda times invalidate confirmation. Model
readiness uses Essentials.AI's native status APIs; unsupported devices retain
random discovery, and no model download or cloud request is initiated.

The reminder step explains the benefit before asking for OS notification
permission, only on an explicit Enable tap. Reminders are device-local and tied
to saved session starts; no push backend or registration is involved. Denial
has a visible recovery state and never blocks completion. Settings offers
enable/retry, disable and system-settings controls. Saved-session edits and app
activation reconcile owned pending notifications without touching unrelated
requests. A nearest-60 budget stays below Apple's pending-notification limit;
OS policy still controls delivery. Fictional content remains labeled in alerts.

Sessions keeps search clearing in the Entry and topic clearing beside the summary
and in Filters. Clearing topics leaves day selection and search unchanged.
Neither changes saved sessions. A zero-match result keeps search reachable; day
tabs and the toolbar filter action return after a constrained compact search.

Community is a Shell tab; Home's Fluent settings gear opens Settings.
Settings includes appearance, device-time-zone information, and an About action
preserving access to disclosure and app information through native back navigation.
Community's overview leads with a restrained branded introduction, gathering
formats, and an official-website link. No community events or venues are bundled,
and no official event feed is connected yet. The primary action says Browse
community events and opens an honest empty list with an official-website action,
not an empty map. Editorial sections explain how to start a local
gathering; there are no invented community statistics or organizer workflows.
The map/list implementation remains available for a future official event source.
The pushed explorer hides tabs with MAUI Shell, retains native back navigation,
and gives the map the full available width. A content-sized opaque legend
wraps its labeled pin types without
a fixed-height scrolling panel. Gesture instructions remain available as the
map's semantic hint. Show all / Event list occupy a separate bottom action row,
not a floating overlay over provider zoom controls or attribution. The action
surface fills the bottom edge while container insets keep its buttons above
system gestures on both platforms. A separate safe-area-aware layer keeps
the legend, list, and preview clear of system chrome.
Unused overlay space does not intercept map gestures. Native counted clusters
keep dense locations legible. White-outlined teardrop pins use typed Fluent video and
presenter symbols to distinguish purple watch parties from deep-magenta in-person
talks. A matching text legend explains both types without relying on color.
Fictional map locations exist only in test fixtures, not the app. Available event
locations would be framed initially; “Show all”
restores that view. Pin taps select details without forced recentering, while
list selections focus their location for a subsequent map switch. A themed,
dismissible preview exposes the event name, kind, and city. Event details opens
a native reading page with the venue and website;
back preserves the explorer's viewport and selection. Preview height
is bounded relative to the viewport and scrolls for larger text. The list supplies
a non-map selection path, also directly reachable from the overview.
Unconfigured Android builds omit the map switch and show an honest availability
statement alongside the usable list, not API-key setup instructions.

About discloses the Microsoft agenda source and lack of an official community-event feed through `AgendaNotice`. Both mock flags are false; no session, profile or community-event dataset ships in the app. Startup immediately displays a validated local cache, when available, then refreshes Microsoft's agenda without announcing success. A fresh install shows small first-download progress; without a connection it explains how to download the agenda and preserves saved choices. Inline notices appear only when connectivity, updates or local storage need attention; unrelated About and Community screens carry no agenda status. Saved sessions withdrawn from the feed remain in My Schedule with an explicit explanation. Corrupt bookmarks are preserved, saving is disabled, and agenda browsing remains available.

Speaker rows and detail pages show an approved downloaded cached portrait or the exact bundled .NET Bot placeholder from the website. Speaker photographs are not packaged with the app; GitHub-hosted photos are rejected, including redirects and cached references. Bounded background downloads replace portraits in place; unavailable or invalid images retain the placeholder, including on a first offline launch. Agenda and biographies come only from the public Microsoft feed, never private organizer enrichment. Unpublished bios retain the "Biography to be announced." state. Topic filters explain when no topics have been published. Loading, empty results, no bookmarks, and invalid IDs retain their content states. Overlaps are persistent session attributes, not notification banners or restrictions. Optional showcases must not replace these core affordances.

About's Third-party dependencies page uses simple, full-width reading rows with
names and license/version details. Tapping a row opens a bundled, offline license;
the final action exposes all notices and artwork credits. Native back navigation
preserves the reading stack.

## Release review requirements

Verify discoverable navigation, independent heart tap behavior, spoken saved
state, one-tap search clearing, large-text reachability, and last-item visibility
above native bars. Exercise speaker/session navigation, retained search focus,
safe areas and both appearances on the supported mobile targets.

Real VoiceOver/TalkBack user testing remains a release requirement.
