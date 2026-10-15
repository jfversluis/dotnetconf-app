# On-device planner spike: runtime prompts

On first launch, use the skippable welcome, or reopen
**My Schedule > Plan with on-device AI (spike)** on an Apple
Intelligence-capable target. Wait for the actual local readiness inference,
then enter a request and generate a preview. Review both the session list and
the interpreted constraints before confirming and adding sessions.

All talks and participation in this agenda are fictional. Real names do not
imply announced participation or endorsement.

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
explicitly rejects catalogs over 120 sessions or 9,000 context characters,
rather than silently dropping categories. All matching sessions remain visible;
ordinary recommendations use a bounded interval-scheduling dynamic program
instead of searching all subsets of a 100-session catalog. Required speakers,
availability, existing likes and the 12-selection limit still apply.

## Verification and limitations

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
