using System.Text.RegularExpressions;
using ApiScout.Models;

namespace ApiScout.Services;

/// <summary>Reads "awesome list" style READMEs: a heading per category followed by a table (or bullets) of links.</summary>
public static partial class MarkdownListParser
{
    [GeneratedRegex(@"^\s{0,3}(#{1,5})\s+(.+?)\s*#*\s*$")]
    private static partial Regex HeadingRx();
    [GeneratedRegex(@"\[\*{0,2}\s*([^\]]+?)\s*\*{0,2}\]\(\s*<?(https?://[^)\s>]+)>?[^)]*\)")]
    private static partial Regex LinkRx();
    [GeneratedRegex(@"^\s*\|?\s*:?-{2,}:?\s*(\|\s*:?-{2,}:?\s*)*\|?\s*$")]
    private static partial Regex SeparatorRx();
    [GeneratedRegex(@"^\s*[-*+]\s+\[([^\]]+)\]\((https?://[^)\s]+)[^)]*\)\s*[-–—:]*\s*(.*)$")]
    private static partial Regex BulletRx();
    [GeneratedRegex(@"<[^>]+>|!\[[^\]]*\]\([^)]*\)")]
    private static partial Regex HtmlOrImageRx();
    [GeneratedRegex(@"\[([^\]]*)\]\([^)]*\)")]
    private static partial Regex InlineLinkRx();
    [GeneratedRegex(@"[^\p{L}\p{N}\s&/,+'.()-]")]
    private static partial Regex HeadingJunkRx();

    private static readonly string[] SkipHeadings =
        ["sponsor", "apilayer", "content", "index", "license", "contribut", "resource", "related", "about", "credit", "learn more", "table of",
         "unverified", "unreachable", "deprecated", "dead link", "graveyard"];

    /// <param name="strictTables">Only accept tables that have an Auth, HTTPS or CORS column - the mark of a real API list.</param>
    public static List<ApiEntry> Parse(string markdown, string sourceName, bool bullets = false, bool strictTables = false)
    {
        var list = new List<ApiEntry>();
        var lines = markdown.Replace("\r", "").Split('\n');
        string heading = "";
        bool skip = false;
        int[]? cols = null; // name, description, auth, https, cors, open/trial

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var h = HeadingRx().Match(line);
            if (h.Success)
            {
                heading = CleanHeading(h.Groups[2].Value);
                var lower = heading.ToLowerInvariant();
                skip = SkipHeadings.Any(lower.Contains);
                cols = null;
                continue;
            }
            if (skip) continue;

            if (line.Contains('|'))
            {
                // Header row is the one directly above a |---|---| separator
                if (i + 1 < lines.Length && SeparatorRx().IsMatch(lines[i + 1]) && !SeparatorRx().IsMatch(line))
                {
                    cols = MapColumns(SplitRow(line));
                    if (strictTables && cols[2] < 0 && cols[3] < 0 && cols[4] < 0) cols = null;
                    i++;
                    continue;
                }
                if (cols is null || SeparatorRx().IsMatch(line)) continue;

                var cells = SplitRow(line);
                if (cols[0] >= cells.Count) continue;
                var link = LinkRx().Match(cells[cols[0]]);
                if (!link.Success) continue;

                var entry = new ApiEntry
                {
                    Name = CleanText(link.Groups[1].Value),
                    Url = link.Groups[2].Value.Trim(),
                    Description = Cell(cells, cols[1]),
                    RawCategory = heading,
                    AuthRaw = Cell(cells, cols[2]),
                    Cors = NormaliseCors(Cell(cells, cols[4])),
                    Sources = [sourceName],
                };
                entry.Auth = cols[2] < 0 ? AuthKind.Unknown : ParseAuth(entry.AuthRaw);
                if (cols[5] >= 0 && cols[5] < cells.Count)
                {
                    // read the raw cell: the markers are an emoji and an image, which CleanText strips
                    var flag = cells[cols[5]];
                    entry.Pricing = flag.Contains("💸") || flag.Contains("paid", StringComparison.OrdinalIgnoreCase) ? "paid"
                        : flag.Contains("open source", StringComparison.OrdinalIgnoreCase) ? "open" : "";
                }
                var https = Cell(cells, cols[3]).ToLowerInvariant();
                entry.Https = https.StartsWith("yes") ? true : https.StartsWith("no") ? false : entry.Url.StartsWith("https") ? true : null;
                if (entry.Name.Length > 0) list.Add(entry);
            }
            else if (bullets && heading.Length > 0)
            {
                var b = BulletRx().Match(line);
                if (!b.Success) continue;
                list.Add(new ApiEntry
                {
                    Name = CleanText(b.Groups[1].Value),
                    Url = b.Groups[2].Value.Trim(),
                    Description = CleanText(b.Groups[3].Value),
                    RawCategory = heading,
                    Https = b.Groups[2].Value.StartsWith("https"),
                    Sources = [sourceName],
                });
            }
            else if (line.Trim().Length > 0 && !line.TrimStart().StartsWith('|'))
            {
                // leaving the table
                if (cols is not null && !line.Contains('|')) cols = null;
            }
        }
        return list;
    }

    public static AuthKind ParseAuth(string raw)
    {
        var a = raw.Trim().Trim('`').ToLowerInvariant();
        if (a.Length == 0 || a is "no" or "none" or "-" or "null") return AuthKind.None;
        if (a.Contains("oauth")) return AuthKind.OAuth;
        if (a.Contains("key") || a.Contains("token") || a.Contains("mashape")) return AuthKind.ApiKey;
        if (a is "unknown" or "?") return AuthKind.Unknown;
        return AuthKind.Other;
    }

    private static int[] MapColumns(List<string> header)
    {
        int[] cols = [0, -1, -1, -1, -1, -1];
        for (int c = 0; c < header.Count; c++)
        {
            var name = header[c].Trim().Trim('*').ToLowerInvariant();
            if (name is "api" or "name" or "title" || name.StartsWith("api ")) cols[0] = c;
            else if (name.StartsWith("desc")) cols[1] = c;
            else if (name.StartsWith("auth")) cols[2] = c;
            else if (name == "https") cols[3] = c;
            else if (name == "cors") cols[4] = c;
            else if (name.Contains("trial") || name.Contains("pricing")) cols[5] = c;
        }
        if (cols[1] < 0 && header.Count > 1) cols[1] = cols[0] == 0 ? 1 : 0;
        return cols;
    }

    private static List<string> SplitRow(string line)
    {
        var t = line.Trim();
        if (t.StartsWith('|')) t = t[1..];
        if (t.EndsWith('|')) t = t[..^1];
        return [.. t.Split('|').Select(s => s.Trim())];
    }

    private static string Cell(List<string> cells, int index) =>
        index >= 0 && index < cells.Count ? CleanText(cells[index]) : "";

    private static string NormaliseCors(string raw)
    {
        var c = raw.ToLowerInvariant();
        return c.StartsWith("yes") ? "Yes" : c.StartsWith("no") ? "No" : c.Length == 0 ? "" : "Unknown";
    }

    public static string CleanText(string s)
    {
        s = HtmlOrImageRx().Replace(s, "");
        s = InlineLinkRx().Replace(s, "$1");
        s = s.Replace("**", "").Replace("`", "").Replace("&amp;", "&");
        return Regex.Replace(s, @"\s+", " ").Trim();
    }

    private static string CleanHeading(string s)
    {
        s = CleanText(s);
        s = HeadingJunkRx().Replace(s, "");
        return Regex.Replace(s, @"\s+", " ").Trim();
    }
}
