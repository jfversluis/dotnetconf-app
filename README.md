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
- **Plan with on-device AI.** Describe your interests, must-see speakers, and available time to get a personal shortlist from the real agenda. Already have a schedule? Find talks that fit in the gaps.
- **See times that make sense to you.** Session times use your device's local time zone.
- **Feel at home.** Follow your device's appearance or choose Light or Dark in Settings. The app supports larger text.

The agenda opens immediately from the last downloaded update. No sessions or speaker profiles ship with the app: the first launch needs an internet connection to download them. The Microsoft agenda refreshes in the background without interrupting browsing. To check for updates yourself, pull down on Home, Sessions, Speakers, or the Schedule screen. A keyboard- and screen-reader-accessible alternative is available under Settings → About → Check for agenda updates. If an update fails, choose Retry. Downloaded data and your saved choices remain available offline.

## Plan with on-device AI

Tell the app what you want to learn, who you don't want to miss, or when you're
free, and discover a shortlist from the downloaded .NET Conf agenda. Need
inspiration? Playful starting points can highlight topics and speakers from the agenda.
Once you've saved some talks, **Fill schedule gaps** helps you find more on
those same conference days without moving your existing choices.

Open **My Schedule** and choose **Discover sessions for my schedule**, or **Find
talks for my gaps** once you've saved sessions. Discovery is also available
during setup, even without an AI model. When local AI isn't available, **Explore a mix / Shuffle suggestions**
picks up to six compatible talks with topic, speaker, and day variety from the
downloaded agenda. This is random discovery, not personalized AI advice.
Shuffle replaces the unsaved preview, never your saved schedule.

When a local model is ready, the planner instead opens the AI request form;
mix/shuffle isn't shown. Make a specific request, then review the selected talks,
alternatives, and overlap warnings,
confirm you've reviewed the sessions and times, then choose **Add N to My Schedule**. Nothing is saved just by checking a box or
continuing setup. Setup asks before continuing without adding any sessions, and
warns before continuing or skipping setup with checked, unsaved suggestions.
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

### Session reminders

Don't miss a saved talk: opt in to local reminders during setup or in Settings.
The app asks for notification permission only when you choose to enable them
and schedules reminders for up to your next 60 saved sessions. Your device's
notification settings and background restrictions can affect delivery. You can
turn reminders off in Settings; no account or push service is required.

## Availability

The app is **in development and not yet available in the App Store or Google Play**. We're aiming for a first release in November 2026.

**iOS and Android are the primary targets. Linux desktop and browser support is experimental**, with known feature gaps and less validation than the mobile apps. See the [platform limitations](CONTRIBUTING.md#linux-desktop-and-browser-experimental) before trying those builds.

**Conference sessions and profiles come only from the public [Microsoft conference feed](https://dotnet.microsoft.com/blob-assets/json/conf/conference-2026.json).** It supplies the full agenda. Day numbers and times come from the feed; the published November 10 start date is explicit conference metadata because the feed has no calendar dates. There is no fixed day-count limit, and day tabs follow the downloaded sessions in your local timezone. Private organizer enrichment is not used. Successful downloads are cached for offline use; a fresh offline install shows a download prompt, not a bundled agenda. Speaker photographs are downloaded and cached from the feed's public URLs, including GitHub-hosted images, never bundled. Missing or unfinished biographies are shown honestly, and missing or unavailable portraits use the website's bundled .NET Bot image. Only presentation assets such as branding, fonts, icons, splash screens and the Bot placeholder are bundled.

## Your data

Saved sessions and your appearance preference are stored locally on your device. There is no account or bookmark upload/cloud sync. The app caches public agenda data from Microsoft for offline use; failed updates do not replace it. Saved choices remain local even when session details are unavailable or a session is withdrawn from the feed. Speaker photos load on demand from the feed's URLs through the UI framework and its built-in image caching. Photo hosts are not restricted, and HTTP photo URLs are upgraded to HTTPS. Offline photos depend on framework/platform caching; missing or failed images show the bundled .NET Bot placeholder. No GitHub credentials are required. Links to the conference website open in your browser.

Microsoft's feed blocks cross-origin browser requests. A fresh browser startup cannot download the agenda directly and shows an honest empty/download error state. A validated local cache remains readable when updates fail. Browser downloads require upstream CORS support or a separately provided same-origin relay.

The experimental browser app persists its local cache and bookmarks across reloads. This is not an offline-installable PWA. No relay or backend is included.

The map implementation uses Apple Maps on iOS and Google Maps on Android, with an internet connection for map tiles. It stays hidden until official event locations are available. The app does not request your location. The event list remains available without map tiles; Android builds without a configured Maps key, Linux and the browser use the list.

Read the [privacy policy](PRIVACY.md), also available offline under
Settings → About → Privacy policy.

Third-party dependencies, their licenses, and artwork credits are available
offline under Settings → About → Third-party dependencies.

## About the project

Built with [.NET MAUI](https://dotnet.microsoft.com/apps/maui) and .NET 11 RC3 technology, with styling inspired by the .NET Conf website. Experimental Linux desktop and browser builds are drawn by [Avalonia.Controls.Maui](https://github.com/AvaloniaUI/Avalonia.Controls.Maui). The My Schedule timeline is an Avalonia control on every platform, embedded in the native app on iOS and Android. This is an unofficial project, not an official Microsoft conference app.

The rocket-bot artwork and website Bot placeholder are redistributed with the project owner's confirmed permission. That permission does not grant a general license to reuse Microsoft artwork or trademarks; bundled third-party notices include provenance and font licenses.

Want to help build the app? See [the contributor guide](CONTRIBUTING.md) for setup, development tools, and verification details.

Upgrading MAUI Foldable? Follow the [temporary-support removal checklist](CONTRIBUTING.md#replacing-temporary-foldable-support) for exact cleanup points, code to retain, and runtime regression checks.

For security vulnerabilities, use the [private reporting instructions](SECURITY.md) rather than a public issue.

Pull requests validate the Core tests and Android/iOS Release builds. Store uploads
are manual and target Google Play internal testing and TestFlight only; see
[CI and release setup](docs/CI.md) for the required accounts, signing secrets, and
manual promotion instructions.
