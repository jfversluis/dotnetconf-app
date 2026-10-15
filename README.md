# .NET Conf companion

[![Save the date! .NET Conf 2026, November 10-13](https://www.dotnetconf.net/img/conf-og-metadata.png)](https://www.dotnetconf.net/)

Your .NET Conf, in your pocket. An **unofficial companion app for iOS and Android**, built to help you discover talks, meet the speakers, and plan what you want to watch.

**Join us at [.NET Conf 2026](https://www.dotnetconf.net/), November 10-13!** Celebrate the launch of .NET 11, learn what's new, and connect with the community at this free virtual event. [Register now](https://aka.ms/dotnetconf2026) and save the date.

## Explore the conference

- **Find your next idea.** Browse sessions and search by title, topic, or speaker. Switch between local days with scrolling day tabs, or narrow the agenda by topic.
- **Meet the speakers.** Read their biographies and explore their talks, with easy navigation between speakers and sessions.
- **Make room for details.** On wide windows, Sessions and Speakers keep the list on the left and details on the right. On supported foldables, an active division separates the panes; a horizontal division stacks them. Narrow windows or very large text retain native navigation, and resizing carries your selected details with you.
- **Find your community.** Discover ways to watch together, meet local presenters, or organize a gathering. No community events ship with the app, and an official event feed is not connected yet. The empty event list links to the official website; the map stays hidden without events.
- **Build your schedule.** Tap a heart to save a session. My Schedule shows only your saved sessions, as a timeline or a plain list. Only days with saved sessions appear on the timeline; overlaps are marked with how long they clash, topic filters fade what you don't need, and during the conference a live marker shows where you are. Swipe the topic row on narrow or height-constrained screens; taller wide windows show wrapped filters. The decorative headline gives way when space is tight or text is large. Wide windows can show session details beside your schedule; supported vertical folds keep the panes clear of the hinge. Compact windows, horizontal folds, and very large text retain native navigation. Pinch or use the zoom buttons to see more or less of the day. Your choices stay on your device.
- **See times that make sense to you.** Session times use your device's local time zone.
- **Feel at home.** Follow your device's appearance or choose Light or Dark in Settings. The app supports larger text.

The agenda opens immediately from the last downloaded update. No sessions or speaker profiles ship with the app: the first launch needs an internet connection to download them. The Microsoft agenda refreshes in the background without interrupting browsing. To check for updates yourself, pull down on Home, Sessions, Speakers, or the Schedule screen. A keyboard- and screen-reader-accessible alternative is available under Settings → About → Check for agenda updates. If an update fails, choose Retry. Downloaded data and your saved choices remain available offline.

### On-device schedule planner (experimental spike)

An optional first-run screen lets you describe interests, must-see speakers and
availability in natural language. Skip it at any time, or reopen it from
**My Schedule → Plan with on-device AI (spike)**. This is not a hosted chatbot:
there are no API keys, accounts, fake responses or cloud fallback.

The spike uses `Microsoft.Maui.Essentials.AI`'s `AppleIntelligenceChatClient`
on supported Apple Intelligence iPhones with **iOS 26+**, Apple Intelligence
enabled, a supported language, and the local model downloaded. It checks
readiness with a short real local inference because this preview package has no
public model-availability API. Android shows an unavailable message; normal
browsing and likes still work. A simulator build alone does **not** establish
that the model is ready or that inference works.

The local model produces schema-constrained query arguments and, when needed,
session selections. The app dispatches `Microsoft.Extensions.AI` functions:
`search_sessions`, `speaker_sessions`, `topic_sessions`, and `submit_proposal`. This is an
application-managed tool flow, not an autonomous native tool loop: the preview
client can repeat native callbacks within a single request. Preference
extraction is separate from selection; selection cannot rewrite constraints.
C# filters by availability and existing likes. Searches return at most four
real agenda rows per page, with at most ten function calls per request. Speaker
requests support pagination and unique partial names; ambiguous names require
clarification. An "all sessions" request gathers all matching speaker sessions
deterministically, and validation rejects an incomplete result. Independent topic and
speaker requests query both groups, not their intersection. A separate small
local extraction identifies topical intent and its quantity. Literal topics
are grounded directly; inferred interests combined with speakers need quoted
source evidence, and speaker names alone cannot justify an inferred topic.
Topics use real agenda tags. Interest lists joined by "and" or "or" use a union;
only explicit combined-talk requests ("both", "together", "in the same session")
require a tag intersection. Common topic phrases such as "phone apps" are
grounded alongside literal topics, so "mobile development and AI" keeps both
interests. The planner also supports
single speaker/topic scopes (for example, Maddy's MAUI talks). Different
speaker/topic pairings in one request are rejected instead of flattened.
An "all sessions about" request paginates and validates completeness within the
requested availability, including after the user changes the preview selection.
Daily limits without dates are expanded over actual conference dates in the
requested time zone. "After 10pm for me" means the entire session must finish by
22:00 local time. Late/conflicting matches stay visible with warnings, unchecked
and unavailable for saving; they are not silently hidden. A don't-miss speaker
request that cannot fit remains an explicit conflict. Only the topic
vocabulary and bounded query results enter prompts, not the complete catalog.
No function can change likes.
`NLEmbeddingGenerator` is not needed for this small grounded agenda.
Requests are bounded to four topics, eight speakers, twelve availability windows
and twelve selected talks, with one time zone per request. Matching talks are
not hidden just because the selection limit is exceeded; the preview explains
why it cannot be saved.

`Microsoft.Maui.AI.Attributes` / `ExportAIFunction` was evaluated, but the
current preview generator's schema construction fails with reflection-based
JSON serialization disabled. This NativeAOT app therefore uses small explicit
`AIFunction` adapters with literal schemas and bounded argument readers instead.
The tool tests run with reflection-based JSON serialization disabled.

The preview shows the model's interpretation of required speakers and allowed
time windows with explicit time zones. C# converts these to instants; the model
does not need to perform time-zone arithmetic. **Check this interpretation**: C# cannot
prove that a language model extracted every constraint correctly. Edit and
regenerate if anything is missing or wrong. Select proposed sessions, confirm
that the interpreted constraints match your request, then tap **Add selected
sessions to My Schedule**. C# rejects unknown IDs, unknown or unsatisfied
required speakers, overlaps (including existing likes), and sessions outside
the full availability window. Invalid or ambiguous daylight-saving times are
rejected rather than guessed. For ordinary recommendations C# can select a
compatible subset of model-chosen alternatives, preserving required speakers;
it never drops a talk from an "all sessions by" request to hide a conflict.
Saving is an atomic, additive update: it does not remove previous likes.

For a demo with the **fictional** agenda, first browse its speaker names, then
try: "I like mobile development and AI. I must see Alex Rivera. I can watch
November 10 from 16:00 to 18:00 UTC." Proposal session times use your device's
time zone. Requests, proposals and chat history are not persisted; only
confirmed likes and whether the introduction was shown are stored locally.
Cancellation, a 90-second planning timeout, or a model error leaves likes
unchanged. Model accuracy and tool use must still be exercised on a
model-ready physical iPhone before using this as a reliable live demo.

See the [runtime prompt matrix](docs/AI-PLANNER-SPIKE.md) for vague, precise,
ambiguous and impossible requests, expected results, and the actual simulator
inference checks.

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

Upgrading MAUI Foldable? Follow the [temporary-support removal checklist](CONTRIBUTING.md#replacing-temporary-foldable-support) for exact cleanup points, code to retain, and runtime regression checks.

For security vulnerabilities, use the [private reporting instructions](SECURITY.md) rather than a public issue.

Pull requests validate the Core tests and Android/iOS Release builds. Store uploads
are manual and target Google Play internal testing and TestFlight only; see
[CI and release setup](docs/CI.md) for the required accounts, signing secrets, and
manual promotion instructions.
