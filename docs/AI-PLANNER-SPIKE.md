# On-device planner spike: runtime prompts

On first launch, use the skippable welcome, or reopen
**My Schedule > Build a personal schedule** (empty schedule) or
**Fill schedule gaps** (saved talks) on a model-capable target. Wait for the local model availability check,
then choose a starting point above the input or enter a request and generate a
preview. Review the sessions and times before confirming and adding them.
**Show matching details** optionally reveals interpreted interests, speakers and
availability; warning rows and blocking errors remain visible when collapsed.

Onboarding starts the real readiness check while showing the welcome. Its middle
step contains the builder only when that succeeds; missing clients, unavailable
models and timeouts leave a simple hearts/My Schedule explanation instead.
There are no session examples and no saves on that explanatory step. The
in-app builder remains available for readiness retries.
Essentials.AI preview 8 exposes `GetModelStatusAsync` and capability snapshots on
both `AppleIntelligenceChatClient` and `GeminiNanoChatClient`. The same onboarding
gate uses their actual availability, not a device-name guess. No model is
downloaded automatically. Nano has no structured-output capability; its JSON
text goes through the same strict parsing and schedule validation.

Production planning and reminders now read the validated live/cache Microsoft
agenda, not the bundled metadata source. The older fictional agenda remains a
test fixture only. Historical examples below refer to that explicitly fictional
fixture, not announced participation or endorsement.

## Live-agenda gap acceptance matrix (October 31, 2026)

Actual Apple inference used the 70-session downloaded Microsoft feed on the
isolated iPhone 17 Pro simulator (iOS 26.5), with Gerald's `BRK006` and `BRK026`
saved as fixed commitments. All 17 cases passed after runtime-found and review fixes.
Every selected ID was checked against actual intervals, local saved days and
applicable topics/speakers; ordinary suggestions selected at most twelve talks.
No request changed likes without confirmation.

| Request | Expected result |
| --- | --- |
| I see gaps in my schedule. What sessions fit in here? | Fits around saved talks, unrestricted clock availability |
| Help me fill my free slots | Same gap boundary; no invented nine-to-five limits |
| Fill gaps in my schedule with AI sessions | Real AI/.NET AI tags only |
| Fill gaps with .NET MAUI talks | `BRK016`, not already-saved `BRK006`/`BRK026` |
| What Blazor sessions fit in my gaps? | Only fitting Blazor-tagged talks |
| Fill my gaps with C# or Blazor | Topic union; no overlap with saved commitments |
| Find something about building phone apps that fits my schedule gaps | Actual .NET MAUI category |
| What talks by Maddy Montaquila fit my gaps? | `BRK009` and `BRK013` |
| Fill gaps with Daniel Roth's Blazor sessions | `BRK002` and `BRK023`, speaker/topic intersection |
| What sessions by Gerald Versluis fit my gaps? | Explicit unsatisfied request: his talks are already saved |
| Fill my gaps with Ada Lovelace | Speaker-not-found; save disabled |
| Fill my gaps with everything about COBOL | Missing subject; no adjacent-topic substitution |
| Fill my gaps with Scott's talks | `KEY001`; Scott Hanselman is unique in this live feed |
| Fill gaps with C# talks. I can only attend November 10, 2026 from 20:00 to 23:00 UTC. | `BRK009`, `BRK010`, `BRK012`, `BRK042`, exact source clock range |
| Fill my gaps with C# talks, only before 9 | Explicit am/pm or HH:mm clarification; no unrestricted proposal |
| Fill my gaps with C# talks, only after 17 | Literal 17:00 local cutoff preserved on each conference day |
| Fill my gaps with C# talks, only between 9 and 11 | Ambiguous-range clarification; no saved changes |

The first run revealed that generic gaps could induce fabricated availability,
the live agenda lacked the fixture's separate Mobile tag, and generated dated
arrays could contain padded invalid windows. Source-evidenced time extraction,
catalog-aware mobile aliases and direct grounding of literal dated clock ranges
fix those causes; they do not discard invalid model constraints after the fact.
Review also caught numeric cutoffs being missed by the extraction gate and
retained withdrawn bookmarks blocking all gap planning. Numeric restrictions
now reach extraction even if the initial model misses them; ambiguous bare
hours require clarification. Retained withdrawn bookmarks reserve their
last-known slots without becoming candidates or extending updated agenda bounds.

