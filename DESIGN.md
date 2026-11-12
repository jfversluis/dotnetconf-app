# Native .NET Conf design

The official website's purple/magenta identity is adapted to an agenda-first, native mobile interface. Native Shell chrome, back behavior, and controls remain platform-owned. `PRODUCT.md` records scope and product truth.

## Theme

`Resources/Styles/Colors.xaml` is the token source. The Avalonia-drawn schedule planner reads the same tokens by key in `SchedulePlannerHandler`. Match the website's purple `#512BD4`, midnight `#190649`, magenta `#D600AA`, and lavender `#B9AAEE`. Light surfaces are neutral (`#FAFAFA` background, `#333333` text); dark surfaces use the website's charcoal (`#1F1F1F` background, `#E5E5E5` text), not a purple wash. Native interaction tint is purple/lavender.

Home has an expressive, full-width lavender-to-pink wash (midnight-to-magenta in dark appearance), Space Grotesk display type, and a short purple-to-magenta rule. The website's dark accent gradient is `#9780E5` to `#DE33BB`. Speaker portraits use quiet rounded surfaces; list bodies stay quiet. Text uses contrast-safe solid colors, not gradient lettering. Use `AppThemeBinding` for semantic light/dark pairs.

The app defaults to system appearance; Settings offers a persisted System/Light/Dark choice. Status-bar icon contrast follows the app theme. Android opts into MAUI 11's native Material 3 controls with `UseMaterial3=true`. The website's two-robot rocket provides the launcher and light/dark splash artwork. A lavender launcher background separates the purple rocket from its surroundings. The launcher artwork is rendered at width 340 on its 456-unit canvas; iOS uses an additional MAUI `ForegroundScale` of 1.15. Android keeps the unscaled foreground to fit its circular adaptive-icon mask. The monochrome launcher layer uses the same silhouette.

## Typography and layout

Use the website's actual fonts: bundled SIL-OFL Space Grotesk Bold for display/heading roles and Apache-licensed Open Sans Regular/Semibold for body, controls, and session titles. Roles in `Styles.xaml`: body 16, section heading 21, heading 28; Home display 40. All text scales. Compact metadata uses 12-14. Page padding is 24; reading pages have a 720-unit maximum content width. Sections use generous space and separators rather than card grids.

Session and speaker lists use full-width virtualized CollectionView, with 24-unit gutters inside headers and item templates rather than margins on the scroll viewport. Native scroll indicators therefore sit at the screen edge without moving the content or overriding platform indicator insets. Heading, search, day tabs, and the topic summary remain outside the result list when space permits. In short windows, stacked panes, or at larger text sizes with constrained heights, the decorative heading gives way to the list and the session day/topic controls move into its collection header; metadata always scrolls with the sessions. Search stays pinned outside the virtualized collection so it remains focused while results change. Entire session/speaker content regions are native accessible navigation buttons with descriptive semantics; there are no separate Details buttons. Session titles have no floating chevron competing with the heart or narrowing wrapped titles; the compact speaker directory retains its trailing chevron. Session hearts occupy a separate, non-overlapping 48-unit target. Outline means not saved; filled means saved. Spoken descriptions include state and action; persistence commits before the icon changes.

In a master/detail layout, the selected list row uses an inset, rounded themed surface with a slim leading marker. Its content stays aligned to the 24-unit header gutter, and its navigation button announces the selected state. Compact and embedded detail lists do not show this selection treatment.

Native session swipe actions use custom `SwipeItemView` tiles: each revealed
action occupies 96 units with a vertically centered, rounded action on the selected-surface
token, a 22-unit Fluent glyph, and a short font-scaled caption. The accent
stays inside the action rather than filling the row height. More actions,
long press, and the detail page retain the same watched-state command.
Swipe right executes Save/Remove when the full-swipe threshold is crossed and
released. Its purple underlay spans the row width and fills the uncovered area.
A white Fluent heart and font-scaled caption explain "Swipe to save/remove",
then "Release to save/remove" at 60% of the row width, the explicit native
execution threshold. Crossing that threshold gives a short heart-scale pulse
and a supported-device haptic tick; reversing disarms it. The pulse respects
the shared platform Reduce Motion check. A short swipe cannot execute it.
Swipe left reveals More and the watched toggle. More opens the existing
save/watched/share menu. Native rows keep only the heart visible,
giving titles more room. Linux and browser rows retain their visible More
button because they do not use native swipe handlers.
The moving foreground is opaque so the action is uncovered only as the row
slides away, never visible through its text or controls. Its background matches
the selected surface only in lists that display that selection treatment.

