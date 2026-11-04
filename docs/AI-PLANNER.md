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
review the shortlist, session times, and any warnings. Open **Show matching
details** to see how the planner understood your request.

AI can misunderstand, so edit your request and try again if something is
wrong. Checked sessions are the proposed additions, not already-saved talks.
Unchecked alternatives also match; their overlap warnings explain tradeoffs.
Select the talks you want and choose **Add N to My Schedule**. Nothing is added until that action, and your
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

Device, language, region, and model settings affect availability. The app
doesn't start a model download. Linux desktop and browser builds don't support
the on-device AI option. If the model isn't available, you can still explore a
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