Code coverage includes structured/text-only provider flows, empty/withdrawn
schedules, no gaps, overlapping/adjacent commitments, DST/local-day boundaries,
required speakers, availability, and stale validated saves. Android emulator
coverage checks capability-aware manual onboarding and normal app usage;
real Nano inference still needs supported physical hardware. Physical-device
Release inference, VoiceOver/TalkBack listening and background notification
delivery remain release checks, not claims made from simulator builds.

Five additional actual Apple normal-planner cases passed after restoring the
original empty schedule: all Gerald (`BRK006`, `BRK026`), all Alex Keh (`BRK134`),
vague phone apps (all three real .NET MAUI talks), the exact dated UTC/C# window,
and partial "Alex." The latter correctly asks the user to choose Alex Keh or
Alexandre Zollinger Chohfi in this live feed; it is not a silent empty result.
The live "Scott" case is unique Hanselman, not a claim of live ambiguous-Scott
coverage; ambiguity has both fixture coverage and this actual Alex check.

## On-device starting points

The horizontal starting-point strip shows a small spinner until the whole batch
is ready, without blocking typing or Skip. After
readiness, a separate bounded local inference chooses three to five distinct
topics from up to eight shuffled, real agenda topics. C# validates the output
against both the candidate mix and the agenda, then builds input-only prompts
with lighthearted labels. It also chooses a random, uniquely named speaker with
scheduled sessions for an explicit "I want to see all sessions by [name]" prompt.
Nothing generates or saves a schedule until the attendee takes those actions.
The batch stays stable for that planner instance; reopening creates a fresh mix.
Cancellation, timeout or invalid output retains curated options with an explicit
status, revealing all fallback chips together. No model response is presented as
a successful refresh on failure.

A random seed in the prompt alone repeatedly returned the same five topics.
Shuffling the bounded candidate pool before asking the model produced different
mixes across three actual-client simulator launches. Each last/offscreen chip
was scrolled into view and filled the correct prompt without saving; a generated
prompt also produced a real, confirmation-gated preview.

## Why simple requests previously failed

Speaker validation previously required a full-name match, so `Alex` could not
satisfy `Alex Rivera`. Preference extraction could also invent availability
when the user had not supplied any. Short keyword search was unsuitable for
whole sentences, and an open-ended native tool loop could repeat queries or
omit co-presented talks.

The spike now resolves unique partial speaker names consistently in search and
validation. It uses small schema-constrained local requests, explicitly includes
schema instructions that the native preview bridge otherwise omits, and treats
both an empty availability marker and `none` as unrestricted. Time extraction
runs separately, only when availability was specified. Redundant zero seconds
and consistent ISO offsets are normalized in C#; inconsistent offsets, unknown
zones, sub-minute precision, invalid dates and DST ambiguities are not guessed.
The app dispatches bounded `AIFunction` queries using the model's arguments,
then submits the resulting preview through the validation function. This is
not an autonomous model-managed native function loop. For an all-speaker
request, C# gathers every matching result, including co-presented sessions.
Catalog entity kinds and literal topic phrases also outrank model-inferred
labels: `Mobile` cannot become a required speaker, and a request explicitly
naming Blazor cannot be redirected to an unrelated topic. Phrase boundaries
prevent `UI` from accidentally matching the word `build`.

