using System.Text.RegularExpressions;
using ApiScout.Models;

namespace ApiScout.Services;

public sealed record ScanProgress(string Message, int Done, int Total);

public sealed record ScanOutcome(Catalog Catalog, List<string> Notes, int FailedSources = 0);

/// <summary>Runs every enabled source in parallel, then merges, de-duplicates and categorises.</summary>
public static class Scanner
{
    public static async Task<ScanOutcome> RunAsync(IReadOnlyList<(string Id, string Name)> sources, IProgress<ScanProgress> progress, CancellationToken ct)
    {
        int done = 0, failed = 0;
        var notes = new List<string>();
        progress.Report(new($"Contacting {sources.Count} sources…", 0, sources.Count));

        var tasks = sources.Select(async s =>
        {
            try
            {
                var found = await Sources.FetchAsync(s.Id, ct);
                lock (notes) notes.Add($"{s.Name}: {found.Count:N0} APIs");
                progress.Report(new($"{s.Name}: {found.Count:N0} APIs", Interlocked.Increment(ref done), sources.Count));
                return found;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                Interlocked.Increment(ref failed);
                lock (notes) notes.Add($"{s.Name}: failed - {ex.Message}");
                progress.Report(new($"{s.Name}: failed", Interlocked.Increment(ref done), sources.Count));
                return [];
            }
        }).ToList();

        var results = await Task.WhenAll(tasks);
        ct.ThrowIfCancellationRequested();
        progress.Report(new("Merging and categorising…", sources.Count, sources.Count));

        // Task.WhenAll keeps source order, so earlier (better described) sources win ties
        var merged = Merge(results.SelectMany(r => r));
        return new(new Catalog { ScannedAt = DateTime.Now, Entries = merged }, notes, failed);
    }

    public const int NewForDays = 14;

    /// <summary>Carries FirstSeen over from the previous scan and dates everything else now. Returns what changed.</summary>
    public static (int Added, int Removed) StampFirstSeen(Catalog fresh, Catalog? previous)
    {
        if (previous is null || previous.Entries.Count == 0)
        {
            fresh.BaselineAt = fresh.ScannedAt;
            foreach (var e in fresh.Entries) e.FirstSeen = fresh.ScannedAt;
            return (0, 0);
        }
        var baseline = fresh.BaselineAt = previous.BaselineAt ?? previous.ScannedAt;
        var seen = new Dictionary<string, DateTime>();
        foreach (var e in previous.Entries) seen.TryAdd(e.Key, e.FirstSeen ?? baseline.Value);
        int added = 0;
        var now = new HashSet<string>();
        foreach (var e in fresh.Entries)
        {
            now.Add(e.Key);
            now.UnionWith(e.Aliases);
            // an API known before under a key that has since been folded into this one is not new
            DateTime? first = seen.TryGetValue(e.Key, out var f) ? f : null;
            if (first is null)
                foreach (var a in e.Aliases)
                    if (seen.TryGetValue(a, out f)) { first = f; break; }
            if (first is { } known) e.FirstSeen = known;
            else { e.FirstSeen = fresh.ScannedAt; added++; }
        }
        fresh.LastAdded = added;
        return (added, seen.Keys.Count(k => !now.Contains(k)));
    }

    /// <summary>
    /// For a scan in which a source could not be read: what was only known through an unread source stays in the
    /// catalogue. Otherwise it would count as gone now and as "new" again once the source is back.
    /// </summary>
    public static int KeepUnreadable(Catalog fresh, Catalog? previous)
    {
        if (previous is null) return 0;
        var read = fresh.Entries.SelectMany(e => e.Sources).ToHashSet();
        var have = fresh.Entries.Select(e => e.Key).ToHashSet();
        var kept = previous.Entries.Where(e => !have.Contains(e.Key) && e.Sources.Any(s => !read.Contains(s))).ToList();
        if (kept.Count == 0) return 0;
        fresh.Entries.AddRange(kept);
        fresh.Entries.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return kept.Count;
    }

    public static bool IsNew(ApiEntry e, DateTime? baseline, DateTime now) =>
        e.FirstSeen is { } first && baseline is { } b && first > b && now - first < TimeSpan.FromDays(NewForDays);

    public static List<ApiEntry> Merge(IEnumerable<ApiEntry> entries)
    {
        var byKey = new Dictionary<string, ApiEntry>(StringComparer.Ordinal);
        foreach (var e in entries)
        {
            if (e.Name.Length == 0 || !Http.IsWebUrl(e.Url)) continue; // ftp:, mailto: and relative links are no use as docs
            if (e.Key.Length == 0) e.Key = MakeKey(e.Url, e.Name);

            if (!byKey.TryGetValue(e.Key, out var have))
            {
                byKey[e.Key] = e;
                continue;
            }
            Absorb(have, e);
        }

        var list = MergeDuplicates(byKey.Values);
        foreach (var e in list) Categoriser.Apply(e);
        Categoriser.FoldSmall(list);
        list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return list;
    }

