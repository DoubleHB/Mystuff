using System.Text.Json;
using System.Text.RegularExpressions;
using ApiScout.Models;

namespace ApiScout.Services;

public sealed record SourceInfo(string Id, string Name, string Description, bool DefaultOn);

/// <summary>The places API Free looks. Each fetch returns raw entries; <see cref="Scanner"/> merges them.</summary>
public static class Sources
{
    public static readonly SourceInfo[] All =
    [
        new("public-apis", "public-apis (GitHub)", "The big community list - ~1,400 APIs with auth, HTTPS and CORS columns", true),
        new("public-api-lists", "public-api-lists (GitHub)", "Actively maintained fork with many newer APIs", true),
        new("marcelscruz", "publicapis.dev", "JSON database behind publicapis.dev - ~1,700 APIs", true),
        new("freepublicapis", "freepublicapis.com", "~650 keyless APIs, health-tested daily", true),
        new("n0shake", "n0shake/Public-APIs (GitHub)", "Older list of big-name platform APIs (auth not stated)", true),
        new("apisguru", "APIs.guru OpenAPI directory", "~2,500 machine-readable specs - includes paid cloud APIs, so off by default", false),
        new("github-discovery", "Discover more lists on GitHub", "Searches GitHub topics for other API lists and reads any README with API tables", false),
    ];

    public static Task<List<ApiEntry>> FetchAsync(string id, CancellationToken ct) => id switch
    {
        "public-apis" => Markdown("https://raw.githubusercontent.com/public-apis/public-apis/master/README.md", "public-apis", ct),
        "public-api-lists" => Markdown("https://raw.githubusercontent.com/public-api-lists/public-api-lists/master/README.md", "public-api-lists", ct),
        "n0shake" => Markdown("https://raw.githubusercontent.com/n0shake/Public-APIs/master/README.md", "n0shake", ct),
        "marcelscruz" => Marcel(ct),
        "freepublicapis" => FreePublicApis(ct),
        "apisguru" => ApisGuru(ct),
        "github-discovery" => GitHubDiscovery(ct),
        _ => Custom(id, ct),
    };

    private static async Task<List<ApiEntry>> Markdown(string url, string name, CancellationToken ct) =>
        MarkdownListParser.Parse(await Http.GetTextAsync(url, ct), name);

    private static async Task<List<ApiEntry>> Marcel(CancellationToken ct)
    {
        var json = await Http.GetTextAsync("https://raw.githubusercontent.com/marcelscruz/public-apis/main/db/resources.json", ct);
        return ParseMarcel(json);
    }

    internal static List<ApiEntry> ParseMarcel(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var list = new List<ApiEntry>();
        // { "entries": [ … ] } as publicapis.dev has it, or just the array
        var root = doc.RootElement;
        var entries = root.ValueKind == JsonValueKind.Array ? root
            : root.ValueKind == JsonValueKind.Object && root.TryGetProperty("entries", out var inner) && inner.ValueKind == JsonValueKind.Array ? inner
            : throw new InvalidDataException("the JSON has no \"entries\" list (expected the publicapis.dev shape: API, Description, Link, Category, Auth)");
        foreach (var e in entries.EnumerateArray())
        {
            var auth = Str(e, "Auth");
            var url = Str(e, "Link");
            if (url.Length == 0) continue;
            list.Add(new ApiEntry
            {
                Name = Str(e, "API"),
                Description = Str(e, "Description"),
                Url = url,
                RawCategory = Str(e, "Category"),
                AuthRaw = auth,
                Auth = MarkdownListParser.ParseAuth(auth),
                Cors = Str(e, "Cors") switch { "yes" => "Yes", "no" => "No", "" => "", _ => "Unknown" },
                Https = e.TryGetProperty("HTTPS", out var h) && (h.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    ? h.GetBoolean() : url.StartsWith("https"),
                Sources = ["publicapis.dev"],
            });
        }
        return list;
    }

    private static async Task<List<ApiEntry>> FreePublicApis(CancellationToken ct)
    {
        var json = await Http.GetTextAsync("https://www.freepublicapis.com/api/apis?limit=5000", ct);
        using var doc = JsonDocument.Parse(json);
        var list = new List<ApiEntry>();
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            var url = Str(e, "documentation");
            if (url.Length == 0) url = Str(e, "source");
            if (url.Length == 0) continue;
            list.Add(new ApiEntry
            {
                Name = Str(e, "title"),
                Description = Str(e, "description"),
                Url = url,
                // the site only lists APIs usable without signing up
                Auth = AuthKind.None,
                AuthRaw = "No (listed as keyless)",
                Https = url.StartsWith("https"),
                Health = e.TryGetProperty("health", out var h) && h.ValueKind == JsonValueKind.Number && h.TryGetInt32(out var hv) ? hv : null,
                Sources = ["freepublicapis.com"],
            });
        }
        return list;
    }

