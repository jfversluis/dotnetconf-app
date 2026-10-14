# CI and test-track releases

## Pull request validation

`.github/workflows/ci.yml` runs on pull requests, pushes to `main`, and manual
dispatch. It runs the Core tests and independent Android and iOS **Release**
builds. Android publishes a NativeAOT AAB; iOS builds an unsigned ARM64 simulator app.
Neither build needs store credentials. Fork PRs are supported without secrets.
This is compilation validation, not a device/UI test or proof of store acceptance.

The SDK and workload set come from `global.json`. Android SDK dependencies are
installed by the workload's `InstallAndroidDependencies` target with JDK 21.
iOS CI and TestFlight jobs use the public-preview `xcode-27` ARM64 runner and
explicitly select Xcode 27.0, matching the pinned RC2 workload.
When updating the pins, review the JDK, Xcode, runner,
and target frameworks in the workflows/local setup action together.

CI sets `MobileTargetFramework` to narrow the app's restore to the platform on
that runner without overriding the Core project's `net11.0` target. Local builds
still default to both mobile targets.

In GitHub branch protection/rulesets, require `Core tests`, `android build`,
and `ios build` for `main`. Workflows alone do not enable branch protection.

## What the owner needs to provide

The Android package name and iOS bundle ID are both `dev.jfversluis.dotnetconf`.
Use this ID when creating the store records and iOS provisioning profile.
Release jobs require explicit environment variables for the store IDs and
override `ApplicationId` only for that build; they do not change local app IDs.

Create the GitHub environments below under **Settings > Environments**. Restrict
deployment branches to `main` and add a required reviewer if supported by your
GitHub plan. Store credentials as **environment secrets**, never in source or
chat. Variables are non-secret GitHub environment variables.

### `google-play-internal`

| Type | Name | Value |
|---|---|---|
| Variable | `ANDROID_APPLICATION_ID` | `dev.jfversluis.dotnetconf` |
| Secret | `ANDROID_KEYSTORE_BASE64` | Base64-encoded upload keystore |
| Secret | `ANDROID_KEY_ALIAS` | Alias of the upload key |
| Secret | `ANDROID_KEY_PASSWORD` | Upload key password |
| Secret | `ANDROID_KEYSTORE_PASSWORD` | Keystore password |
| Secret | `GOOGLE_PLAY_SERVICE_ACCOUNT_JSON` | Raw service-account JSON, not base64 |
| Secret | `GOOGLE_MAPS_ANDROID_API_KEY` | Optional restricted Maps SDK for Android key; without it Community uses the event list |

Enable **Maps SDK for Android** for the map key's Google Cloud project. Restrict the
key to that API and to the application ID plus signing certificate SHA-1. Builds
delivered by Google Play use the **Play app-signing certificate**, not the upload
certificate. Configure debug/local signing certificates separately when needed.
The publish step passes the key through the `GoogleMapsApiKey` environment property;
the map key is embedded in the installed manifest and must not be treated as a
server credential. Do not print it or commit it. See the community-map section
in [CONTRIBUTING.md](../CONTRIBUTING.md) for local configuration.

You need a Google Play developer account, an app record with that package name,
Play App Signing enrollment, and an internal testing track with testers configured.
Enable the Google Play Android Developer API in the service account's Cloud
project and invite its email in Play Console with access to this app and permission
to release to testing tracks. Do not grant production release access.

Complete the initial Play Console setup and upload the first signed AAB manually
if required before using the publishing API. The upload key must match the key
registered with Play App Signing. Keep an offline backup of the keystore and
passwords. The workflow publishes `completed` releases to **internal** testing
only, not internal app sharing, closed/open testing, or production. Resolve any
draft-app, policy, consent, or review prerequisites in Play Console; failures are
not silently retried with different publication settings.

### `app-store-testflight`