A combined request previously took the all-speaker shortcut and skipped the
topic query entirely. Independent topic and speaker requests now contribute a union of
results. Topic intent, quantity, OR/AND and speaker scope are extracted separately.
Literal topics are grounded directly from the request. Catalog-aware prefix
and edit-distance matching handle shorthand and spelling errors, without a
separate alias for every typo. Names, dates and generic conference-request words
alone are not evidence of an interest. Remaining source clauses are classified
by the real local model into the closest actual agenda topic. Adjacent goal
clauses stay together, so "screen readers and bigger text" retains its context.
The model gets a bounded catalog of every real topic plus a representative
session title, not just an ungrounded enum or the entire agenda JSON.
Every relevant ID is then gathered in C#, and C# validates completeness
independently of all-speaker completeness. Topic completeness is relative to the
explicit availability. All matching talks remain visible, including late talks
and conflicts with existing likes: warning rows are unchecked and cannot be
saved without changing the request. A don't-miss speaker request that cannot fit
still reports its unsatisfied constraint.
An unknown extracted person is retained for speaker-not-found validation whenever
the whole name literally appears in the request, regardless of surrounding phrasing
("sessions for Ada Lovelace", "Ada Lovelace, please", or "put Ada on my schedule").
Matching ignores case and allows whitespace differences, but does not accept
substrings or unmentioned model-invented names. Catalog topics are still not people.
If the model incorrectly extracts a literally mentioned non-catalog term such as
"backend" as a person, the request conservatively fails validation instead of
silently discarding a possible constraint; rephrasing is required. Adapter tests
also verify absent speakers stop the pipeline before agenda queries or recommendations.

Topic matching uses structured agenda tags, not arbitrary substrings: `AI`
does not accidentally include every `MAUI` title. `MAUI` resolves to `.NET MAUI`.
Interest lists joined by AND or OR deduplicate a union of results. Requiring
every tag needs explicit combined-talk wording, such as BOTH, together, or
in the same session. A plain "and" previously forced an intersection, making
three-interest requests find no talks. Literal-topic grounding also previously
discarded inferred interests when mixed with exact tags; catalog topic aliases
now preserve inferred interests alongside literal topics instead of dropping them.
Small scalar semantic decisions avoid allowing one large multi-topic response
to replace clear source mentions with neighboring subjects. A single
speaker scope such as `Maddy's MAUI sessions` intersects those speakers and topics.
Different pairings such as Maddy/MAUI plus Gerald/C# are explicitly rejected
rather than flattened into an incorrect union.

A daily clock limit without dates is applied across the actual conference dates
in the requested zone. Per the confirmed demo behavior, **after 10pm means the
entire session must finish by 22:00**, inclusive. A talk finishing at 22:01 or
crossing midnight remains visible with a warning, not selected. Local offsets
are evaluated for each date, including DST. `No matter the time` imposes no
restriction, and bare `latest` is not a date filter.
For a single explicit numeric clock, C# grounds the stated before/after operator
and clock value so extraction cannot add an unstated morning restriction.

A second-model review supplied the OR/AND, scoped-pairing, unsupported-topic,
cross-midnight, DST, completeness and contradictory-constraint edge cases below
and in the deterministic tests. These are not canned model responses.

## Acceptance matrix

Start with no liked sessions, because existing likes intentionally affect what
can be proposed. The local-time case below assumes Europe/Amsterdam
(UTC+1 on November 10). UTC cases must behave the same in any device zone.
Exact recommendations can differ; the expectations concern real IDs, relevant
topics, complete all-speaker requests and preservation of hard constraints.

