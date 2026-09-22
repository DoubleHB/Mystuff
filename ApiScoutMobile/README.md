# API Scout Mobile - free API finder for Android (Flutter)

The Android sister of the desktop app in `C:\Claude\ApiScout` (which is unchanged by it). Same job: scan the public
API directories, merge and categorise a few thousand free APIs, and show whether each needs a key - with the provider's
own published demo key where there is one, or how to get a key.

## What it does (version 1.5)

- **Scan** the same five keyless directories as the desktop default (public-apis, public-api-lists, publicapis.dev,
  freepublicapis.com, n0shake). Downloads run in parallel; parsing, merging and categorising run in a background
  isolate, so the screen stays smooth. The catalogue is cached on the phone.
- **Browse**: search, a category drawer with counts, filters (what you need before calling it, how much is free,
  HTTPS only, CORS), favourites, "Demo key included".
- **Details**: docs link (copy / open), auth, HTTPS, CORS, health; Keys & access with the demo key, how the key is
  sent, a working example request, how to get a key and the sign-up link - each with a copy button.
- **Try it**: GET / POST / PUT / PATCH / DELETE, headers, body, pretty-printed JSON response, copy response,
  copy as cURL. A request that carries headers does not follow redirects (a key must not travel to another host).
- **Copy as** details / Markdown / cURL. Light, dark or system theme.
- **More about this API** (1.1): "At a glance" - what the known facts mean for you - then "In the provider's words":
  summary, feature list and section names read from the docs page (the README for an API on GitHub). Nothing is
  invented; a page that builds itself with JavaScript simply yields little. Parsing runs in an isolate on the first
  900,000 characters (Dart patterns have no time-out).
- **API of the day** (1.1): one API that answers without sign-up, the same pick as the desktop makes from the same
  catalogue (same day number, same ordering). Shown at the top of the list while nothing is being searched or filtered.
- **Tags, notes, collections** (1.1): on the detail page. Tags appear as filter chips above the list and match the
  search; collections appear in the category drawer (long-press one to delete it).
- **My key** (1.1): your own key per API, stored with `flutter_secure_storage` = encrypted under the Android keystore,
  this phone only (`allowBackup="false"`, so it is not in Android's cloud backup either). Only the *names* of the APIs
  with a key are held in memory. `{key}` in a Try it URL / header / body is filled in at the moment of sending
  (URL-escaped in the URL); copied cURL keeps `{key}`, and a response that echoes the key shows `{key}` again.
- **Request variables** (1.2): a Variables box in Try it (`name = value` per line, saved per API). `{name}` in the
  URL (percent-encoded), headers and body is filled in when sending; `{today} {yesterday} {tomorrow} {now} {timestamp}`
  are always there; a request with an unfilled `{…}` is not sent. New placeholders in the request offer a "+ name ="
  chip. Copied cURL has the variables filled in but keeps `{key}`. Same rules as the desktop's `RequestVariables`.
- **Request history** (1.2): the last 8 requests per API with their responses (History button in Try it; tap one to
  load it back). Stored as typed - so never the key or a variable's value - in `history.bin`, sealed with AES-256-GCM
  under a random key kept in the Android keystore (the desktop uses DPAPI for the same file).
- **Scan docs for key info** (1.2): port of the desktop `DocsScanner` - reads the docs page and the pricing page it
  links to (provider's own site only): sign-up links, sample keys / placeholders, example endpoints (placeholder keys
  become `{key}`; "Try" loads one into Try it and sends plain GETs), free-tier / rate-limit sentences, pricing.
  Results are kept in `docscans.json` and fill in "how much is free" where the directories say nothing. No OpenAPI
  spec reading (none of the five sources gives a spec URL). One deliberate difference: a header value only keeps a
  second word after an auth scheme ("Bearer abc"), so "X-Api-Key: abc with every call" yields "abc".
- **Import / Export** (1.1): the phone reads and writes **the desktop app's own export file** (About → Export… /
  Import… on the PC; `Services/Backup.cs`): favourites, tags, notes, collections in the clear, saved keys and request variables inside
  AES-256-GCM under PBKDF2-SHA256 (310,000 rounds) from a passphrase - or left out. Merge rules are the desktop's:
  nothing already there is overwritten. The desktop needed no change for this.
- 1.2.1: choosing a category, tag, search or filter scrolls the list back to the top (`AppState.listVersion`);
  starring, tagging or a rescan keep the scroll position.
