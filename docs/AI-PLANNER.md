# Plan .NET Conf your way

There are a lot of great talks to choose from. The on-device AI planner helps
you find the ones that fit your interests, your favorite speakers, and your
time. It draws its suggestions from the downloaded .NET Conf agenda, so your
shortlist is made up of real conference sessions.

## Tell it what you're looking for

The app checks local AI availability and shows one discovery path, not a choice
between two modes. Without a ready local model, **Explore a mix / Shuffle suggestions**
offers up to six compatible sessions from the downloaded agenda, favoring a mix
of topics, speakers, and days. It excludes already-saved talks and respects
saved commitments. These suggestions are random, not personalized. Shuffling
replaces the unsaved preview; it never rearranges or removes saved talks.
Without a downloaded agenda, connect and refresh Sessions first.

When a local model is ready, the AI request form opens automatically instead;
mix/shuffle isn't shown. Write a request in your own words, or tap a suggested
starting point and make it your own. Starting points can highlight agenda topics and speakers, with
invitations like "Make me dangerous with [topic]" and "More [topic], less FOMO."
Starting points are available straight away, even while the agenda downloads.
Once the agenda and local model are ready, they can offer more topic and speaker
inspiration without changing anything you've typed.
Try requests such as:

- "I'm interested in Blazor and C#."
- "I want to see all sessions by [speaker name]."
- "Find AI sessions that finish before 5pm."

Replace `[speaker name]` with a speaker in the agenda. You can mention topics,
speakers, dates, and times together; simpler requests are easier to interpret.
Times use your device's time zone, or you can name UTC or a time-zone ID such as
`America/New_York`. For time ranges, use explicit times such as "9am to 11am"
or "09:00 to 11:00".

For one-sided time requests, **Watching hours** supplies the missing
boundary. The initial defaults are 07:00–23:00: "before 10pm" means
07:00–22:00, and "after 6pm" means 18:00–23:00. Choose **Change** beside
the hours to edit either clock before searching, or state both times in your request.
The assumed boundary appears above your shortlist, with the time zone if you
specified one. Defaults use the requested time zone and never restrict a request
with no time limits.

Explicit early or late limits still work: "before 6am" means midnight–06:00,
not 07:00 through the following morning. "After 11:30pm" means
23:30–midnight. Explicit ranges such as "from 10pm to 2am" and "anytime before
11pm" override the defaults. The whole session must fit within your availability,
not just its start time. Changing the defaults clears the old preview so you
can review a fresh one. Talks outside your availability aren't offered as
alternative choices in the shortlist. If an overnight default conflicts with a
single cutoff, or you want to exclude a block of time, state the hours you can
watch explicitly instead. For phrases such as "afternoon" or "after work,"
the model estimates the hours and shows them above the shortlist. Review them;
if the estimate isn't right or can't be determined, use explicit times instead.
Write dates with a month name or as `yyyy-MM-dd` to avoid ambiguity, and put date
ranges in chronological order. If a number after a date could be another day or
a session count, repeat the month for each date or remove the count. For different
hours on different days, or to exclude a date, make separate requests listing when
you can watch. The agenda doesn't label session difficulty, so requests that
filter by difficulty need to use a topic or speaker instead.

## Why a session matches

Focused topic requests prefer sessions whose published title or description
mentions the topic, including sessions missing topic tags. When a topic has
no such matches in the requested speaker scope, the planner falls back to
Microsoft's topic tags. These tags can be broad. This is keyword-based grounding,
not a claim that AI has judged the session's quality or depth.

Ask for **all** sessions about a topic to include tag-only matches as well as
title/description matches. Requests for a speaker's sessions remain independent
unless you specifically ask for that person's talks about a topic. Topic filters
in the normal agenda continue to use Microsoft's tags unchanged.

## Make room for discovery

Already saved a few talks? In **My Schedule**, choose **Find talks for my gaps**
to find sessions that fit around them. You can focus on a topic, a speaker, or
an available time, or leave the request open for ideas. The planner looks for
free time on conference days where you already have saved sessions, within
those days' published agenda. Your existing choices stay in place.

## Make it yours

Try discovery in the welcome experience or open
**My Schedule** and choose **Discover sessions for my schedule**. If you've already
saved sessions, choose **Find talks for my gaps** instead. Find sessions, then
review the shortlist and any warnings. Each talk shows its local start date
and time, plus its duration, without repeating the end timestamp.
Open **Show matching
details** to see how the planner understood your request.

AI can misunderstand, so edit your request and try again if something is
wrong. Checked sessions are the proposed additions, not already-saved talks.
Unchecked alternatives also match; their overlap warnings explain tradeoffs.
Select the talks you want. For AI suggestions, confirm you've reviewed the sessions
and times, then choose **Add N to My Schedule**. Nothing is added until that action, and your
previously saved sessions aren't removed.
There is no 12-session saving limit. Selecting nothing asks you to select at
least one session. If an "all" request has incompatible requirements, edit it
or explicitly choose **Let me choose a subset instead**. Setup's Continue
action does not save suggestions. If you haven't added any sessions during setup,
it asks whether you meant to continue. Checked, unsaved suggestions get a separate
warning before you continue or skip setup; choose **Keep choosing** to return and
use **Add N to My Schedule** to save them. Use the Back and Continue buttons to
move between setup steps.

## Ready when you are

The planner runs on your device; it doesn't need an account or a cloud AI
service. You'll need a downloaded agenda and a ready local model:

- **iOS:** Apple Intelligence on a supported device with iOS 26 or later.
- **Android:** Gemini Nano on a compatible device with Android 8 or later and
  the model available through AICore.

Device, language, region, and model settings affect availability. Checking
availability never starts a download.

On compatible Android phones that need Gemini Nano, choose **Download on-device
model** in the planner or welcome experience. This is a one-time setup using
internet access and device storage, potentially several GB. Wi-Fi is recommended; mobile data charges
may apply. The planner shows progress when Android provides it and checks
readiness again before enabling planning. If setup fails, check your connection
and available storage, then choose **Check again** to retry. You can choose
**Stop waiting**, skip setup, or leave the screen; Android may continue the
download in the background. Return to the planner and choose **Check again**
to see whether it's ready. The app does not remove system-managed model assets.
On iOS, enable Apple Intelligence and manage model setup in device settings.

Linux desktop and browser builds don't support
the on-device planner. If the model isn't available, you can still explore a
random mix or browse the agenda and save sessions with hearts. A previously downloaded agenda can be
used offline; a first download needs an internet connection.

## Powered by MAUI Labs

The planner brings together
[Microsoft.Maui.Essentials.AI](https://github.com/dotnet/maui-labs/blob/main/src/AI/README.md)
for on-device models and
[Microsoft.Maui.AI.Attributes](https://github.com/dotnet/maui-labs/blob/main/src/AIExtensions/Microsoft.Maui.AI.Attributes/README.md)
to connect the planner with the agenda. Learn more about how your information
is handled in our [privacy policy](../PRIVACY.md).

On a model that supports tools, the AI requests read-only agenda searches and
receives their results. The app checks your constraints, arranges the sessions,
and asks you to confirm before saving. Models without tool support use structured
request interpretation instead; your existing schedule remains protected.

For short code examples, see [the planner's presentation examples](AI-PLANNER-DEMO.md).