Successful heart actions show a short Community Toolkit Toast ("Added to My Schedule" / "Removed from My Schedule") after persistence and schedule refresh. Confirmations do not occupy inline space or shift the agenda. The heart and count remain the persistent, accessible state indicators. Blocking operation errors use explicitly dismissed native dialogs. Successful background agenda updates are invisible: no success card, timestamp, or progress panel. Home and the agenda, speaker and saved-session lists support native pull-to-refresh without refresh toolbar items. Only user-requested updates show the native refresh indicator. Offline/update/storage failures use a compact, unboxed inline message with a 48-unit Retry action. The existing agenda stays usable, including while refreshing. No community-event examples are bundled.

`AgendaSearchField` wraps a real Entry in a neutral outlined surface, with magnifier, live query binding, a 48-unit clear action, and a themed focus outline. Toolkit `UserStoppedTypingBehavior` applies the latest query after 300 ms of inactivity, including empty queries, with automatic keyboard dismissal disabled. Clear applies immediately and keeps focus; the Search keyboard action applies immediately then dismisses it. Filtered results reconcile observable collections by stable identity without clearing them; identical results cause no update. Existing session/speaker ViewModels and open details receive remote edits in place. Only the `SearchEntry` subclass has its native border removed through MAUI's handler customization. Android uses `EntryHandler2`: its Material wrapper outline, floating hint, padding, and background are disabled, and the inner native edit field owns the placeholder and its themed color. Android search accessibility uses that placeholder rather than `SemanticProperties.Description`, preserving TalkBack editing actions; iOS retains its descriptive label and existing border customization.

When a focused landscape search would leave less than 120 units for results, a compact, headerless result list and a short error with Details and Retry take priority over navigation chrome and decorative headings. On Android, short landscape windows prepare this layout and hide both navigation and tab bars before the software keyboard opens, avoiding a transient loss of focus; their prior visibility is restored after search. Android Back dismisses the keyboard and restores the normal layout. The Android search field requests an inline keyboard while retaining its Search action, although individual keyboards may ignore that request. Session rows show the title and time; search, saving, selection, and honest empty states remain available. Day/topic controls return when search loses focus, while their selections stay applied. A reported hardware keyboard that leaves enough result space retains the normal layout.

Day navigation uses the MIT-licensed Syncfusion Toolkit `SfTabView`: All days followed by device-local dates, with equal-width `SfTabItem` headers when they fit and scrolling headers when they do not. Each header has two centered lines (weekday / month and date, or All / days), Open Sans Semibold, and a purple/lavender underline. Bar height follows MAUI's measured, font-scaled label height rather than clipping larger text. Tabs combine with the topic filter and remain available except during the constrained compact search described above. Native detail/back navigation preserves the chosen local date.

Sessions' native Fluent filter toolbar item opens a pushed Filters page, outside
the result list. Topic choices are full-width, multi-select rows with native
checkboxes and a subtle selected surface. Sessions matching any chosen topic
appear on every day, including All days; day selection lives only in the tabs.
No topic selection means all topics. Video available and Hide watched combine
with topics and other search criteria. Changes apply immediately,
and Show N sessions returns to the same Sessions page. Native back keeps the
chosen filters too. Clear removes topic, video, and watched filters without changing the selected
day or search. A small, borderless Clear action beside the persistent compact
summary beneath the day tabs exposes a one-tap reset without reopening Filters.
The summary exposes current
choices without repeating a picker in the scrolling agenda.

