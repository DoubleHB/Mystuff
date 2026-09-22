# API Free - free API finder

Find free public APIs and know, before you sign up for anything, whether you can call one right now and with
what key. API Free scans the internet's public API directories, merges them into one catalogue of about 3,600
APIs in 42 categories, and for each one shows the provider's own published demo key where there is one, how the
key is sent, what is free, and where to get your own key. Then you can test the API from the app.

Two apps, one set of rules:

| | Where | What |
|---|---|---|
| **Desktop** | [`ApiScout/`](ApiScout/) | Windows, WPF on .NET 10. The full tool: sources you choose, docs scanner with OpenAPI reading, request tester with history and C# class generation, compare, collections, what-changed, link check, update check. [Read more](ApiScout/README.md). |
| **Android** | [`ApiScoutMobile/`](ApiScoutMobile/) | Flutter. The finder in your pocket: the same catalogue and rules, demo keys, Try it with saved requests and a response tree, docs scan with OpenAPI, API of the day as a notification, weekly rescan on Wi-Fi. [Read more](ApiScoutMobile/README.md) · [Release notes](ApiScoutMobile/RELEASE-NOTES.md). |
| **Self-check** | [`ApiScout.Tests/`](ApiScout.Tests/) | The desktop's 190-odd checks, the `knowledge.json` export the phone ships, and the cross-app import/export fixtures. |

The desktop app is the source of truth for the category rules, keyword rules and provider key hints; it exports
them as `knowledge.json`, which the phone app ships as an asset. Both apps categorise identically, and the two
exchange favourites, tags, notes, collections, keys and saved requests through one export file whose secrets
are encrypted with a passphrase.

## Keys policy

API Free shows only demo keys that providers print in their own public documentation - never leaked or private
keys. Keys you save are encrypted on your device (Windows DPAPI on the desktop, the Android keystore on the phone),
never appear in what you copy or share, and leave a device only inside an export's passphrase-protected section.

## Releases

- Desktop: tags `desktop-vX.Y.Z`; each release carries `ApiScout-X.Y.Z-portable.zip` - unzip and run `ApiScout.exe`,
  nothing to install. In the app, About → Updates takes this repository's `owner/repo` and finds the newest one.
- Android: tags `mobile-vX.Y.Z`; each release carries `ApiScout.apk` (the Play bundle is uploaded to Play, not here).

## Building

- Desktop: `ApiScout/publish.ps1` (needs the .NET 10 SDK). Self-check: `dotnet run --project ApiScout.Tests -c Release -- --offline`.
- Android: `ApiScoutMobile/build-apk.ps1` (Flutter 3.47; `-Bundle` adds the Play bundle).
- CI ([`.github/workflows/ci.yml`](.github/workflows/ci.yml)) builds the desktop and runs the self-check on Windows,
  and runs `flutter analyze` and `flutter test` for the phone on Linux, on every push.

## Layout of this repository

`ApiScout/`, `ApiScoutMobile/` and `ApiScout.Tests/` are git subtrees of the three working folders of the same
names, brought together so the whole project lives in one place with every history intact. `sync.ps1` pulls
their latest commits into this repository and pushes; `release.ps1` creates the GitHub releases for the current
versions with the built files.
