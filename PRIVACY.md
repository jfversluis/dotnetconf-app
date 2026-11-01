# .NET Conf companion privacy policy

Last updated: October 31, 2026.

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

Speaker photographs are downloaded from approved public HTTPS image URLs
supplied by the Microsoft feed. The image hosts and approved redirect destinations
can differ by speaker, including Sessionize's public image CDN and Gravatar.
Photographs and agenda content are cached
locally for offline use, not bundled with the app. Private organizer data and
GitHub-hosted portraits are not used.

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

## Data kept on your device

- Your saved session IDs are stored locally so your schedule survives restarts.
- Your appearance preference is stored in the platform's preferences store.
- The last successfully downloaded agenda, public profiles, source URLs, and
  refresh/cache metadata are stored locally. The agenda cache is limited to
  16 MiB. It has no fixed expiry: older successfully downloaded data can remain
  available offline until replaced or removed.
- Downloaded speaker portraits use the platform cache directory. Individual
  downloads are limited to 4 MiB. Cache maintenance removes unreferenced
  portraits and trims retained files toward a 64 MiB total; these are
  maintenance limits, not an absolute instantaneous disk quota. A cached image
  on native devices is eligible for a new download after one day, but this is not a deletion
  deadline. The operating system may clear cached images sooner.
- Searches and active filter selections are used to display results locally.

On native and desktop targets this data uses app-local storage. The
experimental browser target uses browser storage, including local storage for
saved session choices, scoped to the hosting site's origin. Its durable agenda
cache has a smaller 1 MiB limit. Browser portrait persistence limits each image
to 256 KiB and the encoded portrait entries together to 700 KiB of base64
characters (approximately 525 KiB of image bytes); older entries can be
evicted to make room. Browser portraits are eligible for revalidation after a
page reload; the last successfully cached image remains available offline.
Browser storage quotas or settings can prevent caching.
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
- Google privacy policy: https://policies.google.com/privacy
- Apple privacy policy: https://www.apple.com/legal/privacy/