The toolbar uses MAUI 11's native `ToolbarItem.BadgeText`, counting selected
topics and active video/watched filters, not days, search terms, or results. Zero hides the badge. Its semantic
hint identifies the filter action; the visible summary is also available on
pre-iOS-26 devices where native toolbar badges are unsupported. Badge colors
use purple/white in light appearance and lavender/midnight in dark appearance.

On iOS, pages use `SafeAreaEdges=None` and scrolling reading pages retain `ScrollView.SafeAreaEdges=Default` (native automatic content insets). Agenda screens consume horizontal container edges once at their root layout (`Container,None,Container,None`), keeping notices and content clear of the Duo's trailing native tab chrome without hard-coded widths or changing vertical scrolling. The adaptive pane grids protect their top and side container insets and draw through the bottom container inset. On iOS their bottom edge requests `SoftInput` so the panes resize above the keyboard. Shell's tab background is transparent so the platform-owned material can show scrolling content beneath it. The pinned MAUI iOS CollectionView explicitly disables automatic content insets in `ItemsViewController2.ViewDidLoad`; `SafeAreaFooter` therefore uses the public MAUI page `SafeAreaInsets.Bottom`, observing inset changes, to keep the last item reachable. There is no bottom viewport margin, fixed tab-bar padding, negative margin, or custom renderer. Android pages use `SafeAreaEdges=SoftInput` and resize input mode for keyboard avoidance; the adaptive pane grids use `None` at the bottom to avoid applying that keyboard inset a second time. When compact search hides Shell chrome, the panes add only any remaining measured IME overlap and clear it when search ends. Android retains an opaque native tab surface and a zero-height collection footer. System gesture protection is retained where controls meet the screen edge, not as a blank inset around every page.

Home, session rows and details show a red dot and a text Live indicator during a
published session's time slot, never as proof of stream availability. The
indicator shares a row with the schedule heading or session date/time rather
than adding a dedicated row. The dot has
a static soft glow, with no blinking or continuous animation. A shared foreground
clock refreshes the state every 30 seconds and immediately on activation; it stops
in the background. The schedule timeline uses the same semantic live colors.

My Schedule keeps the heading, view switch and timezone in a content-sized header. In short windows or at large text sizes, the timezone uses a concise local-zone label, the discovery action moves to the native toolbar, and errors use the compact Details/Retry treatment, leaving more room for the timeline; full descriptions remain accessible. Topic filters have a 16-unit top inset inside the planner gradient. Day headings scroll away instead of pinning when the unobscured timeline cannot also fit a readable session card. Native Shell owns safe areas; the embedded Avalonia host does not apply window insets again. The timeline fills its viewport, with 16 units of scroll-content padding plus the iOS page's bottom inset. Android's opaque tab bar needs no extra inset. Native-bar clearance stays in device points when text is scaled.

Home, Community, and the planner keep hero backgrounds full-width while their
inner text and body content retain a bounded reading width. Horizontal safe
insets belong to the content, not the decorative background. Onboarding already
uses this full-width-background composition.
On iPhone Duo only, `DuoSafeAreaContent` intersects the content's native frame
with its owning page's UIKit safe-area layout frame. This keeps readable content
clear of either side's system controls, updates during layout and posture changes,
and does not add padding when the page has already inset the content. The
background stays edge-to-edge. Other iPhones, Android foldables, and desktop
targets retain their existing safe-area policies.

## Icons and navigation

Use only `IconFont.Maui.FluentIcons` for app-owned UI icons. `Icons.xaml` holds typed Regular/Filled glyph resources with theme-aware colors. iOS and Android use the actual NuGet library. The small `DotNetConf.FluentIcons` project is an Avalonia-only compatibility shim: version 1.1.1 has no plain .NET/browser assembly, so those targets otherwise cannot resolve its typed glyphs. Search uses the same icon family; back controls remain native. Launcher and splash icons are image assets, not font glyphs. Bundled website artwork is redistributed with the project owner's confirmed permission. Preserve its provenance notice; this is not a general open-source artwork license.