| Request | Expected result |
| --- | --- |
| `I like mobile, AI and I want to see everything on perf` | All eleven Mobile OR AI OR Performance matches, including both performance talks. |
| `Everything on performnce` / `gimme all the perf talks pls` | Both performance talks, without extra C# talks. |
| `All talks on MAUUI` / `Everything about Blazorrr` | Seven MAUI matches / two Blazor matches, not broad Mobile/Web replacements. |
| `My app is sluggish. Help me find bottlenecks and cut memory allocations` | Only the two performance talks; a valid recommendation. |
| `I need to build HTTP endpoints for my backend` | `aspnet-apis`; Backend must not become a required person. |
| `My users need screen readers and bigger text` | Only `accessible-by-design`; do not split off "bigger text" into an unrelated performance interest. |
| `I'm chasing lower memory use and smoother startup` | Both performance matches. Independent holdout phrasing, not an instruction example. |
| `Could you find talks about assistive technology?` | `accessible-by-design`. Independent holdout phrasing. |
| `Show me every perf talk and all Blazorr talks` | Both performance and both Blazor talks. |
| `I care about MAUUI, AI, and perf` | Twelve MAUI OR AI OR Performance matches, without inventing a speaker/time constraint. |
| `Please find sessions on making builds and tests more reliable` | `gerald-maui-testing`. Independent holdout phrasing. |
| `I like mobile development and AI and want to get the latest on performance` | Eleven Mobile OR AI OR Performance matches; a nonempty valid selected subset, no invented availability. |
| `I like MAUI and AI and performance` | Twelve MAUI OR AI OR Performance matches; a nonempty valid selected subset, not a three-tag intersection. |
| `Give me everything about MAUI and don't miss a session from Maddy` | Seven MAUI talks, including both Maddy talks. Independent all-topic and don't-miss speaker constraints. |
| `I want all the latest on C# but exclude sessions if they are after 10pm for me` | Five matching C# talks shown. Four selected in Europe/Amsterdam; `gerald-csharp-ai` remains visible, unchecked, disabled and warned because it ends after 22:00. |
| `Just everything from Gerald, no matter the time` | Both Gerald talks selected, including the cross-midnight local talk; unrestricted availability. |
| `Everything about AI, MAUI or Blazor` | Eleven distinct matching talks; OR across tags, no duplicates. |
| `Everything about both AI and MAUI` | Only `maui-local-ai` and `maddy-maui-ai`; intersection rather than union. |
| `All of Maddy's MAUI sessions` | Only `maddy-maui-roadmap` and `maddy-maui-ai`, not everyone else's MAUI talks. |
| `Don't miss any session from Gerald, but exclude sessions ending after 10pm local time` | Both talks visible, late talk warned and not selected; explicit unsatisfied all-Gerald constraint prevents saving. |
| `All C# sessions, but they must finish by 22:00 UTC each day` | UTC daily windows, four selected talks and the late Gerald match shown with a warning. |
| `Everything about Rust` | Explicit unmatched agenda entity/topic explanation; never browse unrelated talks as a fallback. |
| `Maddy's MAUI talks and Gerald's C# talks` | Explain that the two scoped pairs need separate requests; do not flatten them. |
| `All sessions about C# and everything from Ortinau` | Seven talks: the five C# talks plus `maui-shipping` and `maui-local-ai`. Both completeness constraints remain visible. |
| `Everything from Ortinau and all C# sessions` | The same seven talks, regardless of clause order. |
| `All sessions about Mobile` | All seven Mobile-tagged talks. |
| `all sessions by Alex` | All three: `building-with-maui`, `avalonia-ui`, `accessible-by-design`; unrestricted availability. |
| `all sessions by Mads` | Both `csharp-next` and `csharp-patterns`. |
| `I'm into web stuff` | Relevant Web/Blazor/ASP.NET talks; no invented required speaker or time window. |
| `I want to build phone apps` | Mobile talks; no invented required speaker or time window. |
| `Help me make my code faster` | `async-performance` and/or `runtime-profiling`; no invented required speaker. |
| `surprise me` | A nonempty, non-overlapping schedule without invented constraints. |
| `Show me all sessions by Daniel Roth` | All three: `blazor-web`, `aspnet-apis`, `blazor-ai`. |
| `I must see David Ortinau. I can only watch November 11, 2026 from 19:00 to 20:00 UTC.` | Select `maui-local-ai`; show the other David match with a warning. Preserve the exact UTC window. |
| `A session with Alex on November 10, 2026, between 17:00 and 18:00 local time.` | Select `building-with-maui`; show the other Alex matches with warnings. Preserve the exact local window and device zone. |
| `all sessions by Scott` | Clarification naming Scott Hanselman and Scott Hunter; no save enabled. |
| `I must see Ada Lovelace` | Explicit absent-speaker explanation; no invented participation or save enabled. |
| `I must see Alex. I am only available November 10, 2026 from 08:00 to 09:00 UTC.` | Preserve the window, show all three Alex matches with warnings, and explain that Alex cannot be satisfied; no save enabled. |

Additional phrasing checks: `I care about accessibility` selects
`accessible-by-design`; `Find talks about Blazor` selects relevant Blazor
talks; `I'd like Stephen Toub's performance talks` selects the performance
talks; `Show every talk by Ortinau` includes both of David Ortinau's talks.
These are runtime checks, not hard-coded responses or prompt-specific branches.

## Real conference size and catalog strategy

Microsoft's [.NET Conf 2024 recap](https://devblogs.microsoft.com/dotnet/dotnet-conf-2024-recap/)
reports **92 sessions over four days**. A 100-session catalog is therefore a
realistic target, not an unusually large conference.

