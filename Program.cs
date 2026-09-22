// ApiScout self-check. `--offline` skips the live checks.
using System.IO;
using ApiScout.Models;
using ApiScout.Services;
using ApiScout.ViewModels;

bool offline = args.Contains("--offline");
if (args.SkipWhile(a => a != "--export-knowledge").Skip(1).FirstOrDefault() is { } knowledgeFile)
{
    // The Android (Flutter) app is built with this file, so both apps categorise and explain keys the same way.
    var (byCategory, byKeyword) = Categoriser.Export();
    var (fullFree, trialOnly) = KeyKnowledge.ExportAccessHosts();
    var knowledge = new
    {
        exportedFrom = "ApiScout " + ApiScout.Views.AboutWindow.VersionText,
        categoryRules = byCategory.Select(r => new { pattern = r.Pattern, category = r.Category }),
        keywordRules = byKeyword.Select(r => new { pattern = r.Pattern, category = r.Category }),
        hints = KeyKnowledge.Hints.Select(h => new { hosts = h.Hosts, howTo = h.HowTo, signupUrl = h.SignupUrl, demoKey = h.DemoKey, keyUsage = h.KeyUsage, example = h.Example, keylessWorks = h.KeylessWorks }),
        fullFreeHosts = fullFree,
        trialOnlyHosts = trialOnly,
    };
    File.WriteAllText(knowledgeFile, System.Text.Json.JsonSerializer.Serialize(knowledge, new System.Text.Json.JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    Console.WriteLine($"knowledge written: {knowledge.hints.Count()} hints, {knowledge.categoryRules.Count()} + {knowledge.keywordRules.Count()} rules");
    return 0;
}
if (args.SkipWhile(a => a != "--phone-fixture").Skip(1).FirstOrDefault() is { } fixtureFile)
{
    // A real export file, made by the real Backup code, for the Android app's tests to open (passphrase below).
    var tempDir = Path.Combine(Path.GetTempPath(), "apiscout-fixture-" + Guid.NewGuid().ToString("N"));
    Environment.SetEnvironmentVariable("APISCOUT_DATA", tempDir);
    var s = new Store();
    s.User.Favourites.Add("url:catfact.ninja"); s.User.Favourites.Add("url:api.nasa.gov");
    s.User.Tags["url:api.nasa.gov"] = ["space", "Side Project"];
    s.User.Notes["url:api.nasa.gov"] = "APOD is the easy one — “curly quotes” and ünïcödé survive";
    s.User.Collections["Weather stuff"] = ["url:open-meteo.com", "url:api.nasa.gov"];
    s.User.Variables["url:api.nasa.gov"] = new(StringComparer.OrdinalIgnoreCase) { ["date"] = "2024-01-01", ["City"] = "New York" };
    s.SaveUser();
    s.SetMyKey("url:api.nasa.gov", "nasa-Key+with/odd=chars&42");
    s.SetMyKey("url:openweathermap.org/api", "0123456789abcdef0123456789abcdef");
    File.WriteAllText(fixtureFile, Backup.Export(s, "correct horse 42"));
    Directory.Delete(tempDir, true);
    Console.WriteLine("fixture written");
    return 0;
}
if (args.SkipWhile(a => a != "--phone-import").Skip(1).Take(2).ToArray() is { Length: 2 } phoneImport)
{
    // The other direction: a file the Android app exported, read by the real desktop import.
    var tempDir = Path.Combine(Path.GetTempPath(), "apiscout-fixture-" + Guid.NewGuid().ToString("N"));
    Environment.SetEnvironmentVariable("APISCOUT_DATA", tempDir);
    var s = new Store();
    var summary = Backup.Import(s, Backup.Read(File.ReadAllText(phoneImport[0])), phoneImport[1]);
    Console.WriteLine(summary);
    foreach (var k in s.User.MyKeys.Keys.Order()) Console.WriteLine($"key {k} = {s.GetMyKey(k)}");
    foreach (var c in s.User.Collections) Console.WriteLine($"collection {c.Key} = {string.Join(", ", c.Value)}");
    foreach (var t in s.User.Tags) Console.WriteLine($"tags {t.Key} = {string.Join(", ", t.Value)}");
    foreach (var n in s.User.Notes) Console.WriteLine($"note {n.Key} = {n.Value}");
    foreach (var v in s.User.Variables) Console.WriteLine($"variables {v.Key} = {string.Join(", ", v.Value.Select(p => $"{p.Key}={p.Value}"))}");
    Directory.Delete(tempDir, true);
    return 0;
}
if (args.Contains("--extra"))
{
    // Evaluation run for the two opt-in sources: how much do they add and how clean is it?
    foreach (var id in new[] { "apisguru", "github-discovery" })
    {
        var sw0 = System.Diagnostics.Stopwatch.StartNew();
        List<ApiEntry> found;
        try { found = await Sources.FetchAsync(id, CancellationToken.None); } catch (Exception ex) { Console.WriteLine($"{id}: FAILED {ex.Message}"); continue; }
        Console.WriteLine($"\n===== {id}: {found.Count:N0} raw entries in {sw0.Elapsed.TotalSeconds:F1}s");
        foreach (var g in found.GroupBy(e => e.Sources[0]).OrderByDescending(g => g.Count())) Console.WriteLine($"   source {g.Key}: {g.Count()}");
        var m = Scanner.Merge(found);
        Console.WriteLine($"   unique after merge: {m.Count:N0}");
        Console.WriteLine("   categories: " + string.Join(", ", m.GroupBy(e => e.Category).OrderByDescending(g => g.Count()).Take(14).Select(g => $"{g.Key} {g.Count()}")));
        Console.WriteLine("   hosts: " + string.Join(", ", m.GroupBy(e => Uri.TryCreate(e.Url, UriKind.Absolute, out var u) ? u.Host : "?").OrderByDescending(g => g.Count()).Take(12).Select(g => $"{g.Key} {g.Count()}")));
        Console.WriteLine($"   no description: {m.Count(e => e.Description.Length < 5)}, name > 60 chars: {m.Count(e => e.Name.Length > 60)}, auth unknown: {m.Count(e => e.Auth == AuthKind.Unknown)}");
        var rnd = new Random(7);
        foreach (var e in m.OrderBy(_ => rnd.Next()).Take(14)) Console.WriteLine($"   - [{e.Category}] {e.Name} | {e.Url} | {(e.Description.Length > 70 ? e.Description[..70] : e.Description)}");
    }
    // how much do they add on top of the default five?
    var baseIds = Sources.All.Where(s => s.DefaultOn).Select(s => (s.Id, s.Name)).ToList();
    var baseScan = (await Scanner.RunAsync(baseIds, new Progress<ScanProgress>(), CancellationToken.None)).Catalog.Entries;
    foreach (var id in new[] { "apisguru", "github-discovery" })
    {
        var plus = (await Scanner.RunAsync([.. baseIds, (id, id)], new Progress<ScanProgress>(), CancellationToken.None)).Catalog.Entries;
        var extra = plus.Where(e => e.Sources.All(s => s.StartsWith("GitHub:") || s == "APIs.guru")).Select(e => new ApiRow(e)).ToList();
        Console.WriteLine($"\n===== default {baseScan.Count:N0} -> with {id} {plus.Count:N0}  (+{plus.Count - baseScan.Count:N0} unique)");
        Console.WriteLine("   access of the extras: " + string.Join(", ", extra.GroupBy(r => r.AccessLabel).Select(g => $"{g.Key} {g.Count()}")));
        Console.WriteLine("   auth of the extras: " + string.Join(", ", extra.GroupBy(r => r.AuthLabel).OrderByDescending(g => g.Count()).Take(5).Select(g => $"{g.Key} {g.Count()}")));
    }
    return 0;
}
int pass = 0, fail = 0;
void Check(string name, bool ok, string detail = "")
{
    if (ok) pass++; else fail++;
    Console.ForegroundColor = ok ? ConsoleColor.Green : ConsoleColor.Red;
    Console.Write(ok ? "  PASS  " : "  FAIL  ");
    Console.ResetColor();
    Console.WriteLine(name + (detail.Length > 0 ? $"  [{detail}]" : ""));
}

Console.WriteLine("== Markdown parser ==");
const string md = """
    # Public APIs
    ## Index
    * [Animals](#animals)
    ### Sponsors
    | [Bad](https://sponsor.example) | should be skipped | No | Yes | Yes |
    ### Animals
    API | Description | Auth | HTTPS | CORS
    |:---|:---|:---|:---|:---|
    | [Cat Facts](https://catfact.ninja/) | Random cat facts | No | Yes | Yes |
    | [Dogs](https://dog.ceo/dog-api/) | Dog pictures | `apiKey` | Yes | No |
    | [RescueGroups](https://rescuegroups.org/api) | Adoption | OAuth | No | Unknown |

    ### 💰 Cryptocurrency
    |                API                 | Description     |   Auth   | HTTPS |  CORS   |
    | :--------------------------------: | --------------- | :------: | :---: | :-----: |
    | [**CoinGecko**](https://www.coingecko.com/en/api) | Crypto prices | No | Yes | Yes |
    ### Advertising
    | API | Description | Open/Trial |
    | --- | ----------- | ---- |
    | [**Ad Thing**](https://ads.example/docs) | Manage ads | **N/A** |
    """;
var parsed = MarkdownListParser.Parse(md, "test");
Check("parses 5 rows, skips sponsors + index", parsed.Count == 5, $"{parsed.Count}");
Check("name + url", parsed[0] is { Name: "Cat Facts", Url: "https://catfact.ninja/" });
Check("auth None / ApiKey / OAuth", parsed[0].Auth == AuthKind.None && parsed[1].Auth == AuthKind.ApiKey && parsed[2].Auth == AuthKind.OAuth);
Check("https + cors flags", parsed[2].Https == false && parsed[2].Cors == "Unknown" && parsed[0].Cors == "Yes");
Check("bold link + emoji heading cleaned", parsed[3].Name == "CoinGecko" && parsed[3].RawCategory == "Cryptocurrency", parsed[3].RawCategory);
Check("table without auth column -> Unknown", parsed[4].Auth == AuthKind.Unknown && parsed[4].Description == "Manage ads");
var bullets = MarkdownListParser.Parse("## Weather\n- [Open-Meteo](https://open-meteo.com/) - Free weather API\n", "t", bullets: true);
Check("bullet lists", bullets.Count == 1 && bullets[0].Description == "Free weather API");

Console.WriteLine("== Categoriser ==");
string Cat(string raw, string name = "x", string desc = "") { var e = new ApiEntry { RawCategory = raw, Name = name, Description = desc }; Categoriser.Apply(e); return e.Category; }
Check("Cryptocurrency", Cat("Cryptocurrency") == "Blockchain & Crypto", Cat("Cryptocurrency"));
Check("Finance", Cat("Finance") == "Currency & Finance", Cat("Finance"));
Check("financial (apis.guru)", Cat("financial") == "Currency & Finance");
Check("Sports & Fitness", Cat("Sports & Fitness") == "Sports & Fitness", Cat("Sports & Fitness"));
Check("Social media", Cat("Social Media") == "Social", Cat("Social Media"));
Check("Business not Transport", Cat("Business") == "Business", Cat("Business"));
Check("Artificial Intelligence", Cat("Artificial Intelligence") == "Machine Learning & AI", Cat("Artificial Intelligence"));
Check("Art & Design", Cat("Art & Design") == "Art & Design");
Check("Geocoding", Cat("Geocoding") == "Geocoding & Maps");
Check("keywords: weather", Cat("", "Open-Meteo", "Global weather forecast API") == "Weather");
Check("keywords: jokes", Cat("", "Dad Jokes", "Random dad jokes") == "Quotes, Jokes & Fun");
Check("nothing matches -> Other", Cat("", "Zzz", "qqq") == "Other");

Console.WriteLine("== Merge / keys ==");
Check("key ignores scheme, www, slash", Scanner.MakeKey("http://www.Example.com/api/", "a") == Scanner.MakeKey("https://example.com/api", "b"));
Check("fragment kept apart", Scanner.MakeKey("https://x.com/docs#one", "a") != Scanner.MakeKey("https://x.com/docs#two", "a"));
var merged = Scanner.Merge(
[
    new ApiEntry { Name = "NASA", Url = "https://api.nasa.gov", Auth = AuthKind.Unknown, Sources = ["a"] },
    new ApiEntry { Name = "NASA", Url = "https://api.nasa.gov/", Auth = AuthKind.ApiKey, AuthRaw = "apiKey", RawCategory = "Science & Math", Sources = ["b"] },
]);
Check("duplicates merged, auth + category filled", merged.Count == 1 && merged[0].Auth == AuthKind.ApiKey && merged[0].Sources.Count == 2 && merged[0].Category == "Science, Math & Education", merged[0].Category);

Console.WriteLine("== Key knowledge ==");
var nasa = new ApiRow(merged[0]);
Check("NASA demo key", nasa.DemoKey == "DEMO_KEY" && nasa.KeyBadge == "Demo key" && nasa.Example!.Contains("DEMO_KEY"));
var sub = new ApiRow(new ApiEntry { Name = "SportsDB", Url = "https://www.thesportsdb.com/api.php", Auth = AuthKind.ApiKey });
Check("subdomain host match", sub.DemoKey == "123");
Check("NASA key note is for api.nasa.gov only, not every nasa.gov site", new ApiRow(new ApiEntry { Name = "JPL", Url = "https://ssd-api.jpl.nasa.gov/doc/cad.html" }).DemoKey is null);
var plain = new ApiRow(new ApiEntry { Name = "Foo", Url = "https://foo.example/docs", Auth = AuthKind.ApiKey, AuthRaw = "apiKey" });
Check("generic how-to for unknown provider", plain.DemoKey is null && plain.HowTo.Contains("Sign up") && plain.KeyBadge == "Key needed");
Check("no hint host collisions", KeyKnowledge.Hints.SelectMany(h => h.Hosts).GroupBy(h => h).All(g => g.Count() == 1));
Check("all sign-up links are https", KeyKnowledge.Hints.All(h => h.SignupUrl is null || h.SignupUrl.StartsWith("https://")));

Console.WriteLine("== Docs scanner ==");
var scan = new DocsScanResult();
DocsScanner.ReadPage("""
    <html><head><script>var key = 'apikey=SHOULD_NOT_SEE';</script></head><body>
    <a href="/signup">Get your free API key</a> <a href="https://other.example/pricing">Pricing</a> <a href="/about">About</a>
    <p>Try it: https://api.foo.example/v1/data?api_key=DEMO_KEY&q=1 or with ?apikey=YOUR_API_KEY</p>
    <p>The free plan allows 1,000 requests per day. Nothing else matters.</p>
    </body></html>
    """, "https://foo.example/docs/", scan);
Check("finds sign-up links, resolves relative", scan.Items.Any(i => i.Kind == "Sign-up link" && i.Value == "https://foo.example/signup"));
Check("ignores unrelated links", !scan.Items.Any(i => i.Value.Contains("/about")));
Check("sample key found", scan.Items.Any(i => i is { Kind: "Sample key", Value: "DEMO_KEY" }));
Check("placeholder recognised", scan.Items.Any(i => i is { Kind: "Placeholder", Value: "YOUR_API_KEY" }));
Check("script content ignored", !scan.Items.Any(i => i.Value.Contains("SHOULD_NOT")));
Check("free tier sentence", scan.Items.Any(i => i.Kind == "Free tier / limits" && i.Value.Contains("1,000 requests per day")));
var spec = new DocsScanResult();
DocsScanner.ReadSpec("""{"securityDefinitions":{"k":{"type":"apiKey","in":"header","name":"X-Api-Key"}}}""", spec);
Check("OpenAPI security scheme", spec.Items.Count == 1 && spec.Items[0].Value == "API key in header: X-Api-Key", spec.Items.FirstOrDefault()?.Value ?? "");

Console.WriteLine("== Export ==");
Check("csv quoting", Exporter.Csv([new ApiRow(new ApiEntry { Name = "A \"q\", b", Url = "https://a.example" })]).Contains("\"A \"\"q\"\", b\""));
Check("markdown table escapes pipes", Exporter.MarkdownTable([new ApiRow(new ApiEntry { Name = "A|B", Url = "https://a.example" })]).Contains("A\\|B"));
Check("curl uses example + header", Exporter.Curl(new ApiRow(new ApiEntry { Name = "r", Url = "https://reqres.in/" })) == "curl \"https://reqres.in/api/users/2\" -H \"x-api-key: reqres-free-v1\"");
Check("json has demo key", Exporter.JsonOne(nasa).Contains("\"demoKey\": \"DEMO_KEY\""));
Check("github url -> raw readme", Sources.ToRaw("https://github.com/a/b") == "https://raw.githubusercontent.com/a/b/HEAD/README.md");

Console.WriteLine("== API tester ==");
var fmt = ApiTester.Format("{\"a\":1,\"b\":[true,\"é\"]}");
Check("pretty-prints JSON", fmt.IsJson && fmt.Body.Contains("\n  \"a\": 1") && fmt.Body.Contains("é"), fmt.Body.Replace("\n", "|"));
Check("non-JSON passes through", ApiTester.Format("<html>hi</html>") is { IsJson: false, Body: "<html>hi</html>" });
Check("broken JSON shown as received", ApiTester.Format("{\"a\":") is { IsJson: false, Body: "{\"a\":" });
Check("long bodies are cut", ApiTester.Format(new string('x', 300_000)).Body.Length < 201_000);
Check("header split", ApiTester.SplitHeader("x-api-key: abc:def") is { Name: "x-api-key", Value: "abc:def" } && ApiTester.SplitHeader("nonsense") is null);
Check("bad url rejected", (await ApiTester.SendAsync("ftp://x", null, CancellationToken.None)) is { Ok: false, Body: "" });
var reqres = new ApiRow(new ApiEntry { Name = "r", Url = "https://reqres.in/" });
Check("default test request from key knowledge", reqres.DefaultTestUrl == "https://reqres.in/api/users/2" && reqres.DefaultTestHeader == "x-api-key: reqres-free-v1");

Check("content type guessed", ApiTester.GuessContentType(" {\"a\":1}") == "application/json" && ApiTester.GuessContentType("a=1&b=two") == "application/x-www-form-urlencoded"
    && ApiTester.GuessContentType("<x/>") == "application/xml" && ApiTester.GuessContentType("hello there") == "text/plain");
Check("several headers parse; bad line reported", ApiTester.ParseHeaders("A: 1\r\n\r\nB: two:2\n", out var hs) is null && hs.Count == 2 && hs[1].Value == "two:2" && ApiTester.ParseHeaders("A: 1\noops", out _) == "oops");
Check("only POST/PUT/PATCH carry a body", ApiTester.HasBody("POST") && ApiTester.HasBody("PATCH") && !ApiTester.HasBody("GET") && !ApiTester.HasBody("DELETE"));
var curl = ApiTester.ToCurl(new ApiTestRequest("POST", "https://x.example/a?k={key}", "X-Api-Key: {key}", "{\"it's\":1}"));
Check("curl: method, headers, body, quoting, {key} kept", curl.StartsWith("curl -X POST 'https://x.example/a?k={key}'") && curl.Contains("-H 'X-Api-Key: {key}'") && curl.Contains("-H 'Content-Type: application/json'") && curl.Contains("--data-raw '{\"it'\\''s\":1}'"), curl.Replace("\n", " "));
Check("curl: plain GET stays simple", ApiTester.ToCurl(new ApiTestRequest("GET", "https://x.example/", "", "ignored")) == "curl 'https://x.example/'");
Check("tidy JSON", ApiTester.TidyJson("{\"a\":[1,2]}")!.Contains("\n  \"a\": [") && ApiTester.TidyJson("{nope") is null);
Check("unsupported method rejected", (await ApiTester.SendAsync(new ApiTestRequest("TRACE", "https://x.example/", "", ""), CancellationToken.None)).Summary.Contains("not a supported"));

Console.WriteLine("== Free access level ==");
ApiRow Row(AuthKind auth, string url = "https://foo.example/docs", string desc = "", string pricing = "") => new(new ApiEntry { Name = "x", Url = url, Auth = auth, Description = desc, Pricing = pricing });
Check("no key -> full free access", Row(AuthKind.None).Access == AccessLevel.FullFree);
Check("curated: NASA full free, Alpha Vantage free tier, HIBP trial only", nasa.Access == AccessLevel.FullFree
    && Row(AuthKind.ApiKey, "https://www.alphavantage.co/").Access == AccessLevel.FreeTier && Row(AuthKind.ApiKey, "https://haveibeenpwned.com/API/v3").Access == AccessLevel.TrialOnly);
Check("directory paid flag beats 'no key'", Row(AuthKind.None, pricing: "paid").Access == AccessLevel.TrialOnly && Row(AuthKind.Unknown, pricing: "open").Access == AccessLevel.FullFree);
Check("wording: free plan / trial", Row(AuthKind.ApiKey, desc: "Weather data with a generous free tier").Access == AccessLevel.FreeTier && Row(AuthKind.ApiKey, desc: "SMS gateway, 14-day trial").Access == AccessLevel.TrialOnly);
Check("'clinical trials' is not a trial", Row(AuthKind.ApiKey, desc: "Search clinical trials data").Access == AccessLevel.Unknown);
var scanned = Row(AuthKind.ApiKey);
Check("key needed, nothing known -> not stated", scanned.AccessLabel == "Not stated");
scanned.DocsScan = new DocsScanResult { Items = [new FoundItem { Kind = "Free tier / limits", Value = "The free plan allows 500 requests per day." }] };
Check("a docs scan upgrades 'not stated'", scanned.Access == AccessLevel.FreeTier);
var flags = MarkdownListParser.Parse("### Ads\n| API | Description | Open/Trial |\n| --- | --- | --- |\n| [A](https://a.example) | x | 💸 |\n| [B](https://b.example) | y | ![Open Source](https://img/o.png \"Open Source\") |\n| [C](https://c.example) | z | **N/A** |\n", "t");
Check("n0shake paid / open flags parsed", flags.Count == 3 && flags[0].Pricing == "paid" && flags[1].Pricing == "open" && flags[2].Pricing == "");

Console.WriteLine("== JSON -> C# classes ==");
var cs = JsonToCSharp.Generate("""
    {"id": 1, "big": 9999999999, "price": 1.5, "mixed": [1, 2.5], "name": "x", "when": "2026-09-21T17:22:01.142Z", "ok": true, "nothing": null,
     "owner": {"login": "me", "site-url": null},
     "tags": ["a", "b"],
     "items": [{"sku": "a", "qty": 1}, {"sku": "b", "qty": 2, "note": "fragile"}],
     "class": "reserved", "2fa": false}
    """, "OrderResponse")!;
Check("root class first + usage hint", cs.Contains("Deserialize<OrderResponse>(json)") && cs.IndexOf("class OrderResponse") < cs.IndexOf("class Owner"));
Check("number types: int / long / double", cs.Contains("public int Id ") && cs.Contains("public long Big ") && cs.Contains("public double Price ") && cs.Contains("public List<double> Mixed "));
Check("string, date, bool, null", cs.Contains("public string Name { get; set; } = \"\";") && cs.Contains("public DateTimeOffset When ") && cs.Contains("public bool Ok ") && cs.Contains("public object? Nothing "));
Check("nested object + odd property names", cs.Contains("public Owner Owner2 ") || cs.Contains("public Owner Owner "), "owner");
Check("json names kept via attribute", cs.Contains("[JsonPropertyName(\"site-url\")]") && cs.Contains("public object? SiteUrl ") && cs.Contains("[JsonPropertyName(\"2fa\")]") && cs.Contains(" _2fa "));
Check("array of objects -> singular class, optional props nullable", cs.Contains("public List<Item> Items { get; set; } = [];") && cs.Contains("class Item") && cs.Contains("public string? Note ") && cs.Contains("public int Qty "));
Check("nested class properties are initialised", cs.Contains("public Owner Owner { get; set; } = new();"));
Check("root array", JsonToCSharp.Generate("[{\"a\":1}]", "Thing")!.Contains("Deserialize<List<ThingItem>>(json)"));
Check("not JSON / no object -> null", JsonToCSharp.Generate("<html>") is null && JsonToCSharp.Generate("[1,2,3]") is null);

Console.WriteLine("== Line diff ==");
var dd = LineDiff.Compare("{\n  \"a\": 1,\n  \"b\": 2\n}", "{\n  \"a\": 1,\n  \"b\": 3,\n  \"c\": 4\n}");
Check("diff marks changed lines only", dd is { Added: 2, Removed: 1 } && dd.Text.Contains("-   \"b\": 2") && dd.Text.Contains("+   \"c\": 4") && dd.Text.Contains("    \"a\": 1,"), dd.Text.Replace("\n", "|"));
Check("identical -> no changes", LineDiff.Compare("a\nb", "a\nb") is { Added: 0, Removed: 0 });
Check("huge inputs refuse politely", LineDiff.Compare(string.Join('\n', Enumerable.Range(0, 3000)), "x").Text.Contains("too long"));

Console.WriteLine("== Example endpoints ==");
var ep = new DocsScanResult();
DocsScanner.ReadPage("""
    <html><body><link href="https://cdn.foo.example/site.css"><a href="https://github.com/foo/sdk">SDK</a> <a href="https://foo.example/docs/getting-started">Guide</a>
    <pre>curl "https://api.foo.example/v1/forecast?city=London&amp;apikey=YOUR_API_KEY"</pre>
    <p>Or try <a href="https://foo.example/api/random.json">a live example</a>. To add one: POST https://api.foo.example/v1/items</p>
    <code>GET /v1/cities/{id}</code> <code>DELETE /v1/items/42</code> <img src="https://foo.example/logo.png">
    </body></html>
    """, "https://foo.example/docs", ep);
var eps = ep.Items.Where(i => i.IsEndpoint).ToList();
Check("finds the curl example, key placeholder -> {key}", eps.Any(i => i is { Value: "https://api.foo.example/v1/forecast?city=London&apikey={key}", Method: "GET" }), string.Join(" | ", eps.Select(i => i.Method + " " + i.Value)));
Check("finds a linked .json example", eps.Any(i => i.Value == "https://foo.example/api/random.json"));
Check("method read from 'POST https://…'", eps.Any(i => i is { Value: "https://api.foo.example/v1/items", Method: "POST" }));
Check("'GET /path' joined to the example's origin", eps.Any(i => i is { Value: "https://api.foo.example/v1/cities/{id}", Method: "GET" } && i.Note.Contains("fill in")) && eps.Any(i => i is { Method: "DELETE", Value: "https://api.foo.example/v1/items/42" }));
Check("ignores assets, code hosts and guide pages", !eps.Any(i => i.Value.Contains("cdn.") || i.Value.Contains("github.com") || i.Value.Contains("logo.png") || i.Value.Contains("getting-started")));
var bareEp = new DocsScanResult();
DocsScanner.ReadPage("<font>www.foo.example/api/json/v1/1/search.php?s=margarita</font><br><font>other.example/api/x.php?a=1</font>", "https://www.foo.example/api.php", bareEp);
Check("scheme-less URLs accepted on the docs' own site only", bareEp.Items.Count(i => i.IsEndpoint) == 1 && bareEp.Items.Any(i => i.Value == "https://www.foo.example/api/json/v1/1/search.php?s=margarita"), string.Join(" | ", bareEp.Items.Where(i => i.IsEndpoint).Select(i => i.Value)));
Check("real key values are left alone", DocsScanner.KeyPlaceholders(new Uri("https://a.example/x?api_key=DEMO_KEY&q=1")) == "https://a.example/x?api_key=DEMO_KEY&q=1" && DocsScanner.KeyPlaceholders(new Uri("https://a.example/x?token=&q=1")) == "https://a.example/x?token={key}&q=1");
var specEp = new DocsScanResult();
DocsScanner.ReadSpec("""{"swagger":"2.0","host":"api.bar.example","basePath":"/v2","schemes":["https"],"paths":{"/pets":{"get":{"summary":"List pets"}},"/pets/{id}":{"get":{}},"/orders":{"post":{}}}}""", specEp);
Check("OpenAPI spec -> base url + GET paths without parameters", specEp.Items.Count(i => i.IsEndpoint) == 1 && specEp.Items.Any(i => i is { Value: "https://api.bar.example/v2/pets", Method: "GET" } && i.Note.Contains("List pets")));

Console.WriteLine("== New since last scan ==");
var t0 = new DateTime(2026, 9, 1); var t1d = new DateTime(2026, 9, 10);
var firstCat = new Catalog { ScannedAt = t0, Entries = [new ApiEntry { Key = "a", Name = "A" }, new ApiEntry { Key = "b", Name = "B" }] };
Check("first scan is the baseline - nothing is new", Scanner.StampFirstSeen(firstCat, null) == (0, 0) && firstCat.BaselineAt == t0 && !Scanner.IsNew(firstCat.Entries[0], firstCat.BaselineAt, t0.AddDays(1)));
var secondCat = new Catalog { ScannedAt = t1d, Entries = [new ApiEntry { Key = "a", Name = "A" }, new ApiEntry { Key = "c", Name = "C" }] };
Check("second scan: 1 new, 1 gone, first-seen carried over", Scanner.StampFirstSeen(secondCat, firstCat) == (1, 1) && secondCat.BaselineAt == t0 && secondCat.Entries[0].FirstSeen == t0 && secondCat.Entries[1].FirstSeen == t1d);
Check("new for 14 days, then not", Scanner.IsNew(secondCat.Entries[1], secondCat.BaselineAt, t1d.AddDays(13)) && !Scanner.IsNew(secondCat.Entries[1], secondCat.BaselineAt, t1d.AddDays(15)) && !Scanner.IsNew(secondCat.Entries[0], secondCat.BaselineAt, t1d));
var legacy = new Catalog { ScannedAt = t0, Entries = [new ApiEntry { Key = "a", Name = "A" }] }; // saved before FirstSeen existed
var afterLegacy = new Catalog { ScannedAt = t1d, Entries = [new ApiEntry { Key = "a", Name = "A" }, new ApiEntry { Key = "z", Name = "Z" }] };
Check("catalog saved by an older version upgrades cleanly", Scanner.StampFirstSeen(afterLegacy, legacy) == (1, 0) && afterLegacy.Entries[0].FirstSeen == t0 && new ApiRow(afterLegacy.Entries[1], afterLegacy.BaselineAt).FirstSeenLabel.Contains("2026"));
Check("re-scan due: daily / weekly / never", MainViewModel.RescanDue("Daily", t0, t0.AddHours(25)) && !MainViewModel.RescanDue("Daily", t0, t0.AddHours(23)) && MainViewModel.RescanDue("Weekly", t0, t0.AddDays(8))
    && !MainViewModel.RescanDue("Weekly", t0, t0.AddDays(6)) && !MainViewModel.RescanDue("Never", t0, t0.AddDays(99)) && !MainViewModel.RescanDue("Daily", null, t0));

Console.WriteLine("== Discovery quality gates ==");
List<ApiEntry> Fake(int n, Func<int, string> url, string desc = "An API") => [.. Enumerable.Range(0, n).Select(i => new ApiEntry { Name = "N" + i, Url = url(i), Description = desc })];
Check("affiliate links are dropped", Sources.Clean(Fake(10, i => i < 4 ? $"https://apify.com/x{i}?fpr=abc" : $"https://h{i}.example/")).Count == 6);
Check("a real list passes", Sources.IsRealApiList(Fake(60, i => $"https://host{i}.example/api")));
Check("too small / one marketplace / translated list fail", !Sources.IsRealApiList(Fake(10, i => $"https://h{i}.example/")) && !Sources.IsRealApiList(Fake(60, i => i < 30 ? $"https://market.example/{i}" : $"https://h{i}.example/"))
    && !Sources.IsRealApiList(Fake(60, i => $"https://h{i}.example/", "你想要的所有数据")));
var strict = MarkdownListParser.Parse("### Tools\n| Name | Description |\n|---|---|\n| [Editor](https://e.example) | not an API |\n### APIs\n| API | Description | Auth |\n|---|---|---|\n| [Real](https://r.example) | yes | No |\n### Unverified & Unreachable (12)\n| API | Description | Auth |\n|---|---|---|\n| [Dead](https://d.example) | gone | No |\n", "t", strictTables: true);
Check("strict tables: needs an Auth/HTTPS/CORS column; dead-link sections skipped", strict.Count == 1 && strict[0].Name == "Real", string.Join(",", strict.Select(e => e.Name)));
Check("cloud giants have honest guidance", new ApiRow(new ApiEntry { Name = "x", Url = "https://docs.aws.amazon.com/appsync/" }) is { AccessLabel: "Free tier (limited)", HasSignupUrl: true });

Console.WriteLine("== Brands ==");
Check("brand domain drops api./docs./www. but keeps real hosts", LogoService.BrandDomain("https://api.nasa.gov/x") == "nasa.gov" && LogoService.BrandDomain("https://docs.api.foo.co.uk/") == "foo.co.uk"
    && LogoService.BrandDomain("https://dev.to/api") == "dev.to" && LogoService.BrandDomain("https://www.thecocktaildb.com/api.php") == "thecocktaildb.com" && LogoService.BrandDomain("nonsense") == "");
var brandRow = new ApiRow(new ApiEntry { Name = "  7Timer!", Url = "https://www.7timer.info/doc.php" });
Check("initial + stable avatar colour", brandRow.Initial == "7" && brandRow.AvatarBrush == new ApiRow(new ApiEntry { Name = "Other", Url = "https://7timer.info/" }).AvatarBrush && new ApiRow(new ApiEntry { Name = "!!", Url = "x" }).Initial == "?");
Check("bad image bytes -> no logo, no crash", LogoService.Decode([1, 2, 3, 4]) is null);
var gh = LogoService.Brand("https://github.com/r-spacex/SpaceX-API");
Check("GitHub repo -> its owner's avatar", gh is { Key: "github.com_r-spacex", Label: "github.com/r-spacex" } && gh.IconUrls[0] == "https://github.com/r-spacex.png?size=64");
Check("GitHub Pages -> owner too; GitHub's own pages are not owners", LogoService.Brand("https://alexwohlbruck.github.io/cat-facts/").Label == "github.com/alexwohlbruck" && LogoService.Brand("https://github.com/features/actions").Label == "github.com");
Check("RapidAPI names the provider", LogoService.Brand("https://rapidapi.com/api-sports/api/api-football").Label == "RapidAPI · by api-sports" && LogoService.Brand("https://rapidapi.com/api-sports/api/api-football").Key == "rapidapi.com");
Check("ordinary site -> its domain + both icon services", LogoService.Brand("https://api.nasa.gov/") is { Key: "nasa.gov", Label: "nasa.gov", IconUrls.Length: 2 });
var pageIcon = DocsScanner.FindPageIcon("<link rel=\"icon\" href=\"/icon.svg\"><link rel='shortcut icon' href='//cdn.foo.example/favicon.ico'><link rel=\"mask-icon\" href=\"/m.png\"><link rel=\"apple-touch-icon\" sizes=\"180x180\" href=\"/touch.png\">", new Uri("https://foo.example/docs"));
Check("page icon: apple-touch-icon preferred, svg + mask skipped", pageIcon == "https://foo.example/touch.png" && DocsScanner.FindPageIcon("<link rel='icon' href='//cdn.foo.example/f.ico'>", new Uri("https://foo.example/")) == "https://cdn.foo.example/f.ico"
    && DocsScanner.FindPageIcon("<link rel=\"icon\" href=\"/icon.svg\">", new Uri("https://foo.example/")) is null, pageIcon ?? "null");
ApiRow.ShowLogos = false;
Check("logos off -> nothing is requested", brandRow.Logo is null);
ApiRow.ShowLogos = true;

Console.WriteLine("== Tags, compare, background scan ==");
Check("tags: trimmed, de-duplicated, capped", string.Join("|", ApiRow.ParseTags(" work ,maps; Work,, a-very-long-tag-name-that-goes-on-and-on ")) == "work|maps|a-very-long-tag-name-tha" && ApiRow.ParseTags("a,b,c,d,e,f,g,h,i,j").Count == 8);
var tagged = new ApiRow(new ApiEntry { Name = "T", Url = "https://t.example/" }) { TagsText = "work, maps" };
Check("tags show in row + exports", tagged.TagsLabel == "🏷 work, maps" && Exporter.Text(tagged).Contains("Tags: work, maps") && Exporter.Csv([tagged]).Contains("\"work, maps\"") && Exporter.JsonOne(tagged).Contains("\"maps\""));
var cmp = ApiScout.Views.CompareWindow.ToMarkdown([nasa, tagged], [("Free", a => a.AccessLabel), ("Multi|line", a => "x|y\nz")]);
Check("comparison as a Markdown table", cmp.StartsWith("| | [NASA](https://api.nasa.gov) | [T](https://t.example/) |\n|---|---|---|\n") && cmp.Contains("| **Free** | Full free access |") && cmp.Contains("x\\|y<br>z"), cmp.Replace("\n", "⏎"));
var weeklyXml = ScheduledScan.BuildXml(@"C:\Apps & Tools\ApiScout.exe", daily: false);
var parsedXml = System.Xml.Linq.XDocument.Parse(weeklyXml.Replace("encoding=\"UTF-16\"", "encoding=\"utf-8\""));
System.Xml.Linq.XNamespace tn = "http://schemas.microsoft.com/windows/2004/02/mit/task";
Check("task xml: valid, weekly Monday, runs --scan, catches up after a missed start", parsedXml.Descendants(tn + "Monday").Any() && parsedXml.Descendants(tn + "Command").Single().Value == @"C:\Apps & Tools\ApiScout.exe"
    && parsedXml.Descendants(tn + "Arguments").Single().Value == "--scan" && parsedXml.Descendants(tn + "StartWhenAvailable").Single().Value == "true" && parsedXml.Descendants(tn + "RunLevel").Single().Value == "LeastPrivilege");
Check("task xml: daily variant", ScheduledScan.BuildXml(@"C:\a.exe", daily: true).Contains("<DaysInterval>1</DaysInterval>") && !ScheduledScan.BuildXml(@"C:\a.exe", daily: true).Contains("Monday"));
Environment.SetEnvironmentVariable("APISCOUT_DATA", Path.GetTempPath());
Check("a test copy never registers a real task", ScheduledScan.Register(daily: false) is { Ok: false } refused && refused.Message.Contains("test data folder"));
Environment.SetEnvironmentVariable("APISCOUT_DATA", null);

Console.WriteLine("== Rate limits ==");
var nowR = new DateTime(2026, 9, 21, 20, 0, 0);
RateInfo? Rate(int status, params (string, string)[] h) => ApiTester.ReadRate(status, n => h.FirstOrDefault(x => x.Item1.Equals(n, StringComparison.OrdinalIgnoreCase)).Item2, nowR);
Check("429 + Retry-After seconds", Rate(429, ("Retry-After", "120")) is { Limited: true, Estimated: false } r1 && r1.ResetAt == nowR.AddSeconds(120));
Check("429 + X-RateLimit-Reset as epoch seconds", Rate(429, ("x-ratelimit-reset", new DateTimeOffset(nowR.AddMinutes(45)).ToUnixTimeSeconds().ToString())) is { Limited: true, Estimated: false } r2 && r2.ResetAt == nowR.AddMinutes(45));
Check("429 + reset as seconds from now", Rate(429, ("RateLimit-Reset", "30")) is { } r3 && r3.ResetAt == nowR.AddSeconds(30));
Check("429 with no headers -> an hour, marked as an estimate", Rate(429) is { Limited: true, Estimated: true } r4 && r4.ResetAt == nowR.AddHours(1));
Check("reset in the past is pushed just ahead; silly far future is capped", Rate(429, ("Retry-After", "0"))!.ResetAt == nowR.AddMinutes(1) && Rate(429, ("Retry-After", "99999999"))!.ResetAt == nowR.AddDays(31));
Check("200 with allowance headers", Rate(200, ("X-RateLimit-Remaining", "38"), ("X-RateLimit-Limit", "40")) is { Limited: false, Remaining: 38, Limit: 40 });
Check("403 with nothing left counts as limited; plain 200 says nothing", Rate(403, ("X-RateLimit-Remaining", "0"), ("X-RateLimit-Reset", "600")) is { Limited: true } && Rate(200) is null);
var limitedRow = new ApiRow(new ApiEntry { Name = "L", Url = "https://l.example/" }) { LimitEstimated = true, LimitedUntil = DateTime.Now.AddMinutes(30) };
Check("row shows the wait, and stops once it is over", limitedRow.IsLimited && limitedRow.LimitLabel.Contains("should work again around") && limitedRow.LimitLabel.Contains("estimate") && limitedRow.LimitShort.StartsWith("⏳ until ")
    && !new ApiRow(new ApiEntry { Name = "L", Url = "https://l.example/" }) { LimitedUntil = DateTime.Now.AddMinutes(-1) }.IsLimited);
Check("version is 1.6.1", ApiScout.Views.AboutWindow.VersionText == "1.6.1", ApiScout.Views.AboutWindow.VersionText);

Console.WriteLine("== Pricing page ==");
const string docsHtml = "<a href=\"https://twitter.com/foo/pricing\">Pricing</a> <a href=\"/docs\">Docs</a> <a href=\"https://www.foo.example/pricing?utm=1#top\">See our plans</a>";
Check("pricing link: same site only, fragment dropped", DocsScanner.FindPricingLink(docsHtml, "https://api.foo.example/docs") == "https://www.foo.example/pricing?utm=1", DocsScanner.FindPricingLink(docsHtml, "https://api.foo.example/docs") ?? "null");
Check("no pricing link -> null", DocsScanner.FindPricingLink("<a href=\"/docs\">Docs</a>", "https://foo.example/") is null);
var free = new DocsScanResult(); DocsScanner.ReadPricing("<div><h3>Hobby</h3><p>$0 / month</p><p>Free plan with 1,000 requests per day</p></div><div><h3>Pro</h3><p>$49</p></div>", "https://foo.example/pricing", free);
Check("free plan found", free.Items[0] is { Kind: "Pricing page" } && free.Items.Any(i => i.Kind == "Pricing" && i.Value.Contains("Free plan")) && ApiRow.AccessFromDocs(free) == AccessLevel.FreeTier);
var paid = new DocsScanResult(); DocsScanner.ReadPricing("<h3>Starter</h3><p>$29 per month</p><h3>Pro</h3><p>$99 per month</p>", "https://foo.example/pricing", paid);
Check("only paid plans -> demo / trial only", paid.Items.Any(i => i.Value.StartsWith("No free plan")) && ApiRow.AccessFromDocs(paid) == AccessLevel.TrialOnly);
var trial = new DocsScanResult(); DocsScanner.ReadPricing("<p>Start your 14-day trial today.</p>", "https://foo.example/pricing", trial);
Check("only a trial -> demo / trial only", ApiRow.AccessFromDocs(trial) == AccessLevel.TrialOnly && ApiRow.AccessFromDocs(null) == AccessLevel.Unknown);

Console.WriteLine("== Store ==");
var dir = Path.Combine(Path.GetTempPath(), "apiscout-test-" + Guid.NewGuid().ToString("N"));
Environment.SetEnvironmentVariable("APISCOUT_DATA", dir);
var store = new Store();
store.SetMyKey("k1", "  secret-123 ");
Check("my key round-trips, trimmed", new Store().GetMyKey("k1") == "secret-123");
Check("my key is not stored in clear text", !File.ReadAllText(Path.Combine(dir, "userdata.json")).Contains("secret-123"));
store.SetTestRequest("k1", new ApiTestRequest("POST", "https://a.example/?key=secret-999", "A: b\nC: d", "{\"x\":1}"));
Check("test request round-trips, encrypted", new Store().GetTestRequest("k1") is { Method: "POST", Url: "https://a.example/?key=secret-999", Headers: "A: b\nC: d", Body: "{\"x\":1}" } && !File.ReadAllText(Path.Combine(dir, "userdata.json")).Contains("secret-999"));
for (int i = 0; i < 10; i++) store.AddHistory("k1", new TestHistoryEntry { At = DateTime.Now, Url = "https://a.example/?key=secret-777", Summary = $"200 OK #{i}", Response = new string('x', i == 9 ? 70_000 : 10) });
var again = new Store();
Check("history: newest first, capped at 8, long bodies cut", again.TestHistory["k1"] is { Count: 8 } hist && hist[0].Summary == "200 OK #9" && hist[0].Response.Length < 61_000 && hist[0].Label.Contains("200 OK #9"));
Check("history file is encrypted", !File.ReadAllText(Path.Combine(dir, "test-history.dat")).Contains("secret-777"));
store.ClearHistory("k1");
Check("history cleared", !new Store().TestHistory.ContainsKey("k1"));
store.SetMyKey("k1", "");
Check("blank removes the key", new Store().GetMyKey("k1") is null);
// ---- export / import between two "PCs" (two data folders)
store.User.Favourites.Add("fav-1"); store.User.Tags["t-1"] = ["work", "maps"]; store.User.Notes["n-1"] = "note from PC one"; store.SaveUser();
store.SetMyKey("key-1", "super-secret-key-111");
store.SetTestRequest("req-1", new ApiTestRequest("POST", "https://a.example/?token=tok-222", "", "{}"));
var withKeys = Backup.Export(store, "correct horse");
var withoutKeys = Backup.Export(store, null);
Check("export: keys never in clear text; none at all without a passphrase", !withKeys.Contains("super-secret-key-111") && !withKeys.Contains("tok-222") && withKeys.Contains("\"Secrets\": \"") && withoutKeys.Contains("\"Secrets\": null") && withoutKeys.Contains("note from PC one"));
var dir2 = Path.Combine(Path.GetTempPath(), "apiscout-test2-" + Guid.NewGuid().ToString("N"));
Environment.SetEnvironmentVariable("APISCOUT_DATA", dir2);
var pc2 = new Store();
pc2.User.Notes["n-1"] = "already here"; pc2.SaveUser();
bool wrongRefused = false;
try { Backup.Import(pc2, Backup.Read(withKeys), "wrong passphrase"); } catch (System.Security.Cryptography.CryptographicException) { wrongRefused = true; }
Check("import: wrong passphrase is refused and changes nothing", wrongRefused && pc2.User.Favourites.Count == 0 && pc2.GetMyKey("key-1") is null);
var skippedImport = Backup.Import(pc2, Backup.Read(withKeys), "");
Check("import without the passphrase: everything but the keys", skippedImport is { Favourites: 1, Tagged: 1, Notes: 0, Keys: 0, KeptLocal: 1, SecretsSkipped: true } && pc2.User.Notes["n-1"] == "already here", skippedImport.ToString());
var fullImport = Backup.Import(pc2, Backup.Read(withKeys), "correct horse");
var pc2Again = new Store();
Check("import with the passphrase: keys usable on the new PC", fullImport is { Keys: 1, TestRequests: >= 1, Favourites: 0 } && pc2Again.GetMyKey("key-1") == "super-secret-key-111" && pc2Again.GetTestRequest("req-1")?.Url == "https://a.example/?token=tok-222"
    && pc2Again.User.Tags["t-1"].SequenceEqual(["work", "maps"]), fullImport.ToString());
Check("importing twice adds nothing", Backup.Import(pc2, Backup.Read(withKeys), "correct horse") is { Favourites: 0, Tagged: 0, Notes: 0, Keys: 0, TestRequests: 0 });
bool notOurs = false; try { Backup.Read("{\"hello\":1}"); } catch (InvalidDataException) { notOurs = true; }
Check("some other json file is rejected", notOurs);
Directory.Delete(dir2, true);
Environment.SetEnvironmentVariable("APISCOUT_DATA", dir);

// ---- review fixes
var lastScan = new Catalog { ScannedAt = t0, BaselineAt = t0.AddDays(-30), Entries = [
    new ApiEntry { Key = "a", Name = "A", Url = "https://a.example", Sources = ["one"], FirstSeen = t0.AddDays(-30) },
    new ApiEntry { Key = "b", Name = "B", Url = "https://b.example", Sources = ["two"], FirstSeen = t0.AddDays(-30) },
    new ApiEntry { Key = "c", Name = "C", Url = "https://c.example", Sources = ["one", "two"], FirstSeen = t0.AddDays(-30) },
    new ApiEntry { Key = "d", Name = "D", Url = "https://d.example", Sources = ["one"], FirstSeen = t0.AddDays(-30) } ] };
// source "two" failed this time, and "one" really dropped D
var partial = new Catalog { ScannedAt = t0.AddDays(7), Entries = [
    new ApiEntry { Key = "a", Name = "A", Url = "https://a.example", Sources = ["one"] }, new ApiEntry { Key = "c", Name = "C", Url = "https://c.example", Sources = ["one"] } ] };
int keptBack = Scanner.KeepUnreadable(partial, lastScan);
var partialStamp = Scanner.StampFirstSeen(partial, lastScan);
Check("failed source: its APIs are kept, a real removal still counts", keptBack == 1 && partial.Entries.Select(e => e.Key).SequenceEqual(["a", "b", "c"]) && partialStamp == (0, 1), $"kept {keptBack}, {partialStamp}");
var recovered = new Catalog { ScannedAt = t0.AddDays(14), Entries = [
    new ApiEntry { Key = "a", Name = "A", Url = "https://a.example", Sources = ["one"] }, new ApiEntry { Key = "b", Name = "B", Url = "https://b.example", Sources = ["two"] },
    new ApiEntry { Key = "c", Name = "C", Url = "https://c.example", Sources = ["one", "two"] } ] };
Check("failed source: nothing is 'new' when it comes back", Scanner.StampFirstSeen(recovered, partial) == (0, 0) && recovered.Entries.All(e => e.FirstSeen == t0.AddDays(-30)));
Check("merge drops links that are not web pages", Scanner.Merge([new ApiEntry { Name = "F", Url = "ftp://files.example/api" }, new ApiEntry { Name = "M", Url = "mailto:a@b.example" }, new ApiEntry { Name = "R", Url = "/docs" }, new ApiEntry { Name = "Ok", Url = "https://ok.example" }]).Select(e => e.Name).SequenceEqual(["Ok"]));
Check("scanners stay off the local network", !Http.IsPublicWebUrl("http://192.168.1.1/reboot") && !Http.IsPublicWebUrl("http://localhost:8080/") && !Http.IsPublicWebUrl("http://10.0.0.5/") && !Http.IsPublicWebUrl("http://172.20.1.1/")
    && !Http.IsPublicWebUrl("http://169.254.169.254/latest") && !Http.IsPublicWebUrl("http://[::1]/") && !Http.IsPublicWebUrl("http://[fd00::1]/") && !Http.IsPublicWebUrl("ftp://a.example")
    && Http.IsPublicWebUrl("https://api.nasa.gov/") && Http.IsPublicWebUrl("http://8.8.8.8/") && Http.IsPublicWebUrl("http://172.32.0.1/"));
Check("link check refuses a private address without a request", (await LinkChecker.CheckAsync("http://192.168.1.1/", CancellationToken.None)) is { Label: "Down", LatencyMs: 0 }
    && (await LinkChecker.CheckAsync("mailto:x@y.example", CancellationToken.None)).Label == "Down");
Check("csv: formulas are defused", Exporter.Quote("=HYPERLINK(\"http://x\")") == "\"'=HYPERLINK(\"\"http://x\"\")\"" && Exporter.Quote("@SUM(A1)").StartsWith("\"'@") && Exporter.Quote("-1").StartsWith("\"'-") && Exporter.Quote("plain") == "\"plain\"");
Check("tsv cell: no tabs, line breaks or formulas", Exporter.Cell("a\tb\r\nc") == "a b c" && Exporter.Cell("+1") == "'+1" && Exporter.Cell(null) == "");
var oddUrl = Exporter.Curl(new ApiRow(new ApiEntry { Name = "Odd", Url = "https://odd.example/a\"b\\c" }));
Check("curl / C# snippets escape quotes in the URL", oddUrl == "curl -i \"https://odd.example/a\\\"b\\\\c\"" && Exporter.CSharp(new ApiRow(new ApiEntry { Name = "Odd", Url = "https://odd.example/a\"b" })).Contains("GetStringAsync(\"https://odd.example/a\\\"b\")"), oddUrl);
var micro = ApiTester.ReadRate(429, n => n == "X-RateLimit-Reset" ? "1790000000000000" : null, new DateTime(2026, 9, 21, 12, 0, 0));
Check("rate limit reset given in microseconds", micro is { Limited: true, Estimated: false, ResetAt: { Year: 2026 } }, micro?.ResetAt?.ToString("s") ?? "null");
bool specOk = true;
foreach (var odd in new[] { "[1,2]", "{\"components\":null}", "{\"host\":\"a.example\",\"schemes\":[1,null],\"paths\":{\"/x\":{\"get\":{}}}}", "\"text\"" })
    try { DocsScanner.ReadSpec(odd, new DocsScanResult()); } catch (Exception ex) { specOk = false; Console.WriteLine($"      {odd} -> {ex.GetType().Name}"); }
Check("odd OpenAPI specs are read without throwing", specOk);
var rootArray = Sources.ParseMarcel("[{\"API\":\"Cats\",\"Link\":\"https://cats.example\",\"Auth\":\"\"}]");
bool noEntries = false; try { Sources.ParseMarcel("{\"apis\":[]}"); } catch (InvalidDataException ex) { noEntries = ex.Message.Contains("entries"); }
Check("custom JSON: a bare array works, a wrong shape says what is expected", rootArray is [{ Name: "Cats" }] && noEntries);
var nulls = Backup.Read("{\"App\":\"ApiScout\",\"Favourites\":null,\"Tags\":{\"k\":null,\"j\":[\"x\",null]},\"Notes\":null}");
Check("import: nulls in a hand-edited file are harmless", nulls.Favourites.Count == 0 && nulls.Notes.Count == 0 && nulls.Tags is { Count: 1 } && nulls.Tags["j"].SequenceEqual(["x"]) && Backup.Import(store, nulls, null) is { Tagged: 1 });
bool stretched = false; try { Backup.Read("{\"App\":\"ApiScout\",\"Secrets\":\"AAAA\",\"Salt\":\"AAAA\",\"Iterations\":2147483647}"); } catch (InvalidDataException) { stretched = true; }
Check("import: an absurd PBKDF2 count is refused instead of hanging", stretched);
File.WriteAllText(Path.Combine(dir, "userdata.json"), "{ not json");
var afterDamage = new Store();
Check("damaged userdata.json: a copy is kept before anything overwrites it", afterDamage.User.Favourites.Count == 0 && Directory.GetFiles(dir, "userdata.json.unreadable-*").Length == 1);
Check("no temp files left behind by saves", Directory.GetFiles(dir, "*.tmp").Length == 0, string.Join(", ", Directory.GetFiles(dir, "*.tmp").Select(Path.GetFileName)));

// ---- what changed
var cBefore = new Catalog { ScannedAt = t0, Entries = [
    new ApiEntry { Key = "same", Name = "Same", Url = "https://same.example", Auth = AuthKind.ApiKey, Description = "x" },
    new ApiEntry { Key = "opened", Name = "Opened up", Url = "https://opened.example", Auth = AuthKind.ApiKey, Description = "x", Category = "Test" },
    new ApiEntry { Key = "paywall", Name = "Paywalled", Url = "https://paywall.example", Auth = AuthKind.ApiKey, Description = "Has a free tier" },
    new ApiEntry { Key = "reworded", Name = "Reworded", Url = "https://reworded.example", Auth = AuthKind.ApiKey, Description = "old words" },
    new ApiEntry { Key = "gone", Name = "Gone", Url = "https://gone.example", Category = "Test" } ] };
var cAfter = new Catalog { ScannedAt = t0.AddDays(7), Entries = [
    new ApiEntry { Key = "same", Name = "Same", Url = "https://same.example", Auth = AuthKind.ApiKey, Description = "x" },
    new ApiEntry { Key = "opened", Name = "Opened up", Url = "https://opened.example", Auth = AuthKind.None, Description = "x", Category = "Test" },
    new ApiEntry { Key = "paywall", Name = "Paywalled", Url = "https://paywall.example", Auth = AuthKind.ApiKey, Description = "Has a free tier", Pricing = "paid" },
    new ApiEntry { Key = "reworded", Name = "Reworded", Url = "https://reworded.example", Auth = AuthKind.ApiKey, Description = "new words" },
    new ApiEntry { Key = "fresh", Name = "Fresh | one", Url = "https://fresh.example", Category = "Test" } ] };
var scanReport = ChangeLog.Build(cAfter, cBefore, new Dictionary<string, DocsScanResult>(), "Scan", 1);
Check("what changed: new and gone", scanReport.Added is [{ Key: "fresh" }] && scanReport.Removed is [{ Key: "gone" }] && scanReport.Total == 5);
Check("what changed: auth and free-access changes, rewording alone is not one", scanReport.Changed.Count == 2
    && scanReport.Changed[0] is { Key: "opened", What: "Auth: API key → No key  ·  Free access: Not stated → Full free access" }
    && scanReport.Changed[1] is { Key: "paywall", What: "Free access: Free tier (limited) → Demo / trial only" }, string.Join(" || ", scanReport.Changed.Select(c => $"{c.Key}: {c.What}")));
var scanMd = ChangeLog.ToMarkdown(scanReport);
Check("what changed: markdown report", scanMd.Contains("1 new, 1 gone, 2 changed") && scanMd.Contains("- [Fresh \\| one](https://fresh.example) - Test") && scanMd.Contains("### Gone (1)") && scanMd.Contains("1 source(s) could not be read"));
store.AddScanReport(scanReport);
for (int i = 0; i < Store.ScanReportsKept + 3; i++) store.AddScanReport(new ScanReport { At = t0.AddDays(8 + i), Trigger = "Background scan" });
var reportsAgain = new Store().ScanReports;
Check("what changed: kept on disk, newest first, capped", reportsAgain.Count == Store.ScanReportsKept && reportsAgain[0].At == t0.AddDays(8 + Store.ScanReportsKept + 2) && reportsAgain[0].Label.EndsWith("no changes"), reportsAgain.FirstOrDefault()?.Label ?? "");

// ---- collections, API of the day, updates
Check("updates: newest version tag wins, other tags ignored", UpdateChecker.FromTags(["v1.2.0", "v1.10.0", "v1.9", "nightly", "V2.0.0-beta"], new Version(1, 4, 0), "test") is { Ok: true, Newer: true, Latest: { Major: 1, Minor: 10 } }
    && UpdateChecker.FromTags(["v1.4"], new Version(1, 4, 0), "test") is { Ok: true, Newer: false } && UpdateChecker.FromTags(["nightly"], new Version(1, 0), "test") is { Ok: false });
// the combined GitHub repository holds both apps: desktop-v… tags count, mobile-v… tags do not
Check("updates: desktop-v tags of the combined repository count, mobile-v tags are ignored",
    UpdateChecker.FromTags(["desktop-v1.11.0", "mobile-v9.0.0", "v1.10.0"], new Version(1, 4, 0), "test") is { Newer: true, Latest: { Major: 1, Minor: 11 } }
    && UpdateChecker.FromTags(["mobile-v9.0.0"], new Version(1, 4, 0), "test") is { Ok: false });
var fromJson = UpdateChecker.FromLatestJson("\uFEFF{\"version\":\"1.5.0\",\"download\":\"https://example.com/a.zip\"}", new Version(1, 4, 0), "test");
Check("updates: latest.json (with a byte-order mark)", fromJson is { Newer: true, Download: "https://example.com/a.zip" } && fromJson.Message.Contains("1.5.0 is available") && UpdateChecker.FromLatestJson("{}", new Version(1, 0), "t") is { Ok: false });
// the default update source is the folder this build came from; it is a git repository of its own on the dev PC,
// but a subfolder of the combined repository on a CI checkout - then these two checks have nothing to read and are skipped
var repoDir = UpdateChecker.DefaultFeed.TrimEnd('\\');
var repoTags = UpdateChecker.GitTags(repoDir);
Check("updates: the default source is the ApiScout project folder", repoDir.EndsWith("ApiScout", StringComparison.OrdinalIgnoreCase) && Directory.Exists(repoDir), repoDir);
if (repoTags is not null)
{
    Check("updates: tags read straight from .git", repoTags.Contains("v1.1.0") && UpdateChecker.GitTags(Path.GetTempPath()) is null, string.Join(", ", repoTags));
    var viaFolder = await UpdateChecker.CheckAsync(repoDir, new Version(1, 0, 0), CancellationToken.None);
    Check("updates: a repo folder as the source", viaFolder is { Ok: true, Newer: true } && (await UpdateChecker.CheckAsync(@"Z:\nowhere\at-all", new Version(1, 0), CancellationToken.None)) is { Ok: false }, viaFolder.Message);
}
else Console.WriteLine($"  (skipped: {repoDir} is not a git repository of its own - a CI checkout of the combined repository)");

// ---- self-update
var updDir = Path.Combine(Path.GetTempPath(), "apiscout-upd-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(updDir);
var fakeExe = Path.Combine(updDir, "ApiScout.exe");
File.WriteAllText(fakeExe, "old version");
var newBytes = new byte[1_200_000]; new Random(3).NextBytes(newBytes);
var updZip = Path.Combine(updDir, "ApiScout-9.9.9-portable.zip");
using (var za = System.IO.Compression.ZipFile.Open(updZip, System.IO.Compression.ZipArchiveMode.Create))
{
    using (var ze = za.CreateEntry("ApiScout.exe").Open()) ze.Write(newBytes);
    using var readme = za.CreateEntry("README.md").Open();
}
var updSum = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(updZip)));
File.WriteAllText(Path.Combine(updDir, "latest.json"), $"{{\"version\":\"9.9.9\",\"download\":\"ApiScout-9.9.9-portable.zip\",\"sha256\":\"{updSum}\"}}");
var updInfo = await UpdateChecker.CheckAsync(updDir, new Version(1, 0, 0), CancellationToken.None);
Check("update: latest.json names the zip and its checksum", updInfo is { Newer: true, Sha256.Length: 64 } && updInfo.Package == updZip, updInfo.Message);
var badSum = await Updater.InstallAsync(updInfo with { Sha256 = new string('0', 64) }, null, new Progress<double>(), CancellationToken.None, fakeExe);
Check("update: a wrong checksum changes nothing", !badSum.Ok && File.ReadAllText(fakeExe) == "old version" && !File.Exists(fakeExe + ".old") && !File.Exists(fakeExe + ".new"), badSum.Message);
var noZip = await Updater.InstallAsync(updInfo with { Package = Path.Combine(updDir, "missing.zip") }, null, new Progress<double>(), CancellationToken.None, fakeExe);
double lastProgress = 0;
var installed = await Updater.InstallAsync(updInfo, null, new SyncProgress(p => lastProgress = p), CancellationToken.None, fakeExe);
Check("update: the exe is swapped, the old one kept as .old until the next start", !noZip.Ok && installed.Ok && File.ReadAllBytes(fakeExe).AsSpan().SequenceEqual(newBytes) && File.ReadAllText(fakeExe + ".old") == "old version"
    && !File.Exists(fakeExe + ".new") && lastProgress == 100 && Directory.GetFiles(Path.GetTempPath(), "apiscout-update-*.zip").Length == 0, installed.Message);
Directory.Delete(updDir, true);
Check("test all: the summary line", MainViewModel.PageTestLine(3, ["Broken", "Other"], 1, 2).Contains("Tested 5 at ") && MainViewModel.PageTestLine(3, ["Broken", "Other"], 1, 2).Contains("3 passed  ·  2 failed (Broken, Other)  ·  1 skipped - still rate limited  ·  2 skipped - no request"));

// ---- request variables
var vars = RequestVariables.Parse("city = New York\n {units}=metric \nkey = nope\nbroken line\nEmpty =\n9bad = x");
var fixedNow = new DateTime(2026, 9, 21, 12, 0, 0);
Check("variables: parsed, {key} and bad names refused", vars.Count == 3 && vars["CITY"] == "New York" && vars["units"] == "metric" && vars["empty"] == "");
Check("variables: filled (URL-encoded in the address), built-ins, unknown and {key} left alone",
    RequestVariables.Fill("https://a.example/w/{city}?u={units}&d={today}&k={key}&x={nope}", vars, escape: true, fixedNow) == "https://a.example/w/New%20York?u=metric&d=2026-09-21&k={key}&x={nope}"
    && RequestVariables.Fill("X-City: {city} {yesterday}", vars, escape: false, fixedNow) == "X-City: New York 2026-09-20");
Check("variables: what is still missing, and which names deserve a line", RequestVariables.Missing("/{city}/{id}/{empty}/{today}/{key}", vars).SequenceEqual(["id", "empty"]) && RequestVariables.Names("/{city}/{today}/{key}/{id}").SequenceEqual(["city", "id"]));
var varClient = ClientGenerator.Generate("Weather", [new TestHistoryEntry { At = fixedNow, Method = "GET", Ok = true, Url = "https://a.example/v1/forecast/{city}/days/{days}?units={units}&from={today}&api_key={key}", Headers = "X-Lang: {lang}", Response = "{\"t\":1}" }],
    null, new Dictionary<string, string> { ["city"] = "New York", ["days"] = "3", ["units"] = "metric", ["lang"] = "en" }) ?? "";
Check("variables: path variables become parameters, the rest defaults", varClient.Contains("(string city = \"New York\", int days = 3, string units = \"metric\", string from = \"2026-09-21\", CancellationToken ct = default)")
    && varClient.Contains("/v1/forecast/{Uri.EscapeDataString(city)}/days/{days}?units={Uri.EscapeDataString(units)}&from={Uri.EscapeDataString(from)}&api_key={Uri.EscapeDataString(_apiKey)}") && varClient.Contains("(\"X-Lang\", \"en\")"),
    varClient.Split('\n').FirstOrDefault(l => l.Contains("var url")) ?? "");
if (args.SkipWhile(a => a != "--client-out").Skip(1).FirstOrDefault() is { } outVars) File.WriteAllText(outVars + ".vars.cs", varClient);

// ---- more about this API
var insight = new ApiInfo();
ApiInsight.ReadHtml("<html><head><title>Foo API</title><meta name=\"description\" content=\"Foo API gives you live and historical foo data for every region, free for personal use.\"></head><body><nav><ul><li>Home page link here now</li></ul></nav>" +
    "<h1>Foo API</h1><p>Short.</p><p>The Foo API is a simple REST interface that returns JSON and needs no authentication for the basic endpoints at all.</p><h2>Features</h2><ul><li>Live foo levels for 200 regions, updated hourly</li><li>Ten years of history</li><li>Login</li></ul>" +
    "<h2>Pricing</h2><h2>Endpoints</h2><p>We use cookies and you accept our privacy policy by reading this long enough sentence right here.</p><script>var x = '<li>not a feature of the api at all</li>';</script></body></html>", insight);
Check("more info: summary, features and sections from a docs page; nav, scripts and legal text ignored", insight.Title == "Foo API" && insight.Summary.StartsWith("Foo API gives you live") && insight.Summary.Contains("simple REST interface") && !insight.Summary.Contains("cookies")
    && insight.Features.SequenceEqual(["Live foo levels for 200 regions, updated hourly", "Ten years of history"]) && insight.Sections.SequenceEqual(["Foo API", "Features", "Endpoints"]), $"{insight.Features.Count} features: {string.Join(" | ", insight.Features)} / {string.Join(" | ", insight.Sections)}");
var mdInfo = new ApiInfo();
ApiInsight.ReadMarkdown("# Bar API\n\n[![badge](x.svg)](y)\n\nBar is a **free** JSON API for bar opening hours around the world, maintained by [volunteers](https://v.example).\n\n## Features\n\n- Opening hours for 40,000 bars\n- No key needed for reads\n- x\n\n## Installation\n\n```\n- not a feature inside a code block at all\n```\n", mdInfo);
Check("more info: a GitHub README", mdInfo.Title == "Bar API" && mdInfo.Summary.StartsWith("Bar is a free JSON API") && mdInfo.Summary.Contains("volunteers.") && mdInfo.Features.SequenceEqual(["Opening hours for 40,000 bars", "No key needed for reads"]) && mdInfo.Sections.SequenceEqual(["Features"]), mdInfo.Summary + " / " + string.Join(" | ", mdInfo.Features));
var benefits = ApiInsight.Benefits(new ApiRow(new ApiEntry { Name = "Open", Url = "https://open.example", Auth = AuthKind.None, Https = true, Cors = "Yes", Health = 97, Sources = ["a", "b", "c"] }));
Check("more info: benefits from the known facts", benefits.Count == 6 && benefits[0].StartsWith("No key") && benefits[1].StartsWith("Completely free") && benefits.Any(b => b.StartsWith("CORS enabled")) && benefits.Any(b => b.Contains("97%")), string.Join(" | ", benefits));

// ---- dashboard
var dashStore = new Store();
var soon = DateTime.Now;
dashStore.SaveCatalog(new Catalog { ScannedAt = soon, BaselineAt = soon.AddDays(-40), Entries = [
    new ApiEntry { Key = "open", Name = "Open", Url = "https://open.example", Auth = AuthKind.None, Category = "Test", FirstSeen = soon.AddDays(-40) },
    new ApiEntry { Key = "open2", Name = "Open two", Url = "https://open2.example", Auth = AuthKind.None, Category = "Test", FirstSeen = soon.AddDays(-2) },
    new ApiEntry { Key = "tier", Name = "Tier", Url = "https://tier.example", Auth = AuthKind.ApiKey, Description = "Has a free tier", Category = "Test", FirstSeen = soon.AddDays(-10) },
    new ApiEntry { Key = "paid", Name = "Paid", Url = "https://paid.example", Auth = AuthKind.ApiKey, Pricing = "paid", Category = "Test", FirstSeen = soon.AddDays(-40) },
    new ApiEntry { Key = "dunno", Name = "Dunno", Url = "https://dunno.example", Auth = AuthKind.ApiKey, Category = "Test", FirstSeen = soon.AddDays(-40) } ] });
dashStore.User.RateLimits["paid"] = new RateLimitNote { Until = soon.AddHours(2) }; dashStore.SaveUser();
var dashVm = new MainViewModel(dashStore);
string TileText(string id) { var tile = dashVm.Dashboard.First(x => x.Id == id); return $"{tile.Value}|{tile.Sub}"; }
Check("dashboard: access levels, new this week, rate limited", TileText("full") == "2|40%" && TileText("tier") == "1|20%" && TileText("trial") == "1|20%" && TileText("unknown") == "1|20%"
    && TileText("new") == "1|2 in 14 days" && TileText("limited").StartsWith("1|") && TileText("links") == "-|not checked yet", string.Join("  ", dashVm.Dashboard.Select(x => $"{x.Id}={x.Value}|{x.Sub}")));
dashVm.DashboardTileCommand.Execute("tier");
bool tierOn = dashVm.Rows.Count == 1 && dashVm.Dashboard.First(x => x.Id == "tier").IsActive;
dashVm.DashboardTileCommand.Execute("tier");
dashVm.DashboardTileCommand.Execute("limited");
Check("dashboard: a tile filters the list, a second click clears it", tierOn && dashVm.Rows is [{ Name: "Paid" }] && dashVm.SelectedCategory?.Name == MainViewModel.LimitedCategory && !dashVm.Dashboard.First(x => x.Id == "tier").IsActive);
dashVm.AddToCollection([.. dashVm.Rows.Take(0)], "Nothing");
var allRows = new List<ApiRow>(); dashVm.ClearFiltersCommand.Execute(null); allRows.AddRange(dashVm.Rows);
dashVm.AddToCollection([allRows[0], allRows[2]], " Weather stuff ");
dashVm.AddToCollection([allRows[2], allRows[1]], "weather STUFF");
dashVm.ShowShortlist = true; dashVm.ShortlistPage = "Weather stuff";
Check("collections: created once whatever the case, order kept, own page", dashStore.User.Collections is { Count: 1 } && dashStore.User.Collections["Weather stuff"].Count == 3 && dashVm.ShortlistPages.Count == 2
    && dashVm.IsCollectionPage && dashVm.ShortlistRows.Select(r => r.Key).SequenceEqual([allRows[0].Key, allRows[2].Key, allRows[1].Key]) && allRows[0].CollectionsLabel == "Weather stuff", string.Join(",", dashVm.ShortlistRows.Select(r => r.Key)));
dashVm.ShortlistSelected = allRows[1];
bool movedLeft = dashVm.MoveSelectedInCollection(-1), movedDrag = dashVm.MoveInCollection(allRows[0], allRows[1]);
Check("collections: Ctrl+arrow and drag reorder, and the order is saved", movedLeft && movedDrag && !dashVm.MoveSelectedInCollection(-5)
    && new Store().User.Collections["Weather stuff"].SequenceEqual([allRows[1].Key, allRows[0].Key, allRows[2].Key]) && dashVm.ShortlistRows[0] == allRows[1], string.Join(",", dashStore.User.Collections["Weather stuff"]));
dashVm.RemoveFromCollectionCommand.Execute(allRows[2]);
bool renamed = dashVm.RenameCollection("Climate") && !dashVm.RenameCollection("  ");
Check("collections: remove, rename, survive a restart", renamed && dashVm.ShortlistPage == "Climate" && dashVm.ShortlistRows.Count == 2 && new Store().User.Collections["Climate"].Count == 2 && allRows[2].CollectionsLabel == "");
var withCollections = Backup.Read(Backup.Export(dashStore, null));
dashStore.User.Collections["Climate"].RemoveAt(0);
Check("collections: exported and merged back on import", withCollections.Collections["Climate"].Count == 2 && Backup.Import(dashStore, withCollections, null).CollectionEntries == 1 && dashStore.User.Collections["Climate"].Count == 2);
dashVm.DeleteCollection();
Check("collections: delete goes back to the shortlist page", dashVm.ShortlistPage == MainViewModel.ShortlistPageName && dashVm.ShortlistPages.Count == 1 && dashStore.User.Collections.Count == 0);
dashVm.ShowShortlist = false;
Check("API of the day: none without a known working example", dashVm.ApiOfTheDay is null);
Directory.Delete(dir, true);

// ---- C# client from the requests that worked
var past = new List<TestHistoryEntry>
{
    new() { At = DateTime.Now, Method = "GET", Ok = true, Url = "https://api.example.com/v1/users/2?api_key={key}&lang=en&limit=5&lat=52.52&in=x",
            Headers = "X-Trace: on\nAuthorization: Bearer {key}", Response = "{\"data\":{\"id\":2,\"first_name\":\"Janet\"},\"tags\":[\"a\"]}" },
    new() { At = DateTime.Now.AddMinutes(-1), Method = "GET", Ok = true, Url = "https://api.example.com/v1/users/7?api_key={key}&lang=de&limit=1&lat=1.5&in=y", Response = "{\"older\":true}" },
    new() { At = DateTime.Now.AddMinutes(-2), Method = "POST", Ok = true, Url = "https://api.example.com/v1/users", Body = "{\"name\":\"morpheus\",\"job\":\"leader\"}", Response = "{\"id\":\"12\",\"data\":{\"x\":1}}" },
    new() { At = DateTime.Now.AddMinutes(-3), Method = "GET", Ok = true, Url = "https://api.example.com/api/json/v1/1/search.php?s=margarita \"quoted\"", Response = "not json <html>" },
    new() { At = DateTime.Now.AddMinutes(-4), Method = "DELETE", Ok = true, Url = "https://api.example.com/v1/users/2", Response = "" },
    new() { At = DateTime.Now.AddMinutes(-5), Method = "GET", Ok = false, Url = "https://api.example.com/v1/broken", Response = "{\"error\":1}" },
    new() { At = DateTime.Now.AddMinutes(-6), Method = "GET", Ok = true, Url = "https://api.example.com/planetary/apod?api_key=DEMO_KEY&date=2024-01-01", Response = "[{\"title\":\"x\"}]" },
};
var client = ClientGenerator.Generate("Example API", past, "DEMO_KEY") ?? "";
if (args.SkipWhile(a => a != "--client-out").Skip(1).FirstOrDefault() is { } outFile) File.WriteAllText(outFile, client);
Check("client: class, constructor with the demo key as default", client.Contains("public sealed class ExampleAPIClient") && client.Contains("public ExampleAPIClient(HttpClient http, string apiKey = \"DEMO_KEY\")"));
Check("client: id in the path and typed query parameters with the tested defaults",
    client.Contains("GetUserAsync(int userId = 2, string lang = \"en\", int limit = 5, double lat = 52.52, string inValue = \"x\", CancellationToken ct = default)")
    && client.Contains("/v1/users/{userId}?api_key={Uri.EscapeDataString(_apiKey)}&lang={Uri.EscapeDataString(lang)}&limit={limit}&lat={lat.ToString(CultureInfo.InvariantCulture)}"), client.Split('\n').FirstOrDefault(l => l.Contains("GetUserAsync")) ?? "");
Check("client: same request shape only once, newest wins", !client.Contains("GetUser2Async") && !client.Contains("Older"));
Check("client: key header uses the constructor key, plain header stays", client.Contains("TryAddWithoutValidation(\"Authorization\", $\"Bearer {_apiKey}\")") && client.Contains("TryAddWithoutValidation(\"X-Trace\", \"on\")"));
Check("client: JSON body becomes a request class", client.Contains("PostUsersAsync(PostUsersRequest body, CancellationToken") && client.Contains("JsonContent.Create(body)") && client.Contains("public sealed class PostUsersRequest"));
Check("client: class names stay unique across methods", client.Contains("public sealed class Data\n".Replace("\n", Environment.NewLine)) && client.Contains("public sealed class Data2"));
Check("client: non-JSON answer -> string, empty answer -> Task, failed test left out",
    client.Contains("public async Task<string> GetSearchAsync(string s = \"margarita \\\"quoted\\\"\"") && client.Contains("public async Task DeleteUserAsync(int userId = 2") && !client.Contains("broken"));
Check("client: array answer and demo key in the URL", client.Contains("Task<List<GetApodResponseItem>?> GetApodAsync(string date = \"2024-01-01\"") && !client.Split('\n').Any(l => !l.TrimStart().StartsWith("//") && l.Contains("api_key=DEMO_KEY")));
Check("client: no saved key, no {key} text left in code lines", !client.Split('\n').Any(l => !l.TrimStart().StartsWith("//") && l.Contains("{key}")));
Check("client: nothing without a successful test", ClientGenerator.Generate("X", past.Where(h => !h.Ok)) is null);
var manyClient = ClientGenerator.GenerateMany("Side project!", [("Example API", past, "DEMO_KEY", null), ("Dogs", past.Where(h => h.Method == "DELETE"), null, null), ("Dogs", past.Where(h => h.Method == "POST"), null, null), ("Untested", [], null, null)]) ?? "";
if (args.SkipWhile(a => a != "--client-out").Skip(1).FirstOrDefault() is { } outMany) File.WriteAllText(outMany + ".many.cs", manyClient);
Check("collection client: a facade over one client per tested API", manyClient.Contains("public sealed class SideProjectApis") && manyClient.Contains("public SideProjectApis(HttpClient http, string exampleAPIKey = \"DEMO_KEY\")")
    && manyClient.Contains("ExampleAPI = new ExampleAPIClient(http, exampleAPIKey);") && manyClient.Contains("Dogs = new DogsClient(http);") && manyClient.Contains("Dogs2 = new DogsClient2(http);") && !manyClient.Contains("Untested"));
Check("collection client: usings once, class names unique across APIs", manyClient.Split("using System.Net.Http;").Length == 2 && System.Text.RegularExpressions.Regex.Matches(manyClient, @"class PostUsersRequest\b").Count == 1 && manyClient.Contains("public sealed class PostUsersRequest2"));
Check("collection client: nothing without a tested API", ClientGenerator.GenerateMany("X", [("A", [], null, null)]) is null);
Check("client: default pick is the newest test of each request", ClientGenerator.Usable(past).Count == 6 && ClientGenerator.NewestOfEach(past).Count == 5 && !ClientGenerator.NewestOfEach(past).Any(h => h.Response.Contains("older")));
var pickedClient = ClientGenerator.Generate("Example API", past.Where(h => h.Method == "DELETE"), "DEMO_KEY") ?? "";
Check("client: only the picked requests become methods", pickedClient.Contains("DeleteUserAsync") && !pickedClient.Contains("GetUserAsync") && !pickedClient.Contains("_apiKey") && ClientGenerator.ClassNameFor("Example API") == "ExampleAPIClient");

if (!offline)
{
    Console.WriteLine("== Live scan (default sources) ==");
    var ids = Sources.All.Where(s => s.DefaultOn).Select(s => (s.Id, s.Name)).ToList();
    var sw = System.Diagnostics.Stopwatch.StartNew();
    var outcome = await Scanner.RunAsync(ids, new Progress<ScanProgress>(), CancellationToken.None);
    foreach (var n in outcome.Notes) Console.WriteLine("        " + n);
    var entries = outcome.Catalog.Entries;
    var rows = entries.Select(e => new ApiRow(e)).ToList();
    Check("every source answered", !outcome.Notes.Any(n => n.Contains("failed")));
    Check("found > 2,000 unique APIs", entries.Count > 2000, $"{entries.Count:N0} in {sw.Elapsed.TotalSeconds:F1}s");
    Check("duplicates were merged", entries.Count(e => e.Sources.Count > 1) > 300, $"{entries.Count(e => e.Sources.Count > 1):N0} in 2+ sources");
    var cats = entries.GroupBy(e => e.Category).OrderByDescending(g => g.Count()).ToList();
    Check("sensible category count", cats.Count is > 25 and < 70, $"{cats.Count}");
    double other = 100.0 * entries.Count(e => e.Category == "Other") / entries.Count;
    Check("'Other' under 8%", other < 8, $"{other:F1}%");
    Check("demo keys matched", rows.Count(r => r.HasDemoKey) >= 8, $"{rows.Count(r => r.HasDemoKey)}");
    Check("sign-up links matched", rows.Count(r => r.HasSignupUrl) >= 60, $"{rows.Count(r => r.HasSignupUrl)}");
    Console.WriteLine("        " + string.Join(", ", cats.Select(g => $"{g.Key} {g.Count()}")));
    Console.WriteLine("        auth: " + string.Join(", ", entries.GroupBy(e => e.Auth).Select(g => $"{g.Key} {g.Count()}")));

    Console.WriteLine("== Live docs scan + link check ==");
    var live = await DocsScanner.ScanAsync(new ApiEntry { Name = "OMDb", Url = "https://www.omdbapi.com/" }, CancellationToken.None);
    Check("OMDb docs: finds the API key page", live.Items.Any(i => i.Kind == "Sign-up link" && i.Value.Contains("apikey")), live.Error ?? $"{live.Items.Count} items");
    var t1 = await ApiTester.SendAsync(nasa.DefaultTestUrl, nasa.DefaultTestHeader, CancellationToken.None);
    // DEMO_KEY is shared per IP and runs out: a correctly recognised "rate limited" is as good a pass as the JSON itself
    Check("test NASA example -> JSON (or a recognised rate limit)", (t1 is { Ok: true, IsJson: true } && t1.Body.Contains("\"title\"")) || (t1.Rate is { Limited: true } && t1.Summary.Contains("should work again")), t1.Summary.Replace("\n", " / "));
    var t2 = await ApiTester.SendAsync(reqres.DefaultTestUrl, reqres.DefaultTestHeader, CancellationToken.None);
    Check("test ReqRes with header -> JSON", t2 is { Ok: true, IsJson: true }, t2.Summary.Split('\n')[0]);
    var t3 = await ApiTester.SendAsync("https://www.omdbapi.com/?t=alien", null, CancellationToken.None);
    Check("no key -> 401 with a hint", !t3.Ok && t3.Summary.Contains("wants a key"), t3.Summary.Split('\n')[0]);
    var t4 = await ApiTester.SendAsync(new ApiTestRequest("POST", "https://postman-echo.com/post", "X-Test: apiscout", "{\"name\":\"morpheus\",\"job\":\"leader\"}"), CancellationToken.None);
    Check("POST JSON body is received as JSON", t4 is { Ok: true, IsJson: true } && t4.Body.Contains("\"job\": \"leader\"") && t4.Body.Contains("application/json") && t4.Body.Contains("apiscout"), t4.Summary.Split('\n')[0]);
    var t5 = await ApiTester.SendAsync(new ApiTestRequest("PUT", "https://postman-echo.com/put", "", "colour=blue&size=9"), CancellationToken.None);
    Check("PUT form body is received as a form", t5.Ok && t5.Body.Contains("\"colour\": \"blue\"") && t5.Body.Contains("x-www-form-urlencoded"), t5.Summary.Split('\n')[0]);
    var t6 = await ApiTester.SendAsync(new ApiTestRequest("DELETE", "https://postman-echo.com/delete", "", ""), CancellationToken.None);
    Check("DELETE works", t6.Ok, t6.Summary.Split('\n')[0]);
    // redirects: followed by ApiTester itself, and the key header must not travel to another host
    var sameHost = await ApiTester.SendAsync(new ApiTestRequest("GET", "https://postman-echo.com/redirect-to?url=https%3A%2F%2Fpostman-echo.com%2Fget%3Fa%3D1", "X-Api-Key: secret-test-123", ""), CancellationToken.None);
    Check("live: same-host redirect is followed with the headers", sameHost is { Ok: true, IsJson: true } && sameHost.Body.Contains("secret-test-123") && sameHost.Summary.Contains("Redirected to"), sameHost.Summary.Replace("\n", " / "));
    var crossHost = await ApiTester.SendAsync(new ApiTestRequest("GET", "https://postman-echo.com/redirect-to?url=https%3A%2F%2Fhttpbin.org%2Fget", "X-Api-Key: secret-test-123", ""), CancellationToken.None);
    Check("live: cross-host redirect is followed WITHOUT the key header", crossHost.Ok && crossHost.Body.Contains("httpbin.org") && !crossHost.Body.Contains("secret-test-123") && crossHost.Summary.Contains("your headers were not sent there"), crossHost.Summary.Replace("\n", " / "));
    var rePost = await ApiTester.SendAsync(new ApiTestRequest("POST", "https://httpbin.org/redirect-to?url=https%3A%2F%2Fpostman-echo.com%2Fpost&status_code=307", "X-Api-Key: secret-test-123", "{\"a\":1}"), CancellationToken.None);
    Check("live: a 307 to another host is shown, not re-posted", !rePost.Ok && rePost.Summary.StartsWith("307") && rePost.Summary.Contains("not followed automatically"), rePost.Summary.Replace("\n", " / "));
    var access = rows.GroupBy(r => r.AccessLabel).ToDictionary(g => g.Key, g => g.Count());
    Check("every access level is populated", access.Count == 4 && access["Full free access"] > 1000 && access["Demo / trial only"] > 30, string.Join(", ", access.Select(a => $"{a.Key} {a.Value}")));
    var priced = await DocsScanner.ScanAsync(new ApiEntry { Name = "WeatherAPI", Url = "https://www.weatherapi.com/" }, CancellationToken.None);
    Check("live: pricing page found and read", priced.Items.Any(i => i.Kind == "Pricing page") && priced.Items.Any(i => i.Kind == "Pricing"),
        priced.Error ?? string.Join(" | ", priced.Items.Where(i => i.Kind.StartsWith("Pricing")).Select(i => i.Value).Take(3)));
    var liveEp = await DocsScanner.ScanAsync(new ApiEntry { Name = "TheCocktailDB", Url = "https://www.thecocktaildb.com/api.php" }, CancellationToken.None);
    var firstEp = liveEp.Items.FirstOrDefault(i => i.IsEndpoint);
    Check("live: example endpoints found in real docs", firstEp is not null, liveEp.Error ?? string.Join(" | ", liveEp.Items.Where(i => i.IsEndpoint).Take(3).Select(i => i.Value)));
    if (firstEp is not null)
    {
        var tEp = await ApiTester.SendAsync(new ApiTestRequest(firstEp.Method, firstEp.Value, "", ""), CancellationToken.None);
        Check("live: the found endpoint answers with JSON", tEp is { Ok: true, IsJson: true }, tEp.Summary.Split('\n')[0]);
    }
    LogoService.Folder = Path.Combine(Path.GetTempPath(), "apiscout-logos-" + Guid.NewGuid().ToString("N"));
    var logo = await LogoService.GetAsync("nasa.gov");
    Check("live: brand icon fetched and cached on disk", logo is not null && File.Exists(Path.Combine(LogoService.Folder, "nasa.gov.png")), $"{(logo as System.Windows.Media.Imaging.BitmapSource)?.PixelWidth}px");
    Check("live: GitHub owner avatar as the brand", await LogoService.GetAsync(LogoService.Brand("https://github.com/r-spacex/SpaceX-API")) is not null);
    Check("live: unknown site remembered as 'no icon'", await LogoService.GetAsync("no-such-site-apiscout.example") is null && File.Exists(Path.Combine(LogoService.Folder, "no-such-site-apiscout.example.none")));
    Directory.Delete(LogoService.Folder, true);
    var gen = JsonToCSharp.Generate(t2.Raw, "ReqResUser");
    Check("live: classes from a real response", gen is not null && gen.Contains("class ReqResUser") && gen.Contains("public Data Data ") && gen.Contains("[JsonPropertyName(\"first_name\")]"), gen?.Split('\n').Length + " lines");
    var link = await LinkChecker.CheckAsync("https://api.nasa.gov/", CancellationToken.None);
    Check("link check online", link.Label == "Online", $"{link.Code} {link.LatencyMs} ms");
    var dead = await LinkChecker.CheckAsync("https://no-such-host-apiscout.invalid/", CancellationToken.None);
    Check("link check down", dead.Label == "Down");
}

Console.WriteLine();
Console.WriteLine($"{pass} passed, {fail} failed");
return fail == 0 ? 0 : 1;

sealed class SyncProgress(Action<double> report) : IProgress<double> { public void Report(double value) => report(value); }