    private static async Task<List<ApiEntry>> ApisGuru(CancellationToken ct)
    {
        var json = await Http.GetTextAsync("https://api.apis.guru/v2/list.json", ct, maxBytes: 64_000_000, timeoutSeconds: 180);
        using var doc = JsonDocument.Parse(json);
        var list = new List<ApiEntry>();
        foreach (var api in doc.RootElement.EnumerateObject())
        {
            var versions = api.Value.GetProperty("versions");
            var preferred = Str(api.Value, "preferred");
            if (!versions.TryGetProperty(preferred, out var v))
            {
                using var en = versions.EnumerateObject();
                if (!en.MoveNext()) continue;
                v = en.Current.Value;
            }
            if (!v.TryGetProperty("info", out var info)) continue;

            string url = "";
            if (v.TryGetProperty("externalDocs", out var ext)) url = Str(ext, "url");
            if (url.Length == 0 && info.TryGetProperty("contact", out var contact)) url = Str(contact, "url");
            if (url.Length == 0) url = "https://" + api.Name.Split(':')[0];

            string cat = "";
            if (info.TryGetProperty("x-apisguru-categories", out var cats) && cats.ValueKind == JsonValueKind.Array && cats.GetArrayLength() > 0)
                cat = (cats[0].GetString() ?? "").Replace('_', ' ');

            var desc = MarkdownListParser.CleanText(Str(info, "description"));
            if (desc.Length > 320) desc = desc[..317] + "…";

            list.Add(new ApiEntry
            {
                Key = "guru:" + api.Name.ToLowerInvariant(),
                Name = Str(info, "title") is { Length: > 0 } t ? t : api.Name,
                Description = desc,
                Url = url,
                RawCategory = cat,
                Https = url.StartsWith("https"),
                SpecUrl = Str(v, "swaggerUrl"),
                Sources = ["APIs.guru"],
            });
        }
        // Hundreds of Azure/AWS/Google specs share one docs URL, so those keep their own id as the key.
        // Where the docs URL is unique, use it - then the entry merges with the same API from the other lists.
        foreach (var g in list.GroupBy(e => Scanner.MakeKey(e.Url, e.Name)).Where(g => g.Count() == 1))
            g.First().Key = "";
        return list;
    }

    private static async Task<List<ApiEntry>> GitHubDiscovery(CancellationToken ct)
    {
        string[] known = ["public-apis/public-apis", "public-api-lists/public-api-lists", "marcelscruz/public-apis", "n0shake/public-apis"];
        var repos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int refused = 0;
        foreach (var topic in new[] { "public-apis", "api-list", "free-api" })
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/search/repositories?q=topic:{topic}&sort=stars&per_page=10");
            req.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var resp = await Http.Client.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) { refused++; continue; } // keyless search allows 10 requests/minute
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            foreach (var item in doc.RootElement.GetProperty("items").EnumerateArray())
            {
                var full = Str(item, "full_name");
                if (full.Length > 0 && !known.Contains(full.ToLowerInvariant()))
                    repos.TryAdd(full, Str(item, "default_branch"));
            }
        }
        // counted as a failed source, so the scan keeps what discovery found last time instead of calling it gone
        if (repos.Count == 0 && refused > 0) throw new HttpRequestException("GitHub search refused the request (it allows 10 a minute without signing in) - try again in a minute");

        var list = new List<ApiEntry>();
        foreach (var (repo, branch) in repos.Take(16))
        {
            try
            {
                var md = await Http.GetTextAsync($"https://raw.githubusercontent.com/{repo}/{branch}/README.md", ct, maxBytes: 6_000_000, timeoutSeconds: 30);
                var found = Clean(MarkdownListParser.Parse(md, "GitHub: " + repo, strictTables: true));
                if (IsRealApiList(found)) list.AddRange(found);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested) { }
        }
        return list;
    }

    /// <summary>Drops affiliate / referral links - the mark of marketplace spam rather than an API's own docs.</summary>
    internal static List<ApiEntry> Clean(List<ApiEntry> found) =>
        [.. found.Where(e => !Regex.IsMatch(e.Url, @"[?&](fpr|ref|via|aff|affiliate|referral|utm_campaign)=", RegexOptions.IgnoreCase))];

    /// <summary>Quality gate for a README nobody has vetted: big enough, not one marketplace, not a translation of a list we already read.</summary>
    internal static bool IsRealApiList(List<ApiEntry> found)
    {
        if (found.Count < 25) return false;
        var topHost = found.GroupBy(e => Uri.TryCreate(e.Url, UriKind.Absolute, out var u) ? u.Host : "").Max(g => g.Count());
        if (topHost > found.Count * 0.35) return false;
        int nonLatin = found.Count(e => e.Description.Any(c => c >= 0x2E80));
        return nonLatin <= found.Count * 0.2;
    }

    /// <summary>A user-supplied URL: a README with tables/bullets, or JSON in the publicapis.dev shape.</summary>
    private static async Task<List<ApiEntry>> Custom(string url, CancellationToken ct)
    {
        var text = await Http.GetTextAsync(ToRaw(url), ct, maxBytes: 12_000_000);
        var host = Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : "custom";
        if (text.TrimStart() is ['{' or '[', ..])
        {
            try
            {
                var list = ParseMarcel(text);
                foreach (var e in list) e.Sources = [host];
                return list;
            }
            catch (JsonException) when (text.TrimStart()[0] == '[') { } // a README that opens with a [link] or a badge
        }
        return MarkdownListParser.Parse(text, host, bullets: true);
    }

    /// <summary>Accepts a normal GitHub repo/blob URL and points it at the raw README.</summary>
    internal static string ToRaw(string url)
    {
        var blob = Regex.Match(url, @"^https?://github\.com/([^/]+)/([^/]+)/blob/(.+)$");
        if (blob.Success) return $"https://raw.githubusercontent.com/{blob.Groups[1].Value}/{blob.Groups[2].Value}/{blob.Groups[3].Value}";
        var repo = Regex.Match(url, @"^https?://github\.com/([^/]+)/([^/#?]+)/?$");
        if (repo.Success) return $"https://raw.githubusercontent.com/{repo.Groups[1].Value}/{repo.Groups[2].Value}/HEAD/README.md";
        return url;
    }

    private static string Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";
}
