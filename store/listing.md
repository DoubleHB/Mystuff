# ApiScout - Google Play listing (version 1.2)

Everything below is ready to paste into the Play Console. Character limits are Google's: app name 30,
short description 80, full description 4,000. Counts are given so you can trim with confidence.

## App name (30 max)

`ApiScout - free API finder` (26)

## Short description (80 max)

`Find free APIs, see demo keys, scan the docs and test them from your phone.` (75)

Alternative, if the first reads as too dense:

`Thousands of free APIs with demo keys, a docs scanner and a request tester.` (75)

## Full description (4,000 max - this one is about 3,000)

ApiScout finds free public APIs and tells you the one thing every directory leaves out: whether you can call it right now, and with what key.

It scans five public API directories (public-apis, public-api-lists, publicapis.dev, freepublicapis.com and n0shake), merges the results into one catalogue of roughly 3,500 APIs in 42 categories, and keeps it on your phone. No account, no sign-up, and nothing is sent anywhere except to the directories and the API you choose to test.

WHAT YOU GET FOR EACH API
- Whether it needs a key, HTTPS and CORS support, and a health score from daily tests.
- The provider's own published demo key where there is one - with a request that works as it is. About 20 APIs answer straight away with no sign-up at all.
- How the key is sent (query parameter, header or path), what is free, and the exact page where you get your own key.
- "More about this API": what the known facts mean for you, then a summary and feature list read from the provider's docs page. Nothing is invented; if the page says little, the app says so.

SCAN THE DOCS FOR KEY INFO
Ask ApiScout to read an API's documentation page and the pricing page it links to. It pulls out sign-up links, sample keys and placeholders, example endpoints, and the sentences about free tiers and rate limits - all from the provider's own site. Press "Try" on any endpoint it finds to send it immediately.

TRY IT
Send GET, POST, PUT, PATCH or DELETE requests with headers and a body, and read the pretty-printed JSON reply. Write {key} anywhere in a request and your saved key is filled in only at the moment of sending - it never appears in what you copy. Request variables ({name} = value, plus {today}, {now}, {timestamp} and more) make requests reusable, and the last 8 requests per API are kept with their responses so you can load one back with a tap. Copy any request as cURL.

ORGANISE
Search by name, description, category or URL. Filter by what you need before calling (no key, API key, OAuth), how much is free, HTTPS and CORS. Star favourites, add tags and notes, and group APIs into collections that appear in the category drawer. An API of the day shows one API you can try today with no key.

YOUR KEYS STAY YOURS
Keys you save are encrypted under the Android keystore and stay on this phone. They are excluded from Android's cloud backup. Request history is encrypted the same way, and keys are never written to it.

WORKS WITH THE DESKTOP APP
ApiScout for Windows exports favourites, tags, notes, collections, keys and variables to a single file; the phone imports it and can export its own for the PC. Keys and variables travel only inside a passphrase-encrypted section (AES-256-GCM, PBKDF2 with 310,000 rounds). Nothing already on the device is overwritten.

ALSO
- Light, dark or system theme.
- Copy any API as plain details, Markdown or cURL.
- Only demo keys that providers print in their own public documentation are shown - never leaked or private keys.
- Free and without adverts.

## Category and rating

- Category: Tools (or Productivity). There is no Play category for developer tools on Android.
- Tags: API, developer, REST, JSON, HTTP client.
- Content rating questionnaire: no user-generated content, no violence, no purchases - expect "Everyone" / PEGI 3.
- Ads: none. In-app purchases: none.

## Data safety form (what the app actually does)

- Data collected by the developer: none. The app has no analytics, crash reporting or accounts.
- Data shared with third parties: none by the app itself. Requests you send in "Try it" go to whichever API you
  choose; the directory scans go to the five directory sites; "More about" and "Scan docs" fetch the provider's own docs and pricing pages.
- Data stored on the device: catalogue, favourites, tags, notes, collections, request variables and docs-scan results in the app's private
  storage; saved API keys in the Android keystore (flutter_secure_storage); request history encrypted with a keystore-held key.
- Data can be exported by the user to a file; secrets in that file are encrypted with the user's passphrase.
- Permissions: INTERNET only. No location, contacts, camera or storage permissions (the import/export file picker uses the system picker).
- Data deletion: uninstalling the app removes everything, since allowBackup is off.

## Privacy policy (Play requires a URL for every app)

Suggested text to host on a page and link from the listing:

> ApiScout does not collect, store or share personal data. It has no accounts, analytics or advertising. The app
> downloads public API directory listings and, when you ask it to, the documentation pages of an API and the
> requests you choose to send in "Try it" - those requests go directly from your phone to that API's servers under
> that provider's own terms. API keys you save are encrypted on your device with the Android keystore and never
> leave it unless you export them yourself in a passphrase-protected file. Uninstalling the app removes all its data.

## Assets in this folder

- `screenshots/01..08-*.png` - eight phone screenshots, 1080x1920 (9:16), captioned. Play accepts 2-8 phone screenshots;
  the order is the suggested order.
- `feature-graphic.png` - 1024x500, required for the store listing.
- `icon-512.png` - 512x512 full-bleed icon (Play applies its own rounded mask and shadow; do not upload one with transparent corners).
- `make-store-assets.ps1` - rebuilds the images from raw 1080x2400 emulator screenshots.

## Before you upload - things Play will insist on

1. **Release signing.** ApiScout.apk is signed with the debug key. Play needs a release key: create one with
   `keytool`, put its details in `android/key.properties`, reference it from `android/app/build.gradle.kts`, and build.
2. **App bundle, not APK.** New apps must be uploaded as an `.aab`: `flutter build appbundle --release`.
3. **Application id.** The bundle uses `com.kramn.apiscout_mobile`. It cannot be changed after the first upload.
4. **Third-party logos.** The screenshots show provider logos (NASA, GitHub, TheCocktailDB) that the app loads from the
   providers' own sites. This is normal for a directory app, but you can pick screenshots without them if you prefer.
5. The "3,591" in the first caption is today's count. Update it, or make it "3,500+", if the catalogue grows before release.