- **1.3.0 - the ledger look.** Same features, new presentation: Manrope for text and JetBrains Mono for hosts,
  keys, URLs and responses (static instances bundled in `assets/fonts`, SIL OFL); white (or near-black) ground with
  hairline dividers instead of cards; the current category as the page headline; colour only in the status words and
  the teal accent; the search bar and status slide away as the list scrolls and return on a flick up; edge to edge;
  skeleton rows while the first scan runs; the logo flies from the row into the detail header. The detail page is
  five tabs - Overview, Keys, Try it, Docs, Mine - so Try it is one tap away instead of a scroll past Keys and My key.
  "Try" on a found endpoint and "Scan docs" switch tabs; long "how to get a key" text is folded behind "Show the steps".
  `ledgerTheme()` in `main.dart` holds every colour and shape; `widgets.dart` has `mono`, `tonalStyle`, `FoldedText`, `SkeletonRows`.
- 1.3.1: the filters live in a chip row under the search (category ▾ opens the drawer; Auth ▾ and Free ▾ are menus;
  HTTPS and CORS toggle; Clear appears while anything is set; the user's #tags follow a divider) - the filter sheet is
  gone. Swipe a row right to star it, left to file it in a collection (`SwipeActions`, a Dismissible that always
  slides back).
- 1.3.2: an A–Z rail at the right edge of any list of 40 or more APIs (`az_rail.dart`): tap or drag a letter to jump
  to the first API starting with it, a bubble shows the letter under your finger, and the letter of the row at the
  top is lit while you scroll. Rows are now a fixed 76 dp (tags moved onto the status line; an API with no
  description shows its category there), which is what lets the rail land on a row by index. Collections have no
  rail - they keep the order you built them in.
- 1.3.3: the detail tabs carry counts - Keys (demo key, how it is sent, example request, sign-up link, my key),
  Try it (requests in the history), Docs (items the docs scan found), Mine (tags + note + collections). Zero shows
  nothing, so a bare tab name means there is nothing behind it yet.
- 1.3.4: a share button on the detail page hands the Markdown copy to Android's share sheet (`share_plus`). It
  carries the provider's published demo key where there is one, never your own key.
- **1.4.0 - daily-use polish.**
  - Search forgives a typo: a word of four letters or more also matches a word one edit away (two from eight
    letters), so "wether" finds weather and "cocktial" the cocktail APIs (`search.dart`, `ApiView.matchesWord`).
    Searches that led somewhere (search pressed, or a result opened) are offered under the box while it is focused
    and empty; long-press one to forget it.
  - A response page (`response_page.dart`): find with next/previous and the match count, and JSON as a tree whose
    nodes fold (tap a `{…}` or `[…]`, long-press a value to copy it); a query shows only the matching lines and the
    way to them. "Tree & search" above the response in Try it opens it.
  - Auth presets in Try it: Bearer, X-Api-Key, ?api_key= and RapidAPI write the usual header or query parameter
    with `{key}` in it (`withHeaderLine`, `withQueryParam` in `tester.dart`).
  - Large text and TalkBack: list rows, the header and the rail grow with the phone's text size
    (`rowExtentFor`); the rail letters are buttons for a screen reader, swipe actions are in the row's actions
    menu, filter pills report their state, brand tiles are decoration.
  - "API of the day" as a notification at 9:00 (menu; Android asks once). The next seven mornings are scheduled
    from the phone's catalogue with the same pick the card makes (`notify.dart`, `flutter_local_notifications`);
    tapping one opens the API.
  - Deep links: the shared text ends with `apiscout://open/api/<key>`, which opens that API on a phone with
    API Scout (Flutter's own deep-link handling, `links.dart`; an API not in the phone's catalogue gets a page saying
    so). There is no https domain to verify, so the link is plain text in most chat apps: copy and open it.
  - Rescan weekly on Wi-Fi (menu): Android WorkManager runs the same scan while the app is closed
    (`background.dart`, `workmanager`; unmetered network, battery not low). A thin result (fewer than three
    sources, or under 500 APIs) is dropped rather than replacing a good catalogue.
  - What changed: every scan, in the app or in the background, leaves `changes.json` - new, no longer listed, and
    changed category or auth (`changes.dart`). A strip above the list says "12 new, 3 gone since the last scan ·
    See" until dismissed; the menu keeps the last one.
  - Cache versioning: the cached catalogue remembers a hash of `knowledge.json`. An app update with new rules
    re-categorises the cached entries at start-up (`recategorise`), no scan needed.
- **1.5.0 - saved requests, OpenAPI, Play-ready.**
  - Saved test requests, the desktop's way: a Try it request edited away from the suggested one is remembered for
    that API when you press Test this API, comes back next time, and Reset brings the suggested one back
    (`TestRequest` in `user_data.dart`; `requests.bin`, sealed like the history). They travel in the export's
    encrypted part as `TestRequests`, so the desktop's Import reads them and the phone reads the desktop's.
  - OpenAPI spec reader in the docs scan (`openapi.dart`): the page's spec link (an href, or the URL a Swagger UI
    script is given) or the usual places (`/openapi.json`, `/swagger.json`, `/v3/api-docs`, …) on the docs site and
    the API host; JSON or YAML (`yaml` package). Adds the spec (title, version, path count), up to five GET
    endpoints without parameters, and the auth schemes - the desktop's `ReadSpec`, which only gets a spec URL from
    a source; the phone finds it.
  - Release signing: `make-release-key.ps1` makes `android/keystore/apiscout-release.jks` + `android/key.properties`
    (both ignored by git; back them up); `build.gradle.kts` signs with them when present, else the debug key.
    `build-apk.ps1 -Bundle` also builds `ApiScout.aab` for Play. A phone with a debug-signed build must uninstall
    it first (export your data before, import after).
  - Play screenshots redone with the 1.5 look (`store/`).