Five Shell destinations: Home, Sessions, Speakers, My Schedule, Community. Android's compact tab label is "Schedule" to avoid truncation; the page is still My Schedule. Sessions and speakers use ID-based details. In the Sessions and Speakers tabs, selections and cross-links replace the current detail, with no pane Back or Close; compact Back returns directly to the list, and expanding restores the latest selection. Elsewhere, a detail link returns to a matching page in the active tab's navigation stack, preserving that page and its scroll position; otherwise it pushes a new page. Links to the current detail are no-ops, and navigation is serialized across session/speaker commands. Other tabs' histories are not searched or disturbed. My Schedule displays a live saved-session badge; zero hides it. Clearing the topic count also recreates the filter toolbar entry through shared MAUI APIs, because iOS can retain a stale native badge after its bound value becomes null. Counts and saved state are also visible text, never badge-only information.

## Content and states

Home shows a prominent Ask launcher for a ready, tool-capable on-device model,
or Browse sessions in its place when Ask is unavailable. The Sessions tab
remains available in either case.
It expands the existing Syncfusion Toolkit bottom sheet, not a chat transcript.
A native editor retains text selection and accessibility; a decorative
purple-magenta GraphicsView halo is static at rest and under reduced motion.
The content determines the composer's height; its proportional decorative
background does not participate in measurement. Multiline requests and scaled
text grow the content naturally, within the editor's scrolling height limit.
The composer reserves a footer inside the halo for its on-device caption and
a compact 36-unit purple/lavender send disc inside a 48-unit touch target,
with a 20-unit up-arrow glyph. While working, that same action becomes
a stop square with a nearby progress indicator; it restores send when work ends.
Android's `AskEditor` removes only its native Material underline so the shared
composer halo remains the single input outline; other editors are unchanged.
The header uses a small X icon with a 48-unit close target. All icon-only actions
have descriptive accessibility labels and follow the current light/dark theme.
The sheet scrolls its input, controls, and results when space is tight, and is
capped at 720 units on wide windows; close and backgrounding cancel pending
work. Outcomes are announced while the sheet is open.

Each request stages one grounded action against the actual agenda. Navigation
uses the existing detail service and shared search state. Saving reuses the
normal saved-state/reminder update path and never toggles an existing save.
Tool invocations are serialized per request, including native provider callbacks;
identical proposals are idempotent, while conflicting proposals invalidate the request.
Saving opens My Schedule after one atomic update. Explicit all/every requests
save the full grounded set, not just a page of model lookup results; other
ambiguous targets require a choice. Removal lists exact saved talks and
device-local times, with explicit confirm/cancel controls. Confirmation is
consumed once and rejects changed agenda/saved snapshots; the model never
approves persistence. The developer trace setting stays hidden, including in
Debug builds; retained Debug-only diagnostics contain no argument values.

First-run onboarding is a separate full-screen modal carousel without a
navigation bar: welcome, personal schedule introduction, and optional session
reminders. Space Grotesk headings, typed Fluent icons and short explanatory
copy focus each step on one task. Next moves from welcome to session planning;
Continue moves to reminders, and Done finishes setup. Skip setup sits alongside each hero icon.
The discovery view and local-model readiness check start when entering the planning step;
agenda-based discovery can run while the optional check is still in progress.
A ready model offers the AI editor alongside random discovery; otherwise up to six
compatible, varied sessions are selected from the downloaded agenda. Readiness changes
preserve an existing preview, and replacing checked suggestions requires confirmation.
Status feedback stays visible above the scrolling planner; long messages scroll independently,
with a compact scroll cue beside them. The status area gives the request editor more room while editing.
Review and AI setup shortcuts lead to the shortlist and optional model setup without switching models,
including while an existing model download is being checked.
Shuffle guidance appears only alongside a preview. No cached agenda means an honest
download state, never bundled session examples. Continue moves to reminders without
saving suggestions. Checked, unsaved suggestions require a warning before continuing
or leaving setup. Continue also asks whether proceeding without sessions is intentional
if none have been added during setup; explicit Skip does not ask that question.
After confirming continuation to
reminders, Done does not repeat the same warning. Back returns to the preceding step.
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
single compact row: quiet Back, three centered progress dots with a step count,
and a content-width Next, Continue, or Done action. Buttons advance the carousel;
swiping is disabled so it cannot bypass the unsaved-selection warning.
Idle reminder status is hidden; permission
results and recovery remain visible. Labels wrap and content scrolls at larger
text sizes without disabling font scaling.
Completion and skipping persist locally only after successful modal dismissal;
failed dismissal leaves setup retryable, while an interrupted launch remains
eligible. Reminder subscriptions attach/detach with page visibility.
Android back moves to the previous step or dismisses from welcome.
My Schedule opens the regular native planner, which shares the builder view
without the onboarding Skip action.
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
compact line of local start date/time and duration, with an expandable session
description that preserves the checked selection. The accessible checkbox description retains the end date/time.
Watching hours sit in a single summary row with a Change action; native clock
pickers and guidance expand only when needed. Any assumed time boundary stays
visible above the shortlist as a short factual note. Users review the selected
sessions and times before confirming. Editing a request preserves the existing
selection but disables adding it until the prior request is restored or a new
shortlist is generated. Replacing or leaving checked suggestions requires confirmation.
If the agenda or time zone changes, checked suggestions remain visible but
cannot be added until a fresh shortlist has been reviewed.
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