    /// <summary>What a second listing of the same API adds to the first: more sources, and facts the first did not have.</summary>
    private static void Absorb(ApiEntry have, ApiEntry e)
    {
        foreach (var s in e.Sources)
            if (!have.Sources.Contains(s)) have.Sources.Add(s);
        if (have.Auth == AuthKind.Unknown && e.Auth != AuthKind.Unknown) { have.Auth = e.Auth; have.AuthRaw = e.AuthRaw; }
        if (have.Description.Length < 12 && e.Description.Length > have.Description.Length) have.Description = e.Description;
        if (have.RawCategory.Length == 0) have.RawCategory = e.RawCategory;
        if (have.Cors is "" or "Unknown" && e.Cors is "Yes" or "No") have.Cors = e.Cors;
        if (have.Pricing.Length == 0) have.Pricing = e.Pricing;
        have.Https ??= e.Https;
        have.Health ??= e.Health;
        have.SpecUrl ??= e.SpecUrl;
    }

    private static readonly Regex NameEnding = new(@"\.(com|io|org|net|dev|co|app)$", RegexOptions.Compiled);
    private static readonly Regex NotWord = new(@"[^\p{L}\p{N}]+", RegexOptions.Compiled);
    private static readonly Regex TrailingApi = new(@"\s*apis?$", RegexOptions.Compiled);

    /// <summary>"1inch", "1inch API" and "1Forge.com" come out as "1inch" / "1forge": the name without case, punctuation, a web ending or a trailing "API".</summary>
    public static string SameNameKey(string name)
    {
        var n = NameEnding.Replace(name.Trim().ToLowerInvariant(), "", 1);
        n = NotWord.Replace(n, " ").Trim();
        if (n.StartsWith("the ")) n = n[4..];
        n = TrailingApi.Replace(n, "", 1);
        return n.Replace(" ", "");
    }

    /// <summary>Hosts that hold many unrelated APIs: two "Weather" projects on GitHub are not one API.</summary>
    private static readonly HashSet<string> SharedHosts = ["github.com", "gitlab.com", "bitbucket.org", "rapidapi.com", "sourceforge.net"];
    private static readonly HashSet<string> CountryLevels = ["co", "com", "org", "net", "gov", "ac", "edu"];

    /// <summary>The site a docs URL belongs to: "docs.1inch.io" and "1inch.io" are both "1inch.io". Empty for a shared host.</summary>
    public static string SiteOf(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return "";
        var parts = u.Host.ToLowerInvariant().Split('.');
        if (parts.Length < 2) return "";
        // two labels, or three when the second-last is a country-level "co"/"com"/"org" (api.example.co.uk)
        int n = parts.Length >= 3 && parts[^1].Length == 2 && CountryLevels.Contains(parts[^2]) ? 3 : 2;
        var site = string.Join('.', parts[^n..]);
        return SharedHosts.Contains(site) ? "" : site;
    }

    /// <summary>
    /// One API listed twice under two docs URLs ("1inch" at 1inch.io/api and docs.1inch.io; "1Forge" and "1Forge.com"):
    /// entries with the same name are folded together when their docs are on the same site, or when they say the same
    /// thing about themselves. The first listing stays, the other's key is kept as an alias so what the user saved under
    /// it is not lost. Order is kept. The same rule as the phone app's mergeDuplicates.
    /// </summary>
    public static List<ApiEntry> MergeDuplicates(IEnumerable<ApiEntry> entries)
    {
        static string Said(ApiEntry e) => NotWord.Replace(e.Description.Trim().ToLowerInvariant(), " ").Trim();
        var byName = new Dictionary<string, List<ApiEntry>>(StringComparer.Ordinal);
        var kept = new List<ApiEntry>();
        foreach (var e in entries)
        {
            var name = SameNameKey(e.Name);
            var site = SiteOf(e.Url);
            var said = Said(e);
            ApiEntry? same = null;
            if (name.Length > 0 && byName.TryGetValue(name, out var candidates))
                foreach (var c in candidates)
                {
                    bool sameSite = site.Length > 0 && site == SiteOf(c.Url);
                    bool sameWords = said.Length >= 12 && said == Said(c);
                    if (sameSite || sameWords) { same = c; break; }
                }
            if (same is null)
            {
                if (!byName.TryGetValue(name, out var list)) byName[name] = list = [];
                list.Add(e);
                kept.Add(e);
                continue;
            }
            Absorb(same, e);
            // the one that was found twice keeps the longer description: the two listings often describe it differently
            if (e.Description.Length > same.Description.Length) same.Description = e.Description;
            foreach (var k in e.Aliases.Prepend(e.Key))
                if (k != same.Key && !same.Aliases.Contains(k)) same.Aliases.Add(k);
        }
        return kept;
    }

    /// <summary>Same docs URL = same API, ignoring scheme, www, tracking parameters and trailing slashes.</summary>
    public static string MakeKey(string url, string name)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return "name:" + name.ToLowerInvariant();
        var host = u.Host.ToLowerInvariant();
        if (host.StartsWith("www.")) host = host[4..];
        var path = u.AbsolutePath.TrimEnd('/').ToLowerInvariant();
        var key = host + path;
        // Marketplaces and code hosts hold many APIs under one host, and some docs sites hold several under one page
        if (path.Length == 0 || u.Fragment.Length > 1) key += "#" + (u.Fragment.Length > 1 ? u.Fragment[1..].ToLowerInvariant() : "");
        return key.TrimEnd('#');
    }
}
