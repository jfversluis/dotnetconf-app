# .NET Conf companion privacy policy

Last updated: November 5, 2026.

This policy describes the .NET Conf companion app maintained by Gerald
Versluis. The app is in development and has not been released. This policy is
included in the app and is available offline under Settings → About → Privacy
policy.

## No accounts, analytics, or tracking

The app has no account sign-in, advertising, analytics SDK, or app-operated
tracking service. It does not upload your saved schedule, appearance preference,
or searches to the developer. It does not request device location, contacts,
camera, microphone, or photo-library access. It does not sell personal data.

## Public conference content and network requests

The app downloads the published conference agenda and speaker profiles from:

- Microsoft: https://dotnet.microsoft.com/blob-assets/json/conf/conference-2026.json

Speaker photographs are downloaded only from public image URLs supplied by
the Microsoft feed, following the image host's redirects. No image hosts are
blocked; these can include Sessionize's public image CDN, GitHub, GitHub's image
CDN, and Gravatar. HTTP photo URLs are upgraded to HTTPS. Photographs load on
demand through the UI framework and its image cache; they are not bundled with
the app. Agenda content is cached locally for offline use. Private organizer
data is not used.

To deliver these requests, Microsoft, image hosts, and their
hosting/CDN providers receive ordinary network metadata, including your public
IP address, request time, requested URL, and HTTP/client information. Requests
can include cache validators such as an ETag or last-modified date. They do not
include your saved session choices or an app-created advertising/tracking ID.
These independent providers may process network logs under their own policies;
the developer does not control their retention or processing locations.

On supported native devices, maps use Apple Maps on iOS or Google Maps on
Android when official event locations and the required configuration are
available. Loading a map can send network/device metadata and the displayed
map area to that provider. The app does not enable user-location tracking or
send your saved schedule to the map provider. Maps are not needed to read the
event list. Links you choose to open in your browser are subject to the
destination's privacy policy and your browser's settings.

The experimental browser app also makes ordinary requests to the website that
hosts the app, which can retain server logs under its own policy.

## On-device planning and reminders

Random session discovery uses only the downloaded public agenda and your locally
saved choices. It does not use an AI model or send a request to an AI service.
Shuffling changes only the unsaved preview; additions require your explicit Add action.

If you use the optional planner, your request and relevant public agenda
information are processed by a local model through Apple Intelligence on iOS
or Gemini Nano on supported Android devices. The app does not send planner
requests or responses to an app-operated AI service or use a cloud-model
fallback. It does not save planner requests, proposals, or chat history;
confirmed session choices are stored locally. The app checks whether a local
model is ready but does not start a model download.
On tool-capable models, the model can request read-only agenda searches in the
app. Their results stay within this on-device planning flow; the model cannot
save sessions without your confirmation.

Session reminders are optional. If you enable them, the app requests
notification permission and gives your device's notification service the
session title, ID, and start time to schedule local reminders. The app does
not send these as push notifications from an app-operated server. You can turn
reminders off in Settings; your device's notification settings also control
delivery.

## Data kept on your device

- Your saved session IDs are stored locally so your schedule survives restarts.
- Your appearance preference, whether you completed the welcome, and whether
  reminders are enabled are stored in the platform's preferences store.
- The last successfully downloaded agenda, public profiles, source URLs, and
  refresh/cache metadata are stored locally. The agenda cache is limited to
  16 MiB. It has no fixed expiry: older successfully downloaded data can remain
  available offline until replaced or removed.
- Speaker photographs use the UI framework's image loading and caching.
  The framework and operating system control cache retention and may evict
  images. Offline photographs are not guaranteed; missing or failed images
  show the bundled .NET Bot placeholder.
- Searches and active filter selections are used to display results locally.

On native and desktop targets this data uses app-local storage. The
experimental browser target uses browser storage, including local storage for
saved session choices, scoped to the hosting site's origin. Its durable agenda
cache has a smaller 1 MiB limit. Photographs are not stored in the app's local
storage cache; framework/browser image caching and cross-origin access rules
apply. Browser storage quotas or settings can prevent agenda caching.
The app does not provide an account-based backup or cloud synchronization service. Operating
system/device backups may nevertheless include preferences, saved choices,
and app data, depending on platform and backup settings. Android backup is
enabled. Browser or device backup/sync features are controlled by their
providers and your settings, not by an app-operated service.

## Retention and your choices

Remove a saved session by toggling its save control, and change your appearance
preference in Settings. Device app-data controls or uninstalling the app remove
local app data subject to platform behavior; clear this site's storage for
the browser app. Platform backups may retain or restore data separately.
Clearing data also removes the offline agenda, which must be downloaded again.
There is no developer-held account or saved-schedule database to delete.

## Development builds and support

Debug builds can include MAUI DevFlow inspection/automation and debug logging
for development. These facilities can expose the running app's UI/state and
diagnostic information to a developer-connected tool. They are excluded from
Release builds and are not an end-user analytics service. Do not share device
logs or screenshots without reviewing them for personal information.

If you contact the maintainer or submit a GitHub issue, the information you
choose to send is handled by that communication service; public issue content
is public. Avoid posting private information. Privacy questions can be raised
through the maintainer's contact options at https://jfversluis.dev/.

## Changes and related provider policies

Policy changes are versioned in this repository and bundled with subsequent
app versions. Review this policy after updating.

- Microsoft privacy statement: https://privacy.microsoft.com/privacystatement
- Sessionize privacy policy (public speaker image hosting): https://sessionize.com/privacy-policy/
- Automattic privacy policy (Gravatar speaker image hosting): https://automattic.com/privacy/
- GitHub privacy statement (public speaker image hosting): https://docs.github.com/en/site-policy/privacy-policies/github-general-privacy-statement
- Google privacy policy: https://policies.google.com/privacy
- Apple privacy policy: https://www.apple.com/legal/privacy/