Several actual-client approaches were tried rather than assuming that more
context would solve relevance. Full session batches (titles, descriptions and
tags) produced missing and irrelevant matches. English sentence embeddings
ranked adjacent programming categories above the intended API/accessibility
subjects. Multi-topic arrays and fixed slots also over-expanded interests.
The retained approach uses compact catalog context, source-anchored mentions,
and small semantic classifications for goals. No experimental embedding,
response-repair or fake-model fallback ships.

Context size scales with the topic vocabulary, not session count. The preview
explicitly rejects topic context over 9,000 characters rather than silently
dropping categories; a compact 250-session catalog has regression coverage.
There is no arbitrary 120-session count rejection. All matching sessions remain visible;
ordinary recommendations use a bounded interval-scheduling dynamic program
instead of searching all subsets of a 100-session catalog. Required speakers,
availability, existing likes and the 12-selection limit still apply.

## Verification and limitations

### Current live-feed and preview-8 integration

The rebased app passes **561 Core tests and 18 App tests**, Android and iOS Debug
builds, the preserved desktop/browser compilation, and unsigned iOS NativeAOT
publishing. The latest iOS workload pins were not changed. Installed Xcode 27.1
was used with the build-only `ValidateXcodeVersion=false` override because the
pinned iOS workload expects Xcode 27.0. This is not an officially matched
toolchain claim. Existing Essentials.AI native-callback trim/AOT warnings,
Syncfusion trim warnings and the plugin privacy-manifest duplication need
release review; an unsigned publish does not prove physical Release inference.

Actual simulator checks include the 17-gap/5-normal live matrices above,
confirmation-gated additive saving, rejecting a confirmed preview after an
external bookmark change, cancellation during real inference, and relaunch
persistence. Native screenshots and geometry cover the sparkle action, gap
preview/confirmation, Android manual onboarding and Timeline/List, light/dark
appearance and maximum iOS Dynamic Type/Android 2.0 font scaling. Search
filtering/clearing and a heart round-trip preserved Android's original bookmarks.
The isolated iOS schedule was restored to its original empty snapshot, and both
devices' appearance/font settings were restored.

This is targeted integration coverage, not an exhaustive whole-app UI audit.
Actual Gemini Nano inference requires supported physical hardware with AICore
and prepared assets; the tested emulator correctly uses the unavailable/manual
path. VoiceOver/TalkBack listening, physical-device Release inference and
background notification delivery remain unverified.

### MAUI-first hardening follow-up

The following sections preserve **historical fictional-fixture** verification
from before the live-feed rebase; their counts and prompts are not the current
production acceptance matrix.

The review fixes keep orchestration cross-platform. Onboarding uses a root MAUI
`SafeAreaEdges` content layout and a separate gradient background, replacing
AndroidX inset/pixel calculations and the iOS inset-padding branch. Applying
safe areas only inside carousel item templates did not protect the native iOS
status area; the root composition was verified on both dedicated targets.
The remaining native search-border customization is isolated in `SearchEntry`,
registered once by its static constructor, and still guarded to that subclass.
Platform chat-client registration and the pinned iOS CollectionView footer
workaround remain necessary; no new native layout code was added.

Explicit speaker/topic exclusions such as "no sessions by Alex", "except
Daniel Roth" and "don't show me Blazor" now fail with a visible unsupported
exclusion explanation, rather than accidentally requiring the excluded person.
This is not general-purpose negative-filter support. Existing clock exclusions,
including "exclude sessions ending after 10pm", still use validated availability.
Unknown-name handling remains conservative. The extraction prompt explicitly
distinguishes personal names from programming concepts, with two small
person-versus-interest examples. Earlier runtime attempts misclassified generic
interests as people; no constraint is silently discarded to hide that failure.
Explicit unrestricted-time and don't-miss-every-session wording now outrank
contradictory extracted availability/quantity. Local generation is still not
guaranteed deterministic.

Internal validation routes on typed issue codes, and speaker pagination returns
a typed next offset. Neither depends on parsing English display text.
Planner and schedule-update interfaces let the pure .NET test host exercise the
actual ViewModel without booting MAUI. Saving disables shortlist/confirmation
editing and persists the confirmed snapshot. A successful write followed by a
refresh failure is reported as saved, never as unchanged likes. Outcomes are
explicit events gated by loaded/active view state. Onboarding persists completion
after dismissal, prevents duplicate modals and detaches reminder subscriptions
on disappearance.

