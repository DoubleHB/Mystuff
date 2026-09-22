using System.Net;
using System.Text.RegularExpressions;
using ApiScout.Models;
using ApiScout.ViewModels;

namespace ApiScout.Services;

/// <summary>What the provider's own page says about an API: a summary, its feature list, its main sections.</summary>
public sealed class ApiInfo
{
    public string Source { get; set; } = "";
    public string Title { get; set; } = "";
    public string Summary { get; set; } = "";
    public List<string> Features { get; set; } = [];
    public List<string> Sections { get; set; } = [];
    public string? Error { get; set; }
    public bool IsThin => Summary.Length == 0 && Features.Count == 0;
}

/// <summary>
/// "More about this API". Two halves: <see cref="Benefits"/> is worked out from what ApiScout already knows (key, free
/// level, HTTPS, CORS, health, spec); <see cref="ReadAsync"/> reads the provider's docs page - or the README for an API
/// that lives on GitHub - and pulls out how it describes itself. Nothing is invented: what the page does not say is not shown.
/// </summary>
public static partial class ApiInsight
{
    private const int Ms = 2500;
    [GeneratedRegex(@"<(script|style|noscript|svg|nav|footer|header|form|aside)\b.*?</\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline, Ms)] private static partial Regex NoiseRx();
    [GeneratedRegex(@"<meta\b[^>]*?(?:name|property)\s*=\s*[""'](?:og:|twitter:)?description[""'][^>]*>", RegexOptions.IgnoreCase, Ms)] private static partial Regex MetaRx();
    [GeneratedRegex(@"content\s*=\s*(?:""([^""]*)""|'([^']*)')", RegexOptions.IgnoreCase, Ms)] private static partial Regex ContentRx();
    [GeneratedRegex(@"<title\b[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline, Ms)] private static partial Regex TitleRx();
    [GeneratedRegex(@"<h([1-3])\b[^>]*>(.*?)</h\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline, Ms)] private static partial Regex HeadingRx();
    [GeneratedRegex(@"<p\b[^>]*>(.*?)</p>", RegexOptions.IgnoreCase | RegexOptions.Singleline, Ms)] private static partial Regex ParagraphRx();
    [GeneratedRegex(@"<li\b[^>]*>(.*?)</li>", RegexOptions.IgnoreCase | RegexOptions.Singleline, Ms)] private static partial Regex ItemRx();
    [GeneratedRegex(@"<[^>]+>", RegexOptions.None, Ms)] private static partial Regex TagRx();
    [GeneratedRegex(@"(?i)\b(features?|what (you|it) can|capabilit|highlights|why |benefits|overview|what is|about|use cases|endpoints|resources|available data)\b")] private static partial Regex FeatureHeadingRx();
    [GeneratedRegex(@"(?i)^(home|login|log in|sign ?up|sign in|pricing|blog|contact|docs?|documentation|menu|search|privacy|terms|cookies?|table of contents|contents|navigation|license|contributing|installation|install|changelog|support|faq|footer|share|follow us)\b")] private static partial Regex NavWordsRx();
    [GeneratedRegex(@"^https?://github\.com/([^/\s]+)/([^/#?\s]+)", RegexOptions.IgnoreCase)] private static partial Regex GitHubRx();

    /// <summary>Plain facts turned into "what is in it for you" lines.</summary>
    public static List<string> Benefits(ApiRow r)
    {
        var list = new List<string>();
        if (r.HasDemoKey) list.Add($"Try it right now: the provider publishes a demo key ({r.DemoKey}) - no sign-up to see real data.");
        else if (r.Entry.Auth == AuthKind.None) list.Add("No key and no sign-up: you can call it straight away, from code or the browser.");
        else if (r.KeylessWorks) list.Add("Works without a key; a free key only lifts the limits.");
        else if (r.Entry.Auth == AuthKind.ApiKey) list.Add(r.HasSignupUrl ? "Needs a free API key - the sign-up link is on the Keys card." : "Needs an API key - 'Scan docs for key info' looks for the sign-up page.");
        else if (r.Entry.Auth == AuthKind.OAuth) list.Add("Uses OAuth: more set-up (an app registration), but it can act on behalf of your users.");

        list.Add(r.Access switch
        {
            AccessLevel.FullFree => "Completely free: everything it offers, not just a sample.",
            AccessLevel.FreeTier => "Free tier: free to keep using within limits; paid plans only matter if you outgrow them.",
            AccessLevel.TrialOnly => "Careful: only a demo or trial is free - real use needs a paid plan.",
            _ => "What is free is not stated by the directories - 'Scan docs for key info' can usually find out.",
        });
        if (r.Entry.Https == true) list.Add("HTTPS: requests and any key you send are encrypted in transit.");
        else if (r.Entry.Https == false) list.Add("No HTTPS: fine for public data, but do not send a secret key over it.");
        if (r.Cors == "Yes") list.Add("CORS enabled: callable directly from JavaScript in a web page, no proxy server needed.");
        else if (r.Cors == "No") list.Add("No CORS: call it from a server or desktop app - a browser page would need a proxy.");
        if (r.Entry.Health is { } h) list.Add(h >= 90 ? $"Reliable: {h}% in freepublicapis.com's daily health tests." : $"Health {h}% in freepublicapis.com's daily tests - check it answers before building on it.");
        if (r.HasSpec) list.Add("Has an OpenAPI spec: client code and request collections can be generated from it.");
        if (r.HasExample) list.Add("ApiScout knows a request that works as it is - press Test this API.");
        if (r.Entry.Sources.Count >= 3) list.Add($"Well known: listed by {r.Entry.Sources.Count} independent directories.");
        if (r.IsNew) list.Add("New: first seen by a scan in the last two weeks.");
        return list;
    }

    public static async Task<ApiInfo> ReadAsync(ApiEntry api, CancellationToken ct)
    {
        var info = new ApiInfo { Source = api.Url };
        if (!Http.IsPublicWebUrl(api.Url)) { info.Error = "The docs link is not a public web address."; return info; }
        try
        {
            // an API that lives on GitHub describes itself in its README
            if (GitHubRx().Match(api.Url) is { Success: true } gh && gh.Groups[1].Value is not ("orgs" or "topics" or "features"))
            {
                try
                {
                    info.Source = $"https://raw.githubusercontent.com/{gh.Groups[1].Value}/{gh.Groups[2].Value}/HEAD/README.md";
                    ReadMarkdown(await Http.GetTextAsync(info.Source, ct, maxBytes: 1_500_000, timeoutSeconds: 20), info);
                    if (!info.IsThin) return info;
                }
                catch (HttpRequestException) { }
                info.Source = api.Url;
            }
            ReadHtml(await Http.GetTextAsync(api.Url, ct, maxBytes: 2_500_000, timeoutSeconds: 20), info);
            if (info.IsThin) info.Error = "The page says little in plain HTML - it probably builds itself with JavaScript. Open it in the browser.";
        }
        catch (HttpRequestException ex) { info.Error = ex.StatusCode is { } code ? $"The page answered {(int)code} {code} - it may block automated readers. Open it in the browser." : $"Could not reach the page: {ex.Message}"; }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { info.Error = "The page took too long to answer."; }
        catch (RegexMatchTimeoutException) { info.Error = "The page is too tangled to read automatically. Open it in the browser."; }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException) { info.Error = $"Could not read the page: {ex.Message}"; }
        return info;
    }

    internal static void ReadHtml(string html, ApiInfo info)
    {
        info.Title = Text(TitleRx().Match(html).Groups[1].Value);
        var meta = MetaRx().Matches(html).Select(m => ContentRx().Match(m.Value)).Where(c => c.Success)
            .Select(c => Text(c.Groups[1].Success ? c.Groups[1].Value : c.Groups[2].Value)).Where(t => t.Length >= 30).OrderByDescending(t => t.Length).FirstOrDefault() ?? "";
        var body = NoiseRx().Replace(html, " ");

        var paragraphs = ParagraphRx().Matches(body).Select(m => Text(m.Groups[1].Value)).Where(t => t.Length is >= 60 and <= 700 && t.Contains(' ') && !LooksLikeLegal(t)).Distinct().Take(3).ToList();
        info.Summary = string.Join("\n\n", new[] { meta }.Concat(paragraphs.Where(p => !Same(p, meta))).Where(s => s.Length > 0).Take(3));

        var headings = HeadingRx().Matches(body).Select(m => (m.Index, Level: m.Groups[1].Value, Text: Text(m.Groups[2].Value))).Where(h => h.Text.Length is >= 3 and <= 70 && !NavWordsRx().IsMatch(h.Text)).ToList();
        info.Sections = [.. headings.Select(h => h.Text).Distinct(StringComparer.OrdinalIgnoreCase).Take(12)];

        // list items under a "Features"-like heading first, otherwise the first decent list items on the page
        var items = ItemRx().Matches(body).Select(m => (m.Index, Text: Text(m.Groups[1].Value))).Where(i => IsFeature(i.Text)).ToList();
        var under = headings.Where(h => FeatureHeadingRx().IsMatch(h.Text)).SelectMany(h => items.Where(i => i.Index > h.Index && i.Index < h.Index + 6000)).Select(i => i.Text);
        info.Features = [.. under.Concat(items.Select(i => i.Text)).Distinct(StringComparer.OrdinalIgnoreCase).Take(10)];
    }

    internal static void ReadMarkdown(string md, ApiInfo info)
    {
        md = Regex.Replace(md, @"```.*?```", " ", RegexOptions.Singleline, TimeSpan.FromMilliseconds(Ms));
        var lines = md.Replace("\r", "").Split('\n');
        string Clean(string s) => Text(Regex.Replace(Regex.Replace(s, @"!\[[^\]]*\]\([^)]*\)", ""), @"\[([^\]]+)\]\([^)]*\)", "$1").Replace("**", "").Replace("`", "").Replace("__", ""));

        info.Title = Clean(lines.FirstOrDefault(l => l.StartsWith("# ")) ?? "").TrimStart('#', ' ');
        info.Sections = [.. lines.Where(l => l.StartsWith("## ") || l.StartsWith("### ")).Select(l => Clean(l.TrimStart('#', ' '))).Where(t => t.Length is >= 3 and <= 70 && !NavWordsRx().IsMatch(t)).Distinct().Take(12)];

        var paragraphs = new List<string>();
        var current = new List<string>();
        foreach (var line in lines.Append(""))
        {
            var t = line.Trim();
            bool prose = t.Length > 0 && !t.StartsWith('#') && !t.StartsWith('|') && !t.StartsWith('<') && !t.StartsWith("[!") && !t.StartsWith("- ") && !t.StartsWith("* ") && !t.StartsWith('>') && !Regex.IsMatch(t, @"^\d+\.\s");
            if (prose) { current.Add(t); continue; }
            if (current.Count > 0 && Clean(string.Join(' ', current)) is { Length: >= 60 and <= 900 } p && !LooksLikeLegal(p)) paragraphs.Add(p);
            current.Clear();
        }
        info.Summary = string.Join("\n\n", paragraphs.Take(3));

        var bullets = lines.Select((l, i) => (i, l.Trim())).Where(x => x.Item2.StartsWith("- ") || x.Item2.StartsWith("* ")).Select(x => (x.i, Text: Clean(x.Item2[2..]))).Where(b => IsFeature(b.Text)).ToList();
        var featureLines = lines.Select((l, i) => (i, l)).Where(x => x.l.StartsWith('#') && FeatureHeadingRx().IsMatch(x.l)).Select(x => x.i).ToList();
        var under = featureLines.SelectMany(h => bullets.Where(b => b.i > h && b.i < h + 40)).Select(b => b.Text);
        info.Features = [.. under.Concat(bullets.Select(b => b.Text)).Distinct(StringComparer.OrdinalIgnoreCase).Take(10)];
    }

    private static bool IsFeature(string t) => t.Length is >= 18 and <= 220 && t.Count(c => c == ' ') >= 2 && !NavWordsRx().IsMatch(t) && !LooksLikeLegal(t);
    private static bool LooksLikeLegal(string t) => Regex.IsMatch(t, @"(?i)cookie|all rights reserved|privacy policy|terms of (use|service)|©|subscribe to our|javascript (is|must be) (disabled|enabled)");
    private static bool Same(string a, string b) => a.Length > 0 && b.Length > 0 && (a.StartsWith(b[..Math.Min(40, b.Length)], StringComparison.OrdinalIgnoreCase) || b.StartsWith(a[..Math.Min(40, a.Length)], StringComparison.OrdinalIgnoreCase));
    private static string Text(string html) => Regex.Replace(WebUtility.HtmlDecode(TagRx().Replace(html, " ")), @"\s+", " ").Trim();

    public static string ToMarkdown(ApiRow r, ApiInfo? info)
    {
        var sb = new System.Text.StringBuilder($"## {r.Name}\n\n{r.Description}\n\n[{r.Url}]({r.Url})\n\n### At a glance\n\n");
        foreach (var b in Benefits(r)) sb.Append("- ").Append(b).Append('\n');
        if (info is { IsThin: false })
        {
            if (info.Summary.Length > 0) sb.Append("\n### In the provider's words\n\n").Append(info.Summary).Append('\n');
            if (info.Features.Count > 0) { sb.Append("\n### Features\n\n"); foreach (var f in info.Features) sb.Append("- ").Append(f).Append('\n'); }
            if (info.Sections.Count > 0) sb.Append("\n### The docs cover\n\n").Append(string.Join(" · ", info.Sections)).Append('\n');
            sb.Append($"\n_Read from {info.Source}_\n");
        }
        return sb.ToString();
    }
}