## One source of truth

`assets/knowledge.json` is **exported from the desktop app** (`dotnet run --project C:\Claude\ApiScout.Tests -- --export-knowledge …`):
the category rules, keyword rules and ~70 provider key hints. The .NET regex patterns are used as they are (Dart's
unicode mode understands the same constructs), so both apps categorise identically - the live test scans to the same
3,591 APIs / 20 demo keys as the desktop. Edit rules in `Categoriser.cs` / `KeyKnowledge.cs`, never in the JSON.

Keys policy is the desktop's: only demo keys the providers print in their own docs; nothing leaked or private.

## Layout

- `lib/models.dart` - ApiEntry, KeyHint, Catalogue
- `lib/knowledge.dart` - loads the rules; categorise, find a key hint, access level; `ApiView` (what the UI shows)
- `lib/sources.dart` - the five sources, awesome-list table parser, JSON parsers, merge / de-dupe key, isolate entry point
- `lib/tester.dart` - Try it: send, format, cURL, {key};  `lib/variables.dart` - request variables
- `lib/docs_scanner.dart` - Scan docs for key info;  `lib/openapi.dart` - find and read an OpenAPI / Swagger spec
- `lib/insight.dart`, `insight_page.dart` - More about this API
- `lib/user_data.dart` - favourites/tags/notes/collections, the desktop export format and its encryption (pointycastle)
- `lib/vault.dart` - My key storage (Android keystore)
- `lib/app_state.dart` - catalogue, filters, favourites, theme (ChangeNotifier)
- `lib/main.dart`, `detail_page.dart`, `widgets.dart`, `az_rail.dart`, `response_page.dart`, `changes_page.dart` - the screens
- `lib/search.dart` (typo-tolerant search, recent searches), `json_tree.dart` (response tree), `changes.dart` (scan diff),
  `links.dart` (apiscout:// links), `notify.dart` (API of the day notification), `background.dart` (weekly rescan)
- `test/apiscout_test.dart` - self-check (`--dart-define=LIVE=true` adds the real scan). `test/fixtures/desktop-export.json` is a real
  file from the desktop's Backup code (`ApiScout.Tests -- --phone-fixture <file>`, passphrase in the test); the other direction is
  `ApiScout.Tests -- --phone-import <file> <passphrase>` on the file the test writes when APISCOUT_PHONE_EXPORT is set.

## Build

```
powershell -ExecutionPolicy Bypass -File C:\Claude\ApiScoutMobile\build-apk.ps1        # -> ApiScout.apk
```

Flutter 3.47 is in `C:\Claude\tools\flutter` (not on PATH; the script sets everything). App id `com.kramn.apiscout_mobile`.

Signing: run `make-release-key.ps1` once (it writes `android/keystore/apiscout-release.jks` and `android/key.properties`,
both kept out of git - back them up, a lost key cannot be replaced for the same app on Play). With those present the
APK and the bundle are release-signed; without them the debug key is used, which is fine for your own phone (allow
"install unknown apps") but not for Play. `build-apk.ps1 -Bundle` adds `ApiScout.aab`, the file Play takes; the
listing text and images are in `store/`.

Not in this version (desktop only): C# generation, compare, link check, drag-to-reorder and Test all for collections.