My Schedule's quiet Fluent agenda action sits after the timezone
and beside the existing Timeline/List workflow, or in the native toolbar when
space is constrained. Empty schedules open the personal
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
and in Filters. Clearing topics leaves day selection and search unchanged. Empty
results also offer a separate action that clears search, day, speaker and all filters.
Neither changes saved sessions. A zero-match result keeps search reachable; day
tabs and the toolbar filter action return after a constrained compact search.

Community is a Shell tab; Home's Fluent settings gear opens Settings.
Settings includes appearance, device-time-zone information, and an About action
preserving access to disclosure and app information through native back navigation.
Home shows the published date range, the next current or upcoming saved session,
and links for registration and conference-site updates. It does not imply a live
stream is available before a destination is published.
Community's overview leads with a restrained branded introduction, sharing and
registration, then YouTube and social channels. Meet locally states when no
events are listed; a single Browse community events action appears only when
there are listings and opens the list first. No community events or venues are
bundled, and no official event feed is connected yet. An official-website link
and short guidance for starting a gathering remain available; there are no
invented community statistics or organizer workflows.
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

About discloses the Microsoft agenda source and links to the community overview. Both mock flags are false; no session, profile or community-event dataset ships in the app. Startup immediately displays a validated local cache, when available, then refreshes Microsoft's agenda without announcing success. A fresh install shows small first-download progress; without a connection it explains how to download the agenda and preserves saved choices. Inline notices appear when connectivity, updates or local storage need attention, including on Community and the filter page. Saved sessions withdrawn from the feed remain in My Schedule with an explicit explanation. Corrupt bookmarks are preserved, saving is disabled, and agenda browsing remains available.

Speaker rows and detail pages show a feed-provided downloaded portrait or the exact bundled .NET Bot placeholder from the website. Speaker photographs are not packaged with the app; public URLs may include GitHub-hosted images. Unavailable or invalid images retain the placeholder, including on a first offline launch. Agenda and biographies come only from the public Microsoft feed, never private organizer enrichment. Unpublished bios retain the "Biography to be announced." state. Topic filters explain when no topics have been published. Loading, empty results, no bookmarks, and invalid IDs retain their content states. Overlaps are persistent session attributes, not notification banners or restrictions. Optional showcases must not replace these core affordances.

Session details show Watch session only for a published video link; otherwise
they explain that a video has not been linked yet. Any published session has
an explicit Mark as watched action, including sessions watched live. The device handles video links, so a supported
video app may open; otherwise the link opens in a browser. Opening the link does
not change watched status. A full-width watched control pairs its label with an
empty or checked circle and announces both its current state and action. Watched
rows say Watched, and the action remains available to undo that choice if a video
link disappears. Watched state is local and separate from the
heart; storage failures remain visible without blocking agenda browsing.

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
