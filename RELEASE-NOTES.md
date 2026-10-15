# API Free for Android - release notes

## 1.5.4

Three small things from the closed test.

- **The first scan starts by itself.** A fresh install no longer opens on an empty page with a button to find:
  the list fills in while the explanation shows. If the phone is offline the page says so, with the Scan button
  as before.
- **"Works right now" chip.** One tap in the filter row keeps only the APIs you can call this minute: no key
  needed, or a demo key the provider publishes. It combines with the other chips and the search.
- **The filters come back.** The category, the chips (Auth, Free, HTTPS, CORS, Works right now) and a tag filter
  are remembered between launches. The search box starts empty, as before. Clear works as it did.

## 1.5.3

- **The app's own typefaces, at last.** Manrope for text and JetBrains Mono for hosts, keys, URLs and responses
  have been part of the design since 1.3, but the font files in the app were in a web-only wrapper that Android
  skips without a word, so every screen was drawn in the phone's system font. The files are now real TrueType:
  headings look as designed and everything a machine would read lines up in a fixed-width face.
- Includes everything in 1.5.2.

## 1.5.2

A polish pass over every screen, from a walk through the app during the closed test.

- **Large text.** At a large system text size the list rows no longer touch their dividing lines, and the five
  tabs on an API's page scroll instead of cutting off "Overview".
- **Clearer empty list.** A search with no matches now says `Nothing matches "…"` and offers to clear the
  search, instead of blaming filters that were not set.
- **Auth fact.** An API whose directory entry does not say how it authenticates no longer shows "Unknown" in the
  header when the key knowledge knows the answer (NASA now reads "API key").
- **Try it.** The Headers and Variables boxes have short labels that fit a narrow screen, with the format as a
  hint; the spinner on the greyed "Sending…" button is visible.
- **One look.** Dialogs, the history sheet and menus use the same small corners and heavy titles as the rest of
  the app; every menu line starts at the same place, ticked or not; page titles share one style; About shows
  the icon.
- **Name.** Exports are now called `apifree-phone-<date>.json`, and requests the app sends identify it as
  API Free. Older export files still import.

## 1.5.1

The app is now called **API Free** (it was ApiScout; that name was already taken). Nothing else changes: the
package id, your data, keys and saved requests stay exactly where they were, and 1.5.1 installs over 1.5.0.

## 1.5.0

The phone app catches up with the desktop on saved requests and OpenAPI, and is ready for Google Play.

**New**
- **Saved test requests.** A request you change in Try it is remembered for that API the moment you press
  Test this API, and comes back next time. Reset brings the suggested request back. Saved requests are encrypted
  on the phone like the request history, and travel to and from the desktop app inside the export's
  passphrase-protected section.
- **OpenAPI spec in the docs scan.** Scan docs for key info now looks for the API's OpenAPI or Swagger spec: a
  link on the docs page, or the usual places on the docs site and the API host. JSON and YAML both read. The scan
  adds the spec, up to five GET endpoints you can try at once, and the auth schemes it declares.
- **Release signing and a Play bundle.** Builds are signed with a release key; `ApiScout.aab` is the file Play
  takes. The `store` folder holds the listing text and eight fresh screenshots.

**Fixed**
- The 9:00 "API of the day" notification never showed in 1.4.0: the notification plugin's receivers were missing
  from the manifest.

**Upgrading from a sideloaded 1.4.0 or earlier**
Those builds were signed with a debug key, so Android will not install 1.5.0 over them. Export your data in the
app (menu → Export my data…), uninstall, install 1.5.0, then Import.

## 1.4.0

Daily-use polish: typo-forgiving search with recent searches, a response page with search and a folding JSON
tree, auth presets in Try it, large-text and TalkBack support, "API of the day" as a 9:00 notification, deep
links in shared text, a weekly rescan on Wi-Fi while the app is closed with a "what changed" report, and
catalogue cache versioning. Also 1.3.2-1.3.4: the A-Z rail, tab counts on the detail page, and a share button.

## 1.3.0

The ledger look: Manrope and JetBrains Mono, a floating header with the category as headline, filter chips,
swipe to favourite or collect, and a tabbed detail page (Overview, Keys, Try it, Docs, Mine).

## 1.2.0

Request variables, request history (encrypted), Scan docs for key info.

## 1.1.0

More about this API, API of the day, tags, notes and collections, My key in the Android keystore, and
import/export of the desktop app's own backup file.

## 1.0.0

The core finder: scan five public API directories, browse 42 categories, search and filter, see demo keys and
how to get your own, Try it with GET/POST/PUT/PATCH/DELETE, favourites, light/dark/system theme.
