# ApiScout - free API finder

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
  with a working example when ApiScout knows one, otherwise paste an endpoint from the docs. Headers one per line;
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
- **Brand logos**: provider site icons in the grid and detail header (Google s2, then DuckDuckGo; only the domain is
  sent; cached in `logos\`; coloured initial when there is none). Toggle in the Sources popup.
- **Check links**: tests whether every listed docs site still answers (Online / Restricted / Down + latency);
  "Online only" filter afterwards.
- **Copy everywhere**: Copy buttons beside every value; Ctrl+C (selected rows - multi-select pastes into Excel),
  Ctrl+K demo key, Ctrl+U docs URL; right-click menu; copy one API as Details / Markdown / JSON / cURL / C#;
  copy the whole filtered list as Markdown / CSV / JSON / URLs; Export… to file.
- Favourites (Ctrl+D), notes, and a "My key" vault per API (DPAPI-encrypted for your Windows account).
- Search (Ctrl+F), auth filter, HTTPS only, CORS enabled, light/dark.

## Keys policy

Demo keys in `Services/KeyKnowledge.cs` are only ones the provider publishes in its own docs (NASA `DEMO_KEY`,
Alpha Vantage `demo`, TheSportsDB `123`…); each was verified against the live API on 2026-09-21.
ApiScout never looks for leaked or private keys - the docs scan only reads the provider's own public pages.

## Layout

- `Services/Sources.cs` - one fetcher per directory; `MarkdownListParser.cs` - generic awesome-list table/bullet parser
- `Services/Scanner.cs` - parallel run, merge, de-dupe key; `Categoriser.cs` - category rules + keyword classifier
- `Services/KeyKnowledge.cs` - demo keys, sign-up links, generic how-to per auth type
- `Services/DocsScanner.cs`, `ApiTester.cs`, `JsonToCSharp.cs`, `LineDiff.cs`, `LogoService.cs`, `LinkChecker.cs`, `Exporter.cs`, `Store.cs`
- `ViewModels/MainViewModel.cs`, `ApiRow.cs`; `MainWindow.xaml`

Data: `%LOCALAPPDATA%\ApiScout` (settings.json, catalog.json, userdata.json, docs-scans.json, test-history.dat, logos\, apiscout.log).
`APISCOUT_DATA` env var redirects it (used for testing).

## Build / test / publish

```
dotnet run --project C:\Claude\ApiScout.Tests -c Release            # 130 checks (add -- --offline to skip live ones)
dotnet publish C:\Claude\ApiScout\ApiScout.csproj -c Release -o C:\Claude\ApiScout-App
```

Close ApiScout.exe before republishing. Git: local repo (portable git at `C:\Claude\tools\MinGit\cmd\git.exe`); the
self-check project `C:\Claude\ApiScout.Tests` sits outside it.
