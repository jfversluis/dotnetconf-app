# .NET Conf companion

[![Save the date! .NET Conf 2026, November 10-13](https://www.dotnetconf.net/img/conf-og-metadata.png)](https://dot.net/conf)

Your .NET Conf, in your pocket. A **companion app for iOS and Android**, built to help you discover talks, meet the speakers, and plan what you want to watch.

**Join us at [.NET Conf 2026](https://dot.net/conf), November 10-13!** Celebrate the launch of .NET 11, learn what's new, and connect with the community at this free virtual event. [Register now](https://aka.ms/dotnetconf2026) and save the date.

## Explore the conference

- **Get to the event.** Home links to [dot.net/conf](https://dot.net/conf) for event and viewing updates, and to registration. Community brings together #dotnetconf, the .NET YouTube channel and social channels, and a shareable conference invitation.
- **Watch sessions.** As Microsoft publishes session YouTube links, normal agenda refreshes reveal Watch session on session details and an indicator in the lists. The device handles video links, which may open in an installed video app or a browser depending on its settings. Use the in-app Video available filter to find them; when no session videos are linked, the app says so. Mark any published session as watched, including one you watched live, then use Hide watched to narrow the list; opening a video never marks it automatically. Watched status is stored separately from your schedule and can be reversed. No app update is needed for new video links. Published session resources and speaker links appear only when supplied by the feed. Share a session from its details; recipients get its public title, speakers, link, and hashtag, not your saved schedule. Experimental desktop/browser builds copy the invitation or session text instead of opening a native share sheet.
- **Find your next idea.** Browse sessions and search by title, topic, or speaker. Switch between local days with scrolling day tabs, or narrow the agenda by topic. Empty results offer a way to clear every search and filter at once.
- **Meet the speakers.** Read their biographies and explore their talks, with easy navigation between speakers and sessions.
- **Make room for details.** On wide windows, Sessions and Speakers keep the list on the left and details on the right. On supported foldables, an active division separates the panes; a horizontal division stacks them. Narrow windows or very large text retain native navigation, and resizing carries your selected details with you.
- **Find your community.** Share an invitation, follow the official channels, or explore ways to organize a gathering. No community events ship with the app, and an official event feed is not connected yet. The overview says when no events are listed; browsing appears only when there are listings.
- **Build your schedule.** Tap a heart to save a session. My Schedule shows only your saved sessions, as a timeline or a plain list. Only days with saved sessions appear on the timeline; overlaps are marked with how long they clash, topic filters fade what you don't need, and during the conference a live marker shows where you are. Swipe the topic row on narrow or height-constrained screens; taller wide windows show wrapped filters. The decorative headline gives way when space is tight or text is large. Wide windows can show session details beside your schedule; supported vertical folds keep the panes clear of the hinge. Compact windows, horizontal folds, and very large text retain native navigation. Pinch or use the zoom buttons to see more or less of the day. Your choices stay on your device.
- **Plan with on-device AI.** Describe your interests, must-see speakers, and available time to get a personal shortlist from the real agenda. Already have a schedule? Find talks that fit in the gaps.
- **Ask, then get on with it.** On supported devices, the glowing Ask control on Home opens sessions or speakers, filters the agenda, or saves a talk from one request. No chat transcript. Removals always need your confirmation.
- **See times that make sense to you.** Session times use your device's local time zone.
- **See what's on now.** A red dot and Live label identify sessions during their published time slots on Home, session lists, details, and the schedule timeline. This follows the agenda, not a check that a broadcast is running. It updates every 30 seconds while the app is open and immediately when you return.
- **Feel at home.** Follow your device's appearance or choose Light or Dark in Settings. The app supports larger text.

The agenda opens immediately from the last downloaded update. No sessions or speaker profiles ship with the app: the first launch needs an internet connection to download them. The Microsoft agenda refreshes in the background without interrupting browsing. To check for updates yourself, pull down on Home, Sessions, Speakers, or the Schedule screen. A keyboard- and screen-reader-accessible alternative is available under Settings → About → Check for agenda updates. If an update fails, choose Retry; in a compact view, open Details first. Downloaded data and your saved choices remain available offline.

## Plan with on-device AI

Tell the app what you want to learn, who you don't want to miss, or when you're
free, and discover a shortlist from the downloaded .NET Conf agenda. Need
inspiration? Playful starting points can highlight topics and speakers from the agenda.
Once you've saved some talks, **Fill schedule gaps** helps you find more on
those same conference days without moving your existing choices.

Open **My Schedule** and choose **Discover sessions for my schedule**, or **Find
talks for my gaps** once you've saved sessions. Discovery is also available
during setup, even without an AI model. **Explore a mix** or **Shuffle suggestions**
picks up to six compatible talks with topic, speaker, and day variety from the
downloaded agenda. This is random discovery, not personalized AI advice.
Shuffle replaces the unsaved preview, never your saved schedule. If sessions are
checked, confirm before replacing them; a failed refresh keeps the current preview.

When a local model is ready, the planner also offers the AI request form.
Make a specific request, then review the selected talks,
alternatives, and overlap warnings,
confirm you've reviewed the sessions and times, then choose **Add N to My Schedule**. Nothing is saved just by checking a box or
continuing setup. Setup warns before leaving with checked,
unsaved suggestions; continuing without choosing sessions needs no confirmation.
There is no 12-session saving limit; ordinary AI suggestions still start with
a manageable shortlist. If an "all sessions" request cannot fit, you can explicitly
choose to review a subset instead.

Focused topic suggestions favor evidence in session titles and descriptions,
with agenda tags as a fallback; **Show matching details** explains the approach.
Each suggestion shows its local start date, time and duration. Asking for
all topic sessions also includes tag-only matches. **Watching hours**
(initially 07:00–23:00, editable with **Change**) fill the missing boundary in one-sided time
requests, with the assumption shown in the preview. Explicit ranges override
those defaults, and requests without time limits remain unrestricted.

Planning runs on your device with no app account or cloud AI fallback. It requires a
downloaded agenda and a ready local model: Apple Intelligence on supported
iOS 26+ devices, or Gemini Nano on compatible Android 8+ devices. Availability
depends on the device and its model settings. If a compatible Android phone needs
the model, the planner offers **Download on-device model**, with download progress
when Android provides it. Downloads only start when you choose that action and
can use several GB of internet data and device storage; Wi-Fi is recommended. You can stop
waiting or leave the planner, though Android may continue in the background.
Android automatically uses Gemini Nano 4 Fast for function calling; no model
picker is shown. This model is in Google's AICore Developer Preview, so it
requires a supported device and an enrolled Google account. If it is unavailable,
the planner explains the limitation and keeps agenda-based discovery available.
On iOS, enable Apple Intelligence in device settings. Random discovery and saving
sessions with hearts remain available without a local model.

The planner uses MAUI Labs'
[Essentials.AI](https://github.com/dotnet/maui-labs/blob/main/src/AI/README.md)
and [AI Attributes](https://github.com/dotnet/maui-labs/blob/main/src/AIExtensions/Microsoft.Maui.AI.Attributes/README.md).
Explore [what you can ask and how to get started](docs/AI-PLANNER.md).

### Ask for one app action

When a ready local model supports tools, Home shows **Ask about the conference**.
Otherwise it shows **Browse sessions**. The Sessions tab remains available regardless of AI readiness.
On Android, Ask and the schedule planner both use Gemini Nano 4 Fast
(`gemini-nano-preview-fast`), with no model picker. Readiness requires reported
native tool-calling support. Checking readiness does not download the model or
enroll your account in Google's AICore Developer Preview.
Ask can find sessions by topic, title, speaker, and one device-local conference
day; open session or speaker details; save matching sessions; or preview removal of
saved sessions. Try a topic or speaker actually present in the downloaded agenda.
Opening a specific item or saving without **all** or **every** shows a chooser
when there are several matches rather than guessing. For example, “favorite all Aspire
sessions” or “add all MAUI sessions to my schedule” saves the entire matching
set in one update and opens **My Schedule**. Existing favorites stay saved;
asking twice does not toggle them off or create duplicates.
Use separate requests for ambiguous topic combinations; Ask does not infer topic
unions for bulk saves.
Try **by** with a full speaker name when the intended speaker could be confused
with a topic, session title, or a request for "my" saved sessions. Multi-word
names must match the agenda; a single name must match a complete name part. Ask
matches whole search terms rather than fragments of other words and rejects
actions whose scope cannot be preserved. If the name remains ambiguous, use
normal search and hearts instead.
Keep punctuation in technical names such as `ASP.NET`.
For schedule changes, topic and title terms match session content, not speaker
names; use **by** to target a speaker.
Use **on** before a weekday if it could also be a speaker's name. An unambiguous
full title stays exact when opening or saving/removing a specific session;
searches and bulk or plural topic requests keep all matching talks even when
one has the same title as the topic. If a title could also be a day, speaker,
or schedule phrase, use normal search and hearts to choose the intended session.
For opening a title, enclosing the full title in quotes can distinguish it from
a separate speaker or day. If a quoted topic phrase could also be such a title,
Ask may ask you to use normal search and hearts instead of narrowing a plural request.
Ask cannot restrict searches, opening, or saves to sessions already in your
schedule, including requests for "my", "saved", or "bookmarked" sessions; use
**My Schedule** to review those.
Speaker searches keep a visible speaker filter on Sessions, which you can clear
without changing your search, day, or topic filters.

Removal shows the exact saved talks and times first. Nothing is removed until
you choose **Remove sessions**. Canceling, closing Ask, editing the request,
or a changed agenda/schedule invalidates the pending approval. Ask supports one
action per request, not chained instructions, exclusions, or clock-time ranges;
the separate planner remains the place for building a multi-session shortlist.
Unsupported devices keep the ordinary search, details, and heart controls.
There is no cloud fallback.

The sheet uses the existing MIT Syncfusion Toolkit bottom sheet and a native
editor with a decorative animated glow. Reduced motion keeps the glow static.
Debug builds optionally expose a local tool-call trace; Release builds do not.
Each request exposes only relevant action tools, plus read-only agenda lookup,
so a search cannot turn into an unsolicited save or detail navigation.

### Session reminders

Don't miss a saved talk: opt in to local reminders during setup or in Settings.
The app asks for notification permission only when you choose to enable them
and schedules reminders for up to your next 60 saved sessions. Your device's
notification settings and background restrictions can affect delivery. You can
turn reminders off in Settings; no account or push service is required.

Tapping a reminder opens the matching session in the downloaded public agenda.
Unknown or withdrawn sessions show an unavailable message rather than invented
details. Links wait until first-run setup is dismissed.

### Session shortcuts and companion links

Touch and hold a session, or swipe left and tap **More**, to save/remove it,
change its watched status, or share it. Native iOS and Android lists reveal
compact, rounded More and watched-status actions when you swipe left.
Continue a swipe right past 60% of the row width and release to
save the session, or remove it if already saved; a short swipe cancels.
The purple fill changes its caption from **Swipe to save/remove** to
**Release to save/remove** at the execution threshold, with a brief heart
pulse (unless Reduce Motion is enabled) and supported-device haptic feedback.
The heart remains visible for one-tap saving; the detail-page watched and Share actions remain
available without gestures. Linux and browser lists keep the visible **More**
button because native swipe handlers are unavailable there.
On iOS, revealing or closing a swipe consumes the touch instead of activating
the row's detail button; ordinary taps and long-press actions remain separate.

Shared session text includes Microsoft's public website link and
`dotnetconf://session/{sessionId}` for opening the installed native companion.
Feed IDs that cannot form a companion route retain the website-only fallback.
The custom scheme is not a verified HTTPS/universal link and does not add a
server or account. External links use MAUI 11 route templates, return to
Sessions when navigating Back, and retain inline details on suitable wide windows.
Ordinary in-app navigation still preserves its original list and back stack.
An uncached linked session waits for the shared agenda download; a valid cached
session can open without waiting for the network.

To inspect app-link delivery on a running iOS simulator, use a session ID from
the downloaded agenda, for example:

```bash
xcrun simctl openurl booted 'dotnetconf://session/BRK001'
```

Accept the system's **Open in .NET Conf?** confirmation if it appears. The app
should show the matching published session, with Back returning to Sessions.
Use the simulator UDID instead of `booted` when multiple simulators are running.
To test a cold launch, terminate the app first, then run the same command.

Android runtime testing found one unresolved accessibility limitation with the
pinned packages: repeated custom-scheme activation can reset the native Back
announcement to "Navigate up". Initial destination labels and the navigation
stack pass the native regressions; label persistence after reactivation still
needs investigation.

## Availability

The app is **in development and not yet available in the App Store or Google Play**. We're aiming for a first release in November 2026.

**iOS and Android are the primary targets. Linux desktop and browser support is experimental**, with known feature gaps and less validation than the mobile apps. See the [platform limitations](CONTRIBUTING.md#linux-desktop-and-browser-experimental) before trying those builds.

**Conference sessions and profiles come only from the public [Microsoft conference feed](https://dotnet.microsoft.com/blob-assets/json/conf/conference-2026.json).** It supplies the full agenda. Day numbers and times come from the feed; the published November 10–13 event dates are explicit conference metadata because the feed has no calendar dates. There is no fixed day-count limit, and day tabs follow the downloaded sessions in your local timezone. Private organizer enrichment is not used. Successful downloads are cached for offline use; a fresh offline install shows a download prompt, not a bundled agenda. Speaker photographs are downloaded and cached from the feed's public URLs, including GitHub-hosted images, never bundled. Missing or unfinished biographies are shown honestly, and missing or unavailable portraits use the website's bundled .NET Bot image. Only presentation assets such as branding, fonts, icons, splash screens and the Bot placeholder are bundled.

## Your data

Saved sessions, watched status, and your appearance preference are stored locally on your device. There is no app account or app-operated upload/cloud sync for these choices. The app caches public agenda data from Microsoft for offline use; failed updates do not replace it. Saved and watched choices remain local even when session details are unavailable or a session is withdrawn from the feed. Speaker photos load on demand from the feed's URLs through the UI framework and its built-in image caching. Photo hosts are not restricted, and HTTP photo URLs are upgraded to HTTPS. Offline photos depend on framework/platform caching; missing or failed images show the bundled .NET Bot placeholder. No GitHub credentials are required. Links to the conference website open in your browser.

Microsoft's feed blocks cross-origin browser requests. A fresh browser startup cannot download the agenda directly and shows an honest empty/download error state. A validated local cache remains readable when updates fail. Browser downloads require upstream CORS support or a separately provided same-origin relay.

The experimental browser app persists its local cache, bookmarks, and watched status across reloads. This is not an offline-installable PWA. No relay or backend is included.

The map implementation uses Apple Maps on iOS and Google Maps on Android, with an internet connection for map tiles. It stays hidden until official event locations are available. The app does not request your location. The event list remains available without map tiles; Android builds without a configured Maps key, Linux and the browser use the list.

Read the [privacy policy](PRIVACY.md), also available offline under
Settings → About → Privacy policy.

Third-party dependencies, their licenses, and artwork credits are available
offline under Settings → About → Third-party dependencies.

## About the project

Built with [.NET MAUI](https://dotnet.microsoft.com/apps/maui) and .NET 11 RC3 technology, with styling inspired by the .NET Conf website. Experimental Linux desktop and browser builds are drawn by [Avalonia.Controls.Maui](https://github.com/AvaloniaUI/Avalonia.Controls.Maui). The My Schedule timeline is an Avalonia control on every platform, embedded in the native app on iOS and Android.

The rocket-bot artwork and website Bot placeholder are redistributed with the project owner's confirmed permission. That permission does not grant a general license to reuse Microsoft artwork or trademarks; bundled third-party notices include provenance and font licenses.

Want to help build the app? See [the contributor guide](CONTRIBUTING.md) for setup, development tools, and verification details.

Upgrading MAUI Foldable? Follow the [temporary-support removal checklist](CONTRIBUTING.md#replacing-temporary-foldable-support) for exact cleanup points, code to retain, and runtime regression checks.

For security vulnerabilities, use the [private reporting instructions](SECURITY.md) rather than a public issue.

Pull requests validate the Core tests and Android/iOS Release builds. Store uploads
are manual and target Google Play internal testing and TestFlight only; see
[CI and release setup](docs/CI.md) for the required accounts, signing secrets, and
manual promotion instructions.
