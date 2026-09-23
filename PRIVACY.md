# API Free - privacy policy

Effective 23 September 2026. Applies to API Free for Android and API Free for Windows.

API Free does not collect, store or share personal data. It has no accounts, analytics, crash reporting or
advertising, and the developer receives nothing from the app.

## What the app connects to

- **Public API directories.** The app downloads public listings from public-apis, public-api-lists,
  publicapis.dev, freepublicapis.com and n0shake to build its catalogue. These are ordinary web requests
  carrying no personal data.
- **API providers' own pages.** When you press "More about this API" or "Scan docs for key info", the app
  fetches that provider's documentation and pricing pages, and its OpenAPI specification when it finds one.
- **APIs you test.** Requests you send in "Try it" go directly from your device to the API you chose, under
  that provider's own terms. Any key you include is sent only to that API.
- **GitHub (Windows app only).** The optional update check reads release information from api.github.com.

## What stays on your device

Your catalogue, favourites, tags, notes, collections, request variables, docs-scan results and test history are
kept in the app's private storage. API keys you save are encrypted on the device: with the Android keystore on
Android, and with Windows Data Protection (DPAPI) on Windows. Keys and request history are excluded from
Android's cloud backup. Nothing leaves the device unless you export it yourself, and an export's keys and
variables are held in a section encrypted with a passphrase you choose.

## Permissions (Android)

Internet, to reach the directories and APIs above; notifications, for the optional "API of the day" reminder;
and receive-boot-completed, so that reminder survives a restart. No location, contacts, camera, microphone or
storage permissions are requested. Import and export use Android's own file picker.

## Deleting your data

Uninstalling the app removes everything it stored. There is no account and nothing held elsewhere to delete.

## Contact

Questions about this policy: kramn101@gmail.com