That pre-rebase suite had **223 passing tests**, including delayed/failed saves,
late cancellation results, unsupported clients, dismissal/persistence failures,
notification permission denial/revocation, owned-request reconciliation and
retry after notification failures. Both platform Debug builds and unsigned iOS
NativeAOT publishing succeed. Existing AI/Syncfusion trim/AOT warnings and the
duplicate plugin privacy-manifest warning remain; physical Release inference
has not been established.

The final **44/44 existing runtime prompt matrix passed**, using the real Apple
client, not a fake. Checks on the isolated iOS 26.5 simulator exercised actual local
inference for all-Alex, clock-cutoff, impossible all-Gerald/cutoff, unrestricted
Gerald, combined AI/MAUI and exact UTC availability requests. Negative filters
were visibly blocked, and leaving during generation twice prevented late
previews while the retained planner's active flag was false.
Confirmation-gated saving preserved an existing like and survived app relaunch;
only test-added likes were then removed, restoring the original snapshot.
Both platforms passed native search, empty-results/clear and heart checks,
onboarding navigation/persistence, normal and enlarged text layouts
(maximum iOS Dynamic Type in dark appearance; Android font scale 2.0).
Device font/appearance settings were restored. These are targeted regressions,
not exhaustive whole-app UI coverage. Actual VoiceOver/TalkBack listening and
physical-device background notification delivery remain outstanding.

### Current onboarding and reminder follow-up

First-run onboarding now uses a separate full-screen modal carousel:
welcome, shared AI builder, then optional reminder consent. It has no native
navigation bar; swipe and persistent Next/Back/Skip controls remain available.
The regular planner retains native back and a post-save My Schedule action,
but no onboarding-only Skip or explore action. The older pushed-page lifecycle
results below describe the previous iteration, not the current navigation.

On the isolated iOS 26.5 simulator, six current lifecycle checks passed:
fresh modal startup, all three steps/back, completion persistence, Skip
persistence, no permission request before opt-in, and normal in-app builder
without onboarding actions. Native swipe navigation was exercised separately.
Dark-mode visual checks at maximum iOS accessibility text size exposed clipped
Skip text and animated page changes disrupted by a resizing footer. Footer
labels now wrap, and Next/Back use nonanimated native scrolling; all three
steps were rechecked at that size. Swipe gestures retain native paging.
The actual iOS consent alert was shown on explicit Enable; denial displayed a
visible recovery state without trapping the user. A separate temporary QA
bundle identity then exercised Allow without resetting the original app's
permission or likes. Returning to the builder, real local inference for
"I care about accessibility" produced a preview; explicit confirmation saved
the session and the native notification service accepted its reminder.

Reminders use Plugin.LocalNotification 14.0.0, the same toolkit as
`jfversluis/app-myconference`. They schedule future saved sessions at their
start time, reconcile stable IDs on schedule changes/activation, and cap pending
requests at the nearest 60. Settings exposes opt-in/retry, disabling and a
device-settings link. These are local reminders, not remote APNs/FCM push.
Review caught Android's default one-minute delivery grace period and missing
boot permission. The app now permits inexact delivery until the session ends
and declares boot permission for the plugin's existing restore receiver.
The simulator's device-settings link opened the Settings root rather than a
per-app pane; that shortcut still needs physical-device verification.

The follow-up passed 154 core/adapter tests, iOS simulator and Android Debug
builds, and unsigned iOS NativeAOT publishing. Native consent and scheduling
were exercised on iOS only; Android delivery and actual background alert
delivery on physical devices remain unverified. Existing AI dependency
trim/AOT warnings remain. The notification package's empty privacy manifest
is ignored because the app already supplies its own manifest.

### Speaker grounding and accessibility follow-up

186 core/adapter tests pass, including phrase-independent unknown-speaker,
whole-name boundary, topic/hallucination and early-exit pipeline regressions.
Both platform Debug builds pass. Real local inference on the isolated iOS
simulator blocked "Show me sessions for Ada Lovelace", "Ada Lovelace, please",
"Put Ada on my schedule" and "I like AI and sessions for Ada Lovelace".
"all sessions by Alex" and the backend/HTTP-endpoints request still produced
valid previews. These checks did not save or remove likes.

