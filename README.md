# API Free - find and test public APIs

WPF (.NET 10, Fluent theme) desktop tool that scans the internet's public API directories, merges and
categorises what it finds, and tells you for every API whether you need a key - showing the public demo
key when the provider publishes one, or how and where to get a free key when not. Everything is copyable.

## What it does

- **Scan the internet** (F5): fetches every ticked source in parallel (~1.5 s), de-duplicates by docs URL and
  folds every directory's own category names into ~42 canonical categories. ~3,600 unique APIs by default.
- **Sources** (button, top right): public-apis, public-api-lists, publicapis.dev, freepublicapis.com, n0shake -
  plus opt-in APIs.guru (2,500 OpenAPI specs) and "Discover more lists on GitHub" (topic search, keeps READMEs
  that really contain API tables). Add your own list URLs (GitHub repo / README / JSON), one per line.
  Measured 2026-09-21: APIs.guru adds ~2,480 unique APIs, clean but about half are Azure/AWS/Google Cloud
  management APIs (auth always "Unknown" until a docs scan reads the spec) - hence off by default. GitHub
  discovery adds up to ~1,000 (it depends on what the topic search returns; keyless search is limited to 10
  calls/minute). Discovery is gated: tables must have an Auth/HTTPS/CORS column, affiliate links are dropped,
  and a README is skipped when one host makes up over 35% of it or it is a translation of another list.
- **Keys & access** panel: demo key (big, with a Copy key button), how the key is sent, a working example
  request, step-by-step how to get a key, and the sign-up link. Badges in the grid: Demo key / Open /
  Key optional / Free key / Key needed / OAuth / ?.
- **Scan docs for key info**: reads the API's own docs page (and OpenAPI spec when known) for sign-up links,
  free-tier / rate-limit sentences, the auth scheme, and sample keys printed in the docs' examples
  (placeholders such as YOUR_API_KEY are labelled as such).
- **Free access filter**: Full free access / Free tier (limited) / Demo / trial only / Not stated - shown under the
  key badge in the grid and explained in the Keys card ("What is free"). It is an estimate, worked out in this
  order: curated provider knowledge, the directory's own paid/open flag (n0shake), "no key" = fully free, wording
  in the description (free plan, trial, paid…), then the result of a docs scan. Included in CSV/JSON/text copies.
- **Test this API** (Ctrl+T): sends the request in the "Try it" card - GET, POST, PUT, PATCH or DELETE - pre-filled
  with a working example when API Free knows one, otherwise paste an endpoint from the docs. Headers one per line;
  POST/PUT/PATCH get a body box (Tidy JSON button) and the Content-Type is set from the body (JSON, form, XML,
  text) unless you give your own. Shows status, time, type, size and the pretty-printed JSON with Copy response;
  Copy as cURL copies the whole request. `{key}` in the URL, a header or the body inserts the key saved under
  My key (it stays a placeholder in the cURL copy). Edited requests are remembered per API (encrypted, as they
  may hold a key). 401/403/405/415/429/HTML get a plain hint.
- **After a test**: **C# classes** generates System.Text.Json classes that fit the JSON (nested classes, lists,
  int/long/double, DateTimeOffset, nullable for nulls and sometimes-missing properties, `[JsonPropertyName]`) with a
  Copy classes button. **History** keeps the last 8 results per API (encrypted file `test-history.dat`): pick one to
  see it again, **Compare with latest** shows a line diff (+/-), **Reuse request** puts its request back.
- **Pricing page**: the docs scan follows the provider's own Pricing / Plans link and reports what it says about
  free use (free plan, $0, trials, "no free plan mentioned"); this feeds the free-access level.
- **Scan docs for 'Not stated'** (filter row): batch docs + pricing scan of every listed API whose free access is
  unknown, 6 at a time, Esc / Stop to cancel; results are cached in docs-scans.json. Narrow the list first
  (category / search) - the whole catalogue is ~1,500 pages.
