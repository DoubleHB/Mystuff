# ApiScout Mobile - free API finder for Android (Flutter)

The Android sister of the desktop app in `C:\Claude\ApiScout` (which is unchanged by it). Same job: scan the public
API directories, merge and categorise a few thousand free APIs, and show whether each needs a key - with the provider's
own published demo key where there is one, or how to get a key.

## What it does (version 1.0 - "core finder")

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
- `lib/tester.dart` - Try it: send, format, cURL
- `lib/app_state.dart` - catalogue, filters, favourites, theme (ChangeNotifier)
- `lib/main.dart`, `detail_page.dart`, `widgets.dart` - the screens
- `test/apiscout_test.dart` - self-check (`--dart-define=LIVE=true` adds the real scan)

## Build

```
powershell -ExecutionPolicy Bypass -File C:\Claude\ApiScoutMobile\build-apk.ps1        # -> ApiScout.apk
```

Flutter 3.47 is in `C:\Claude\tools\flutter` (not on PATH; the script sets everything). The APK is signed with the
debug key - fine for installing on your own phone (allow "install unknown apps"), not for the Play Store.
App id `com.kramn.apiscout_mobile`.

Not in this version (desktop only): docs scanner, My key vault, tags, notes, collections, request history and
variables, C# generation, compare, what changed, link check.