Native inspection verified the iOS editor label and hint, entered text, and
shortlist checkbox labels with title, speakers and time. Android's native
accessibility tree verified an editable, focusable text field with the intended
hint and no accessibility content description overriding editing. The planner
was checked at the largest iOS accessibility text size and Android font scale
2.0, then both device settings were restored.
Planning/save outcomes now request a single semantic announcement.
Actual VoiceOver/TalkBack spoken output and editing gestures remain unverified:
before release, listen to the empty and populated editor, edit text, generate an
absent-speaker error, then review/toggle a valid shortlist and hear the save outcome.
Native metadata and large-text checks are not a substitute for that listening pass.

### Earlier planner verification

Runtime checks use the isolated `dotnetconf-planner-iPhone17Pro` simulator
(iOS 26.5, Europe/Amsterdam). The expanded **44-case matrix ran twice:
88/88 strict checks passed**, including the original requests, shorthand,
spelling errors, semantic goals, and five new holdout phrasings.
Assertions checked matched and selected IDs, visible warnings, disabled excluded
checkboxes, validity, unrestricted/explicit windows and all-speaker completeness.
Earlier approaches failed measurably: the first full semantic pass reached
31/39, and a later source-grounded iteration reached 73/78. Causes included
broader neighboring tags, a time-only clause misread as an interest, and one
native structured-generation failure. Source anchoring, clock-clause filtering,
explicit absent-subject checks, and the smaller two-field intent schema
addressed those failures before the final repeated run.
The runs exercised the real Apple client and agenda
functions, not a fake `IChatClient`. They generated previews only and did not
change likes.

Two additional actual-client checks used an isolated synthetic **100-session**
fixture, without changing the normal agenda or likes: "Everything about perf"
and a lower-memory/smoother-startup goal both returned all **9/9 expected
performance matches**, with no missing or unrelated IDs (1.9 and 2.4 seconds
in this simulator). The temporary Debug action was removed afterward.
These timings are not physical-device performance guarantees.

The final installed artifact also passed a fresh **44/44** regression pass after
the onboarding corrections. Six end-to-end lifecycle/save scenarios passed:
Skip persists without returning to a cached welcome on Home; native back
persists; an interrupted launch offers onboarding again; cancellation does not
publish a late preview; saving requires confirmation and hands off to Schedule;
and a second proposal preserves the first like across app restart.
Two readiness timeouts during rapid force-quit/re-entry testing showed visible
retry/skip states and recovered through the explicit retry control. Test-created
likes were removed afterward, restoring the original empty schedule.

Light/dark visual inspection included the maximum iOS accessibility text size.
That exposed truncated example/browse buttons; full-width wrapping buttons
were corrected and visually rechecked. The screen scrolls, font scaling remains
enabled, and native Skip stays reachable.

All 149 core/adapter tests passed, as did iOS simulator and Android Debug builds
and an unsigned iOS NativeAOT publish. Core and adapter coverage includes
partial-name ambiguity, independent topic and
speaker completeness, their union, pagination beyond four talks, availability filtering, overlap
arrangement that retains required speakers, offset consistency and
source-generated JSON with reflection serialization disabled.
The independent code review found competing startup navigations; that was
removed. Runtime tests additionally caught tab-stack retention and native-back
dismissal timing, both corrected before the final checks.

This is a proof of concept, not a guarantee for arbitrary language. Earlier
iterations produced invented constraints, incorrect topic classifications,
omitted fields and malformed dates;
the preview must remain visible and explicitly confirmed. A successful
simulator inference is not physical-device verification. An unsigned iOS
NativeAOT publish succeeds, but the preview AI dependency still emits trim/AOT
warnings. Android compiles and remains explicitly unavailable for inference;
there is no cloud or canned fallback.

The spike accepts at most four topics, eight speakers, twelve availability
windows and twelve selected talks, within 32 app-function calls. Use one time
zone per request. An oversized complete match stays visible with a validation
error rather than silently becoming a partial result. Different speaker/topic
pairings still require separate requests.