- **Example endpoints**: the docs scan also collects URLs that look like API calls (api. host, /api/ or /v1/ path,
  query string, .json; scheme-less ones on the docs' own site; "GET /path" lines; GET paths from an OpenAPI spec).
  Each gets a **▶ Test** button that loads it into Try it and sends it (POST etc. wait for a body). Placeholder keys
  such as `apikey=YOUR_KEY` become `apikey={key}`.
- **New since last scan**: every API carries a first-seen date. After a re-scan the status bar says "N new, M gone",
  new ones get a NEW pill for 14 days and a "🆕 New (last 14 days)" category. The very first scan is the baseline.
  **Re-scan automatically** (Sources popup): Never / Daily / Weekly (default) - checked on start-up and every 30
  minutes while open. `ApiScout.exe --scan` does a headless re-scan (for Task Scheduler) and logs the result.
- **Also when API Free is closed** (Sources popup): registers the per-user Windows scheduled task "ApiScout
  background scan" (09:00 daily or Mondays, catches up after a missed start, no admin rights) that runs
  `ApiScout.exe --scan` and shows a Windows notification naming the new APIs - click it to open API Free.
  `ApiScout.exe --background-scan=weekly|daily|off` does the same from a script. Test copies (APISCOUT_DATA) refuse.
- **Brand logos**: provider site icons in the grid and detail header (Google s2, then DuckDuckGo; only the domain is
  sent; cached in `logos\`; coloured initial when there is none). Toggle in the Sources popup. APIs hosted on
  GitHub / GitHub Pages show the repo owner's avatar and "github.com/owner"; RapidAPI publishes no provider logo,
  so those keep RapidAPI's icon with the label "RapidAPI · by provider". A docs scan also picks up the docs page's
  own icon (apple-touch-icon / rel=icon) for sites the icon services do not know.
- **Dashboard** (tiles above the list, can be hidden under Sources): APIs by free-access level with share bars, new
  this week / in 14 days, rate limited right now, and the share of docs links that were online in the last link
  check. Every tile is a shortcut: click to filter, click again to clear. "⏳ Rate limited now" is also a category.
- **C# client** (Try it card, beside "C# classes"): one small typed `HttpClient` class from every request in the
  API's history that worked - a method per distinct request, numeric path segments and query values as parameters
  defaulting to what was tested, JSON bodies as request classes, response classes with unique names. `{key}` and the
  provider's demo key become a constructor parameter, so the saved key never appears in the code. With more than
  one successful test a tick list asks which become methods (default: the newest test of each distinct request).
  "Save as .cs…" writes the classes or the client to a file.
- **API of the day** (right end of the dashboard): a keyless or demo-key API with a known working example request,
  a different one each day - ▶ Test sends it, Details opens it, ↻ picks another.
- **Collections**: named groups of APIs ("Weather side project"). Right-click rows → "Add the selected rows to a
  collection…" (Ctrl+E) or the button in the My key & notes card. Each collection is a page of My shortlist (Page
  box, top right) with the same cards plus Remove (R), Rename and Delete. Every page - the shortlist too - has
  "Copy page as Markdown" and "Copy / Save C# client": one file with a typed client per API that has a test that
  worked and a `<Name>Apis` class that builds them all from one HttpClient. Collections travel with Export / Import.
- **Updates** (About): compares the running version with the newest `v*` tag of an update source - a GitHub
  `owner/repo`, a folder holding the git repository (tags are read straight from `.git`), or a `latest.json`
  file / URL. Empty = the repository the build came from. Checked quietly once a day at start-up (⬆ on About).
- **More about this API** (title card, Ctrl+I): "At a glance" - what the known facts mean for you (key needs, how
  much is free, HTTPS, CORS, health, spec, working example) - and "In the provider's words": the summary, feature
  list and section headings read from the docs page, or from the README when the API lives on GitHub. Nothing is
  invented; a page that builds itself with JavaScript simply yields little. Copy as Markdown.
- **Request variables** (Try it): `name = value` lines per API; `{name}` in the URL (URL-encoded), headers or body is
  replaced when the request is sent - by Test, Test all and the API of the day alike. Always available: `{today}`,
  `{yesterday}`, `{tomorrow}`, `{now}`, `{timestamp}`. A request with an unfilled `{…}` is not sent but says what is
  missing; endpoints from a docs scan add their `{id}`-style names to the box. In the C# client a path variable
  becomes a parameter and the others turn into parameter defaults. "Copy as cURL" fills variables, never `{key}`.
  They are exported with the keys (passphrase only), since a variable may hold something private.
- **Tray mode** (Sources → "Keep API Free in the tray when minimised"): minimising hides the window; the tray icon
  reopens it and has Scan now, API of the day, My shortlist and Exit. Re-scans keep running and new APIs arrive
  as a notification (click → the New category). Starting API Free again brings the hidden window back.
- **Update and restart** (About, portable copy only): fetches the portable zip the update check found - from
  `latest.json` (with its sha256), from `..\ApiScout-Dist` when the source is the repo folder, or from the GitHub
  release of the tag (asset `*portable.zip`; a private repository needs a read-only token, stored DPAPI-encrypted
  and only ever sent to api.github.com). The running exe is renamed to `ApiScout.exe.old`, the new one put in its
  place, the app restarts and deletes the old one. The ordinary multi-file build is updated by `publish.ps1`.
- **Test all** (My shortlist and every collection page): sends each card's saved or suggested request, three at a
  time, then one line: passed, failed (names), skipped because rate limited or because there is no request yet.
- **Reorder** collection cards by dragging one onto another, or Ctrl+← / Ctrl+→ on the selected card.
- **Tour**: four callouts (Scan, dashboard, Keys and Try it, My shortlist) on the very first start; Esc skips,
  About → "Show the tour" brings it back.
- **Docs scan card**: "Open in browser" under the scan result, for the pages that block automated readers.
- **What changed** (status bar, Ctrl+H): for each of the last 12 scans - manual, automatic or background - the APIs
  that are new, gone, or have a different auth / free-access level (`changes.json`, `Services/ChangeLog.cs`).
  "Show in list" jumps to the API, "Copy as Markdown" exports the report; it updates live when a scan finishes.
- **Keyboard**: F5 scan, Esc stop / back, Ctrl+F search, Ctrl+L shortlist, Ctrl+T test, Ctrl+D favourite, Ctrl+K key,
  Ctrl+U docs URL, Ctrl+G tag, Ctrl+M compare, F1 About. Shortlist cards: arrows, Enter details, T, K, O, U, D.
  Compare: F5 measure, Ctrl+Shift+C copy as Markdown, Ctrl+1-4 open docs, arrows / PgUp / PgDn scroll.
- **My shortlist** (header button, Ctrl+L): favourites and tagged APIs as cards - brand, key badge, what is free, demo key /
  "my key saved", tags, note, rate-limit notice and the last test result - with Test, Copy key, Docs and Details.
- **Rate-limit memory**: a 429 (or "0 requests left") is remembered per API with the time it should work again, taken
  from Retry-After / (X-)RateLimit-Reset, else assumed to be an hour and marked as an estimate. Shown in the Try it
  card, as a ⏳ chip in the grid, on shortlist cards and in Compare; cleared by the next successful call. Successful
  responses also report "Requests left: 38 of 40" when the API sends those headers.
- **About** (header button): version, data summary, data folder, and **Export my data… / Import…** to move favourites,
  tags and notes to another PC. Saved keys and edited test requests are tied to the Windows account (DPAPI), so they
  only go into the file when you give a passphrase - then AES-256-GCM under a PBKDF2-SHA256 key (310,000 rounds).
  Import merges and never overwrites what is already there; a wrong passphrase changes nothing.
- **Tags**: your own comma-separated labels per API (My key & notes card), shown as a 🏷 chip in the grid, searchable,
  with a tag filter beside the other filters; right-click → "Tag the selected rows…" labels a multi-selection.
  Included in the text / CSV / JSON copies.
- **Compare**: select 2-4 rows (Ctrl+click) → "Compare selected" (status bar or right-click) for a side-by-side
  window: what each needs, what is free, demo key, how to get a key, HTTPS/CORS, health, example, sources, tags,
  notes. "Measure now" checks each docs site and sends each known example once; "Copy as Markdown" exports it.
- **Check links**: tests whether every listed docs site still answers (Online / Restricted / Down + latency);
  "Online only" filter afterwards.
- **Copy everywhere**: Copy buttons beside every value; Ctrl+C (selected rows - multi-select pastes into Excel),
  Ctrl+K demo key, Ctrl+U docs URL; right-click menu; copy one API as Details / Markdown / JSON / cURL / C#;
  copy the whole filtered list as Markdown / CSV / JSON / URLs; Export… to file.
- Favourites (Ctrl+D), notes, and a "My key" vault per API (DPAPI-encrypted for your Windows account).
- Search (Ctrl+F), auth filter, HTTPS only, CORS enabled, light/dark.

## Safety notes

- "Test this API" follows redirects itself: another host never receives your headers (where a key usually sits), and
  a redirect that would re-send a body elsewhere is shown instead of followed. No cookies are kept between requests.
- The scanners (docs, links, logos) refuse localhost and private addresses - the directories are lists anyone can edit.
- A scan in which a source failed keeps what only that source knew, so nothing is "gone" now and "new" next week.
- Files are written to a temp file, flushed, then swapped in; an unreadable settings / userdata file is copied aside
  (`*.unreadable-<time>`) before anything can overwrite it. CSV / Excel copies defuse leading `= + - @`.
- The scheduled `--scan` does nothing while a window on the same data folder is open (that window re-scans itself),
  and a second window on the same folder just brings the first one forward.
- Nothing typed is lost: a note, tags or a key being typed are committed when the window closes, when a scan
  replaces the rows and when you move to another API; a re-scan also carries the Try it request, response and
  history over to the new rows. The grid keeps its sort when filters change.
- An automatic re-scan that fails (offline) or is stopped waits six hours before trying again.

## Keys policy

Demo keys in `Services/KeyKnowledge.cs` are only ones the provider publishes in its own docs (NASA `DEMO_KEY`,
Alpha Vantage `demo`, TheSportsDB `123`…); each was verified against the live API on 2026-09-21.
API Free never looks for leaked or private keys - the docs scan only reads the provider's own public pages.

## Layout

- `Services/Sources.cs` - one fetcher per directory; `MarkdownListParser.cs` - generic awesome-list table/bullet parser
- `Services/Scanner.cs` - parallel run, merge, de-dupe key; `Categoriser.cs` - category rules + keyword classifier
- `Services/KeyKnowledge.cs` - demo keys, sign-up links, generic how-to per auth type
- `Services/DocsScanner.cs`, `ApiTester.cs`, `JsonToCSharp.cs`, `ClientGenerator.cs`, `LineDiff.cs`, `LogoService.cs`, `LinkChecker.cs`, `Exporter.cs`, `Store.cs`
- `Services/ScheduledScan.cs` - the Windows scheduled task; `Services/Backup.cs` - export / import
- `Views/CompareWindow.cs` - compare window + input prompt, `Views/AboutWindow.cs`, `ChangesWindow.cs`, `ClientDialog.cs` (all code only)
- `ViewModels/MainViewModel.cs`, `ApiRow.cs`; `MainWindow.xaml`

Data: `%LOCALAPPDATA%\ApiScout` (settings.json, catalog.json, changes.json, userdata.json, docs-scans.json, test-history.dat, logos\, apiscout.log).
`APISCOUT_DATA` env var redirects it (used for testing).

## Build / test / publish

```
dotnet run --project C:\Claude\ApiScout.Tests -c Release            # 218 checks (add -- --offline to skip live ones)
powershell -ExecutionPolicy Bypass -File C:\Claude\ApiScout\publish.ps1   # ..\ApiScout-App + the portable zip (-SkipApp: zip only)
```

`publish.ps1` also writes `C:\Claude\ApiScout-Dist\ApiScout-<version>-portable.zip`: one self-contained single-file
`ApiScout.exe` (about 66 MB, no .NET install needed), `README.md` and `portable.txt`. While `portable.txt` sits beside
the exe, all data lives in a `data` folder next to it. `latest.json` beside the zip is an update source.

Close ApiScout.exe before republishing. Git: local repo (portable git at `C:\Claude\tools\MinGit\cmd\git.exe`); the
self-check project `C:\Claude\ApiScout.Tests` sits outside it.