| Type | Name | Value |
|---|---|---|
| Variable | `IOS_APPLICATION_ID` | `dev.jfversluis.dotnetconf` |
| Variable | `IOS_CODESIGN_KEY` | Exact identity, e.g. `Apple Distribution: Your Name (TEAMID)` |
| Secret | `IOS_DISTRIBUTION_P12_BASE64` | Base64 PKCS#12 export of distribution certificate **and private key** |
| Secret | `IOS_DISTRIBUTION_P12_PASSWORD` | Non-empty password protecting that export |
| Secret | `IOS_PROVISIONING_PROFILE_BASE64` | Base64 App Store distribution provisioning profile for this bundle ID and certificate |
| Secret | `APP_STORE_CONNECT_ISSUER_ID` | App Store Connect team API key issuer UUID |
| Secret | `APP_STORE_CONNECT_KEY_ID` | API key ID |
| Secret | `APP_STORE_CONNECT_PRIVATE_KEY` | Raw contents of the downloaded `.p8` file, including PEM headers |

You need active Apple Developer membership, an explicit App ID, an App Store
Connect app record, and an App Store distribution profile (not development,
ad-hoc, or enterprise). Create a team App Store Connect API key with an upload-capable
role such as Developer, subject to your team's access policy. Accept outstanding
agreements in App Store Connect. Certificate/profile renewal is an owner task.

An upload goes to **TestFlight**, not App Review or production. After Apple
processes the build, complete export-compliance information and assign it to
internal testers in App Store Connect. External testing may require Beta App
Review. The workflow does not automatically assign groups, submit reviews, or
make declarations on your behalf.

To encode binary files on macOS without line wrapping:

```sh
base64 -i /path/to/upload.keystore | tr -d '\n' | pbcopy
base64 -i /path/to/distribution.p12 | tr -d '\n' | pbcopy
base64 -i /path/to/profile.mobileprovision | tr -d '\n' | pbcopy
```

Paste each value directly into its GitHub environment secret. Do not paste
credentials into issues, workflow inputs, or this repository.

## Running a test release

After merging the workflows and configuring the environments, open **Actions >
Release to test tracks > Run workflow**, select **main**, and choose `android`,
`ios`, or `both`. Enter a numeric display version such as `0.1.0` and a fresh
positive build number of at most nine digits, greater than all previous uploads.
Both lanes use these values as `ApplicationDisplayVersion` and
`ApplicationVersion`; no version files are rewritten.

The Core tests gate each release. Each selected lane builds a signed Release
package from the same checked-out commit, retains the AAB/IPA as a GitHub artifact
for 14 days, then uploads it. Release jobs are serialized per store and are not
cancelled by a newer dispatch. Do not queue versions out of order. Store APIs
enforce build uniqueness: after a partial success, retry **only the failed lane**
if that store has not accepted the build. Otherwise use a new build number.
Never rerun a successful upload expecting an idempotent publish.

Manually promote the accepted Android build in Play Console when ready. For iOS,
select the processed TestFlight build for an App Store version and submit/release
it manually in App Store Connect. There is deliberately no production workflow.

Signing files/passwords are temporary, never uploaded as artifacts. The iOS
certificate action uses a temporary keychain with an always-run cleanup step; the publish
steps remove the provisioning profile and Android signing files even on failure.
Actions are pinned to commit SHAs. PR builds use read-only GitHub permissions and
do not receive release credentials.

## Release readiness

Use `https://jfversluis.dev/dotnetconf-privacy-policy.html` as the privacy-policy URL
in both stores. Its source is `docs/dotnetconf-privacy-policy.html`; the published
copy lives at `static/dotnetconf-privacy-policy.html` in the website repository
`jfversluis/jfversluis.github.io`. Keep both copies synchronized when privacy
practices change. The About tab links to the public policy. Privacy contact:
`dotnetconfapp@geraldversluis.nl` (the owner must keep this mailbox operational).

The policy describes the current Release build, not planned features. Review
the final binary and all SDKs before submitting Google Play Data safety and
Apple App Privacy answers; a policy is not a substitute for those declarations
or a guarantee of legal compliance/store approval. The initial Android build 4
predates the in-app privacy link; rebuild with a fresh build number before
distribution if build 4 has already been uploaded.

Store acceptance and distribution cannot be verified without the owner's
accounts and credentials. Passing CI does not resolve the existing physical-device,
accessibility, minimum-OS, or iPhone Duo geometry release gates in
`CONTRIBUTING.md`. Keep the fictional-agenda label visible in test releases; do not
promote fictional content as the official conference lineup. Review Apple's
current accepted Xcode/SDK requirements before production submission, especially
while this app uses a preview .NET toolchain.
