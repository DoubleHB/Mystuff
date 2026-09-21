using System.Text;
using ApiScout.Models;
using ApiScout.ViewModels;

namespace ApiScout.Services;

/// <summary>Compares a fresh scan with the catalogue before it: new APIs, APIs that are gone, and APIs whose auth or free-access level changed.</summary>
public static class ChangeLog
{
    private const int MaxPerList = 3000;

    /// <param name="docsScans">Docs scans belong to the API, not to a scan, so both sides are judged with the same one.</param>
    public static ScanReport Build(Catalog fresh, Catalog previous, IReadOnlyDictionary<string, DocsScanResult> docsScans, string trigger, int failedSources = 0)
    {
        var report = new ScanReport { At = fresh.ScannedAt, ComparedWith = previous.ScannedAt, Trigger = trigger, Total = fresh.Entries.Count, FailedSources = failedSources };
        var before = new Dictionary<string, ApiEntry>();
        foreach (var e in previous.Entries) before.TryAdd(e.Key, e);
        var now = new HashSet<string>();

        foreach (var e in fresh.Entries)
        {
            now.Add(e.Key);
            if (!before.TryGetValue(e.Key, out var old)) { report.Added.Add(Line(e)); continue; }
            // the access level is worked out from these three (plus things a scan cannot change), so most entries need no second look
            if (old.Auth == e.Auth && old.Pricing == e.Pricing && old.Description == e.Description) continue;

            var scan = docsScans.GetValueOrDefault(e.Key);
            ApiRow was = new(old) { DocsScan = scan }, isNow = new(e) { DocsScan = scan };
            var what = new List<string>();
            if (was.AuthLabel != isNow.AuthLabel) what.Add($"Auth: {was.AuthLabel} → {isNow.AuthLabel}");
            if (was.AccessLabel != isNow.AccessLabel) what.Add($"Free access: {was.AccessLabel} → {isNow.AccessLabel}");
            if (what.Count > 0) { var line = Line(e); line.What = string.Join("  ·  ", what); report.Changed.Add(line); }
        }
        report.Removed.AddRange(previous.Entries.Where(e => !now.Contains(e.Key)).Select(Line));

        foreach (var list in new[] { report.Added, report.Removed, report.Changed })
            if (list.Count > MaxPerList) list.RemoveRange(MaxPerList, list.Count - MaxPerList);
        return report;
    }

    private static ScanChange Line(ApiEntry e) => new() { Key = e.Key, Name = e.Name, Url = e.Url, Category = e.Category };

    public static string ToMarkdown(ScanReport r)
    {
        static string Cell(string s) => s.Replace("|", "\\|").Replace("\r", "").Replace('\n', ' ');
        var sb = new StringBuilder($"## ApiScout: what the scan of {r.At:d MMM yyyy HH:mm} changed\n\n");
        sb.Append($"{r.Total:N0} APIs, compared with the scan of {r.ComparedWith:d MMM yyyy HH:mm}: {r.Added.Count:N0} new, {r.Removed.Count:N0} gone, {r.Changed.Count:N0} changed.");
        if (r.FailedSources > 0) sb.Append($" {r.FailedSources} source(s) could not be read - what only they knew was kept, not counted as gone.");
        sb.Append('\n');
        void Section(string title, List<ScanChange> list, bool what)
        {
            if (list.Count == 0) return;
            sb.Append($"\n### {title} ({list.Count:N0})\n\n");
            foreach (var c in list) sb.Append($"- [{Cell(c.Name)}]({c.Url}) - {(what ? Cell(c.What) : Cell(c.Category))}\n");
        }
        Section("New", r.Added, false);
        Section("Gone", r.Removed, false);
        Section("Changed", r.Changed, true);
        return sb.ToString();
    }
}
