# On-device planner spike: runtime prompts

Open **My Schedule > Plan with on-device AI (spike)** on an Apple
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
topic query entirely. Topic and speaker queries now contribute a union of
results. Topic intent and quantity are extracted separately when a request
mentions speakers or asks for all sessions. Its source phrase must occur in
the request; names and generic conference-request words alone are not evidence
of an interest. All-topic requests paginate, and C# validates their completeness
independently of all-speaker completeness. Availability or overlaps cannot be
hidden by silently dropping a required talk, including when editing the preview.

## Acceptance matrix

Start with no liked sessions, because existing likes intentionally affect what
can be proposed. The local-time case below assumes Europe/Amsterdam
(UTC+1 on November 10). UTC cases must behave the same in any device zone.
Exact recommendations can differ; the expectations concern real IDs, relevant
topics, complete all-speaker requests and preservation of hard constraints.

| Request | Expected result |
| --- | --- |
| `All sessions about C# and everything from Ortinau` | All six: the four C# talks (`language-features`, `csharp-next`, `csharp-patterns`, `async-performance`) plus `maui-shipping` and `maui-local-ai`. Both all-session constraints remain visible. |
| `Everything from Ortinau and all C# sessions` | The same six talks, regardless of clause order. |
| `All sessions about Mobile` | All four: `building-with-maui`, `maui-shipping`, `maui-local-ai`, `local-ai`. |
| `all sessions by Alex` | All three: `building-with-maui`, `avalonia-ui`, `accessible-by-design`; unrestricted availability. |
| `all sessions by Mads` | Both `csharp-next` and `csharp-patterns`. |
| `I'm into web stuff` | Relevant Web/Blazor/ASP.NET talks; no invented required speaker or time window. |
| `I want to build phone apps` | Mobile talks; no invented required speaker or time window. |
| `Help me make my code faster` | `async-performance` and/or `runtime-profiling`; no invented required speaker. |
| `surprise me` | A nonempty, non-overlapping schedule without invented constraints. |
| `Show me all sessions by Daniel Roth` | All three: `blazor-web`, `aspnet-apis`, `blazor-ai`. |
| `I must see David Ortinau. I can only watch November 11, 2026 from 19:00 to 20:00 UTC.` | `maui-local-ai`; preserve the exact UTC window. |
| `A session with Alex on November 10, 2026, between 17:00 and 18:00 local time.` | `building-with-maui`; preserve the exact local window and device zone. |
| `all sessions by Scott` | Clarification naming Scott Hanselman and Scott Hunter; no save enabled. |
| `I must see Ada Lovelace` | Explicit absent-speaker explanation; no invented participation or save enabled. |
| `I must see Alex. I am only available November 10, 2026 from 08:00 to 09:00 UTC.` | Preserve the window and explain that Alex cannot be satisfied; no save enabled. |

Additional phrasing checks: `I care about accessibility` selects
`accessible-by-design`; `Find talks about Blazor` selects relevant Blazor
talks; `I'd like Stephen Toub's performance talks` selects the performance
talks; `Show every talk by Ortinau` includes both of David Ortinau's talks.
These are runtime checks, not hard-coded responses or prompt-specific branches.

## Verification and limitations

The executable with mixed-request support passed all nineteen cases on two consecutive live-model
UI runs on the isolated `dotnetconf-planner-iPhone17Pro` simulator
(iOS 26.5, Europe/Amsterdam). Assertions checked IDs, validity,
unrestricted/explicit windows and
all-speaker completeness. The runs exercised the real Apple client and agenda
functions, not a fake `IChatClient`. They generated previews only and did not
change likes.

Core and adapter coverage includes partial-name ambiguity, independent topic and
speaker completeness, their union, pagination beyond four talks, availability filtering, overlap
arrangement that retains required speakers, offset consistency and
source-generated JSON with reflection serialization disabled.

This is a proof of concept, not a guarantee for arbitrary language. Earlier
iterations produced invented constraints, incorrect topic classifications,
omitted fields and malformed dates;
the preview must remain visible and explicitly confirmed. A successful
simulator inference is not physical-device verification. An unsigned iOS
NativeAOT publish succeeds, but the preview AI dependency still emits trim/AOT
warnings. Android compiles and remains explicitly unavailable for inference;
there is no cloud or canned fallback.
