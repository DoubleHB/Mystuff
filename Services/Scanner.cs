using ApiScout.Models;

namespace ApiScout.Services;

public sealed record ScanProgress(string Message, int Done, int Total);

public sealed record ScanOutcome(Catalog Catalog, List<string> Notes);

/// <summary>Runs every enabled source in parallel, then merges, de-duplicates and categorises.</summary>
public static class Scanner
{
    public static async Task<ScanOutcome> RunAsync(IReadOnlyList<(string Id, string Name)> sources, IProgress<ScanProgress> progress, CancellationToken ct)
    {
        int done = 0;
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
        return new(new Catalog { ScannedAt = DateTime.Now, Entries = merged }, notes);
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
            if (seen.TryGetValue(e.Key, out var first)) e.FirstSeen = first;
            else { e.FirstSeen = fresh.ScannedAt; added++; }
        }
        return (added, seen.Keys.Count(k => !now.Contains(k)));
    }

    public static bool IsNew(ApiEntry e, DateTime? baseline, DateTime now) =>
        e.FirstSeen is { } first && baseline is { } b && first > b && now - first < TimeSpan.FromDays(NewForDays);

    public static List<ApiEntry> Merge(IEnumerable<ApiEntry> entries)
    {
        var byKey = new Dictionary<string, ApiEntry>(StringComparer.Ordinal);
        foreach (var e in entries)
        {
            if (e.Name.Length == 0 || e.Url.Length == 0) continue;
            if (e.Key.Length == 0) e.Key = MakeKey(e.Url, e.Name);

            if (!byKey.TryGetValue(e.Key, out var have))
            {
                byKey[e.Key] = e;
                continue;
            }
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

        var list = byKey.Values.ToList();
        foreach (var e in list) Categoriser.Apply(e);
        Categoriser.FoldSmall(list);
        list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return list;
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
