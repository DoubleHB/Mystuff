using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using ApiScout.Models;

namespace ApiScout.Services;

/// <summary>
/// Reads one API's public docs page (and its OpenAPI spec when known) looking for: the auth scheme,
/// sign-up / get-a-key links, free-tier and rate-limit sentences, and sample keys printed in the docs.
/// It only ever reads the provider's own published pages.
/// </summary>
public static partial class DocsScanner
{
    [GeneratedRegex(@"<a\b[^>]*?href\s*=\s*[""']([^""'#][^""']*)[""'][^>]*>(.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex AnchorRx();
    [GeneratedRegex(@"<(script|style|noscript|svg)\b.*?</\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex NoiseRx();
    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagRx();
    [GeneratedRegex(@"(?i)\b(api[_-]?key|apikey|app[_-]?id|appid|app[_-]?key|access[_-]?key|access[_-]?token|api[_-]?token|auth[_-]?token|wskey|token|key)\s*=\s*([A-Za-z0-9_\-\.]{1,80})")]
    private static partial Regex KeyParamRx();
    [GeneratedRegex(@"(?i)\b(x-[a-z0-9-]*(?:key|token)[a-z0-9-]*|authorization)\s*:\s*([^\s""'<]{1,90}(?:\s[^\s""'<]{4,90})?)")]
    private static partial Regex HeaderRx();
    [GeneratedRegex(@"(?i)sign\s?-?up|register|get\s+(an?\s+|your\s+)?(free\s+)?(api\s+)?(key|token|access)|api[\s_-]?keys?|dashboard|developer\s+portal|pricing|get\s+started|create\s+(an?\s+)?(account|app)|request\s+(a\s+)?key")]
    private static partial Regex SignupRx();
    [GeneratedRegex(@"(?i)free\s+(tier|plan|forever|account|api|key|to\s+use|for)|no\s+(api\s+)?(key|auth\w*|sign\s?-?up|registration)\s+(is\s+)?(required|needed|necessary)|without\s+(an?\s+)?(api\s+)?key|(requests?|calls?|queries|lookups)\s*(per|/|a|each)\s*(second|minute|hour|day|month)|rate\s?-?limit|demo\s+key|test\s+key|sandbox|free\s+trial|\d+\s*-?\s*day\s+trial")]
    private static partial Regex FreeTierRx();
    [GeneratedRegex(@"(?i)^(your|my|the)?[_\-\.]*(api)?[_\-\.]*(key|token|id|secret|apikey|appid|access|value|here|xxx+|x+|\.\.\.|key_here|token_here|string|text|true|false|null|undefined)+[_\-\.0-9]*$")]
    private static partial Regex PlaceholderRx();
    [GeneratedRegex(@"https?://[^\s""'<>\)\]\\|`]+")]
    private static partial Regex UrlRx();
    [GeneratedRegex(@"(?<![\w/.@:-])(?:[a-z0-9-]+\.)+[a-z]{2,}/[^\s""'<>\)\]\\|`]+", RegexOptions.IgnoreCase)]
    private static partial Regex BareUrlRx();
    [GeneratedRegex(@"\b(GET|POST|PUT|PATCH|DELETE)\s+(/[A-Za-z0-9_\-/{}.:~%]+(?:\?[^\s""'<>|`]*)?)")]
    private static partial Regex MethodPathRx();
    [GeneratedRegex(@"(?i)\.(js|css|png|jpe?g|gif|svg|ico|woff2?|ttf|map|pdf|zip|mp4|webp)(\?|$)")]
    private static partial Regex AssetRx();
    [GeneratedRegex(@"(?i)(^|\.)(github\.com|githubusercontent\.com|twitter\.com|x\.com|facebook\.com|linkedin\.com|youtube\.com|youtu\.be|instagram\.com|medium\.com|discord\.(gg|com)|slack\.com|stackoverflow\.com|npmjs\.com|pypi\.org|nuget\.org|w3\.org|schema\.org|gravatar\.com|shields\.io|google-analytics\.com|googletagmanager\.com|gstatic\.com|cloudflare\.com|jsdelivr\.net|unpkg\.com|cdnjs\.com|apple\.com|play\.google\.com|wikipedia\.org|example\.(com|org))$|^(cdn|fonts|static|assets|img|images)\.")]
    private static partial Regex NotApiHostRx();
    [GeneratedRegex(@"(?i)^(api[_-]?key|apikey|app[_-]?id|appid|app[_-]?key|access[_-]?key|access[_-]?token|api[_-]?token|auth[_-]?token|wskey|token|key)$")]
    private static partial Regex KeyParamNameRx();
    [GeneratedRegex(@"(?i)\bpricing\b|\bplans?\b|\bprices\b")]
    private static partial Regex PricingLinkRx();
    [GeneratedRegex(@"(?i)free\s+(plan|tier|forever|account|version|usage|quota|of\s+charge)|(\$|£|€)\s?0(\.00)?\b|\b0\s?(\$|£|€)|no\s+credit\s+card|free\b[^.|]{0,70}\b(requests?|calls?|credits?|queries|lookups)|\b(requests?|calls?|credits?|queries|lookups)\b[^.|]{0,40}\bfree\b|free\s+trial|\d+\s*-?\s*day\s+trial|always\s+free|free\s+for\s+(personal|non-?commercial|open\s?source|developers)")]
    private static partial Regex PricingRx();

    public static async Task<DocsScanResult> ScanAsync(ApiEntry api, CancellationToken ct)
    {
        var result = new DocsScanResult { ScannedAt = DateTime.Now, PageUrl = api.Url };
        string? pricingUrl = null;
        if (!string.IsNullOrEmpty(api.SpecUrl))
        {
            try { ReadSpec(await Http.GetTextAsync(api.SpecUrl, ct, maxBytes: 12_000_000, timeoutSeconds: 40), result); }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested) { }
        }

        try
        {
            var html = await Http.GetTextAsync(api.Url, ct, maxBytes: 3_000_000, timeoutSeconds: 25);
            ReadPage(html, api.Url, result);
            pricingUrl = FindPricingLink(html, api.Url);
        }
        catch (HttpRequestException ex)
        {
            result.Error = ex.StatusCode is { } code
                ? $"The docs page answered {(int)code} {code} - it may block automated readers. Open it in the browser instead."
                : $"Could not reach the docs page: {ex.Message}";
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            result.Error = "The docs page took too long to answer.";
        }

        if (pricingUrl is not null)
        {
            try { ReadPricing(await Http.GetTextAsync(pricingUrl, ct, maxBytes: 2_000_000, timeoutSeconds: 20), pricingUrl, result); }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested) { }
        }

        if (result.Items.Count == 0 && result.Error is null)
            result.Items.Add(new FoundItem { Kind = "Note", Value = "Nothing key-related found on this page", Note = "The page may build itself with JavaScript, or the key details live on another page. Open the docs in the browser." });
        return result;
    }

    internal static void ReadPage(string html, string pageUrl, DocsScanResult result)
    {
        Uri.TryCreate(pageUrl, UriKind.Absolute, out var baseUri);

        // 1. sign-up style links
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in AnchorRx().Matches(html))
        {
            var href = WebUtility.HtmlDecode(m.Groups[1].Value.Trim());
            var text = Clean(m.Groups[2].Value);
            if (href.StartsWith("javascript", StringComparison.OrdinalIgnoreCase) || href.StartsWith("mailto", StringComparison.OrdinalIgnoreCase)) continue;
            if (!SignupRx().IsMatch(text) && !SignupRx().IsMatch(href.Replace('-', ' ').Replace('_', ' ').Replace('/', ' '))) continue;
            if (baseUri is null || !Uri.TryCreate(baseUri, href, out var abs) || abs.Scheme is not ("http" or "https")) continue;
            if (!seen.Add(abs.GetLeftPart(UriPartial.Path))) continue;
            result.Items.Add(new FoundItem { Kind = "Sign-up link", Value = abs.ToString(), Note = text.Length is > 0 and < 80 ? text : "" });
            if (seen.Count >= 8) break;
        }

        var body = WebUtility.HtmlDecode(TagRx().Replace(NoiseRx().Replace(html, " "), " "));
        body = Regex.Replace(body, @"\s+", " ");

        // 2. values shown next to key parameters in the docs' own examples
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in KeyParamRx().Matches(body))
            AddKey(result, keys, m.Groups[2].Value.TrimEnd('.'), $"Shown in the docs as {m.Groups[1].Value}=…");
        foreach (Match m in HeaderRx().Matches(body))
            AddKey(result, keys, m.Groups[2].Value.Trim(), $"Shown in the docs as header {m.Groups[1].Value}");

        // 3. example endpoints, ready to try
        AddEndpoints(WebUtility.HtmlDecode(html), body, baseUri, result);

        // 4. free tier / rate limit sentences
        int notes = 0;
        var said = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in FreeTierRx().Matches(body))
        {
            var sentence = SentenceAround(body, m.Index, m.Length);
            if (sentence.Length < 25 || !said.Add(sentence[..Math.Min(60, sentence.Length)])) continue;
            result.Items.Add(new FoundItem { Kind = "Free tier / limits", Value = sentence });
            if (++notes >= 6) break;
        }
    }

    /// <summary>
    /// URLs in the docs that look like API calls (api. host, /api/ or /v1/ path, a query string, .json…) and
    /// "GET /path" lines, turned into requests the Try it card can send. Placeholder keys become {key}.
    /// </summary>
    private static void AddEndpoints(string html, string body, Uri? baseUri, DocsScanResult result)
    {
        if (baseUri is null) return;
        var site = SiteOf(baseUri.Host);
        var best = new Dictionary<string, (int Score, string Method, int Order)>(StringComparer.OrdinalIgnoreCase);
        int order = 0;
        // the stripped text catches code samples; the raw html catches href="…" links to live examples
        // …and some docs print URLs without the scheme ("www.foo.com/api/x.php?s=1") - accepted on the docs' own site only
        var sources = new (string Text, Regex Rx, bool Bare)[] { (body, UrlRx(), false), (html, UrlRx(), false), (body, BareUrlRx(), true) };
        foreach (var (text, rx, bare) in sources)
            foreach (Match m in rx.Matches(text))
            {
                var raw = (bare ? baseUri.Scheme + "://" : "") + m.Value.TrimEnd('.', ',', ';', ':', '!', '?', ')', '*', '&', '"');
                if (!Uri.TryCreate(raw, UriKind.Absolute, out var u) || (bare && SiteOf(u.Host) != site) || (NotApiHostRx().IsMatch(u.Host) && !u.Host.StartsWith("api.", StringComparison.OrdinalIgnoreCase)) || AssetRx().IsMatch(u.AbsolutePath)) continue;
                bool sameSite = SiteOf(u.Host) == site;
                int score = (u.Host.StartsWith("api.", StringComparison.OrdinalIgnoreCase) || u.Host.Contains(".api.", StringComparison.OrdinalIgnoreCase) ? 3 : 0)
                          + (Regex.IsMatch(u.AbsolutePath, @"(?i)/api(/|$)|/v\d+(\.\d+)?(/|$)|/rest(/|$)|/json(/|$)") ? 2 : 0)
                          + (u.Query.Length > 1 ? 2 : 0)
                          + (Regex.IsMatch(u.AbsolutePath, @"(?i)\.(json|xml|php)$") ? 2 : 0)
                          + (sameSite ? 1 : 0);
                if (u.AbsolutePath.Length <= 1 && u.Query.Length <= 1) continue;
                if (u.Query.Length <= 1 && Regex.IsMatch(u.AbsolutePath, @"(?i)/(docs?|documentation|guides?|blog|pricing|sign-?up|log-?in|register|terms|privacy|support|contact|about|faq|dashboard|account|reference)(/|$|\.)")) continue;
                if (score < 3 || !(sameSite || score >= 5)) continue;
                if (u.GetLeftPart(UriPartial.Path).TrimEnd('/') == baseUri.GetLeftPart(UriPartial.Path).TrimEnd('/') && u.Query.Length <= 1) continue;

                var url = KeyPlaceholders(u);
                // "GET https://…" in the docs tells us the method
                var before = text[Math.Max(0, m.Index - 12)..m.Index];
                var method = Regex.Match(before, @"\b(GET|POST|PUT|PATCH|DELETE)\s*$").Groups[1].Value;
                if (!best.TryGetValue(url, out var have) || have.Score < score) best[url] = (score, method.Length > 0 ? method : have.Method ?? "", have.Order > 0 ? have.Order : ++order);
            }

        var picked = best.OrderByDescending(p => p.Value.Score).ThenBy(p => p.Value.Order).Take(6).ToList();
        foreach (var (url, info) in picked)
            result.Items.Add(new FoundItem { Kind = "Example endpoint", Value = url, Method = info.Method.Length > 0 ? info.Method : "GET", Note = $"{(info.Method.Length > 0 ? info.Method : "GET")} - from the docs. Press Test to send it." });

        // "GET /v1/things" lines: join them to the origin of the best full example, or to a guessed api. host
        string? origin = picked.Count > 0 && Uri.TryCreate(picked[0].Key.Replace("{key}", "k"), UriKind.Absolute, out var first) ? first.GetLeftPart(UriPartial.Authority) : null;
        bool guessed = origin is null;
        origin ??= $"{baseUri.Scheme}://{(baseUri.Host.StartsWith("api.", StringComparison.OrdinalIgnoreCase) ? baseUri.Host : "api." + site)}";
        int paths = 0;
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in MethodPathRx().Matches(body))
        {
            var path = m.Groups[2].Value.TrimEnd('.', ',', ';', ':');
            if (path.Length < 3 || !path.Any(char.IsLetter) || path.StartsWith("//") || !seenPaths.Add(m.Groups[1].Value + path)) continue;
            if (picked.Any(p => p.Key.Contains(path, StringComparison.OrdinalIgnoreCase))) continue;
            result.Items.Add(new FoundItem
            {
                Kind = "Example endpoint", Value = origin + path, Method = m.Groups[1].Value,
                Note = $"{m.Groups[1].Value} - path from the docs" + (guessed ? "; the api. host is a guess - check it" : "") + (path.Contains('{') ? "; fill in the {…} parts" : ""),
            });
            if (++paths >= 4) break;
        }
    }

    /// <summary>?apikey=YOUR_KEY (or an empty / bracketed value) becomes ?apikey={key} so the saved key can be dropped in.</summary>
    internal static string KeyPlaceholders(Uri u)
    {
        if (u.Query.Length <= 1) return u.GetLeftPart(UriPartial.Query);
        var parts = u.Query[1..].Split('&').Select(p =>
        {
            var kv = p.Split('=', 2);
            if (kv.Length == 2 && KeyParamNameRx().IsMatch(kv[0]))
            {
                var v = Uri.UnescapeDataString(kv[1]);
                if (v.Length == 0 || PlaceholderRx().IsMatch(v) || v.Contains("your", StringComparison.OrdinalIgnoreCase) || v.Contains("xxx", StringComparison.OrdinalIgnoreCase) || v[0] is '{' or '[' or '<' or '$')
                    return kv[0] + "={key}";
            }
            return p;
        });
        return u.GetLeftPart(UriPartial.Path) + "?" + string.Join('&', parts);
    }

    /// <summary>A "Pricing" / "Plans" link on the docs page that stays on the provider's own site.</summary>
    internal static string? FindPricingLink(string html, string pageUrl)
    {
        if (!Uri.TryCreate(pageUrl, UriKind.Absolute, out var baseUri)) return null;
        var site = SiteOf(baseUri.Host);
        foreach (Match m in AnchorRx().Matches(html))
        {
            var href = WebUtility.HtmlDecode(m.Groups[1].Value.Trim());
            var text = Clean(m.Groups[2].Value);
            if (text.Length > 40 || !(PricingLinkRx().IsMatch(text) || PricingLinkRx().IsMatch(href.Replace('-', ' ').Replace('/', ' ').Replace('_', ' ')))) continue;
            if (!Uri.TryCreate(baseUri, href, out var abs) || abs.Scheme is not ("http" or "https") || SiteOf(abs.Host) != site) continue;
            if (abs.GetLeftPart(UriPartial.Path).TrimEnd('/') == baseUri.GetLeftPart(UriPartial.Path).TrimEnd('/')) continue;
            return abs.GetLeftPart(UriPartial.Query);
        }
        return null;
    }

    /// <summary>What the pricing page says about free use.</summary>
    internal static void ReadPricing(string html, string pricingUrl, DocsScanResult result)
    {
        result.Items.Add(new FoundItem { Kind = "Pricing page", Value = pricingUrl });
        var body = WebUtility.HtmlDecode(TagRx().Replace(NoiseRx().Replace(html, " | "), " | "));
        body = Regex.Replace(body, @"\s+", " ");
        int found = 0;
        var said = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in PricingRx().Matches(body))
        {
            var sentence = Regex.Replace(SentenceAround(body, m.Index, m.Length).Trim('|', ' '), @"(\s*\|\s*)+", " · ");
            if (sentence.Length < 12 || !said.Add(sentence[..Math.Min(50, sentence.Length)])) continue;
            result.Items.Add(new FoundItem { Kind = "Pricing", Value = sentence, Note = "From the pricing page" });
            if (++found >= 6) break;
        }
        if (found == 0)
            result.Items.Add(new FoundItem { Kind = "Pricing", Value = "No free plan is mentioned on the pricing page", Note = "Only paid plans were found - or the page builds itself with JavaScript" });
    }

    // "api.foo.co.uk" and "www.foo.co.uk" are the same site; good enough without a public-suffix list
    private static string SiteOf(string host)
    {
        var parts = host.ToLowerInvariant().Split('.');
        int keep = parts.Length >= 3 && parts[^2].Length <= 3 && parts[^1].Length == 2 ? 3 : 2;
        return string.Join('.', parts.Skip(Math.Max(0, parts.Length - keep)));
    }

    private static void AddKey(DocsScanResult result, HashSet<string> keys, string value, string note)
    {
        if (keys.Count >= 8 || value.Length == 0 || !keys.Add(value)) return;
        bool placeholder = value.Length < 3 && !value.All(char.IsDigit)
            || PlaceholderRx().IsMatch(value)
            || value.Contains("your", StringComparison.OrdinalIgnoreCase)
            || value.Contains("xxx", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith('{') || value.StartsWith('<') || value.StartsWith('$') || value.StartsWith('[');
        result.Items.Add(placeholder
            ? new FoundItem { Kind = "Placeholder", Value = value, Note = note + " - a stand-in; swap in your own key" }
            : new FoundItem { Kind = "Sample key", Value = value, Note = note + " - may be a working demo key or just an example" });
    }

    /// <summary>OpenAPI 2 securityDefinitions / OpenAPI 3 components.securitySchemes.</summary>
    internal static void ReadSpec(string json, DocsScanResult result)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        AddSpecEndpoints(root, result);
        JsonElement schemes;
        if (!(root.TryGetProperty("securityDefinitions", out schemes)
              || root.TryGetProperty("components", out var comp) && comp.TryGetProperty("securitySchemes", out schemes))
            || schemes.ValueKind != JsonValueKind.Object)
        {
            result.Items.Add(new FoundItem { Kind = "Auth scheme", Value = "None declared", Note = "The OpenAPI spec defines no security scheme - the API may be open" });
            return;
        }
        foreach (var s in schemes.EnumerateObject())
        {
            string Get(string n) => s.Value.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString()! : "";
            var type = Get("type");
            var value = type.ToLowerInvariant() switch
            {
                "apikey" => $"API key in {Get("in")}: {Get("name")}",
                "oauth2" => "OAuth 2" + (Get("authorizationUrl") is { Length: > 0 } a ? $" - authorise at {a}" : ""),
                "http" => $"HTTP {Get("scheme")} auth",
                "basic" => "HTTP basic auth",
                _ => type,
            };
            result.Items.Add(new FoundItem { Kind = "Auth scheme", Value = value, Note = $"From the OpenAPI spec ('{s.Name}')" });
        }
    }

    /// <summary>Base URL (OpenAPI 3 servers / OpenAPI 2 host + basePath) joined to the first few GET paths without parameters.</summary>
    private static void AddSpecEndpoints(JsonElement root, DocsScanResult result)
    {
        static string S(JsonElement e, string n) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString()! : "";
        string baseUrl = "";
        if (root.TryGetProperty("servers", out var servers) && servers.ValueKind == JsonValueKind.Array && servers.GetArrayLength() > 0) baseUrl = S(servers[0], "url");
        else if (S(root, "host") is { Length: > 0 } host)
        {
            var scheme = root.TryGetProperty("schemes", out var sch) && sch.ValueKind == JsonValueKind.Array && sch.EnumerateArray().Any(x => x.GetString() == "https") ? "https" : "http";
            if (!root.TryGetProperty("schemes", out _)) scheme = "https";
            baseUrl = $"{scheme}://{host}{S(root, "basePath")}";
        }
        if (!baseUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase) || baseUrl.Contains('{') || !root.TryGetProperty("paths", out var paths) || paths.ValueKind != JsonValueKind.Object) return;
        baseUrl = baseUrl.TrimEnd('/');
        int n = 0;
        foreach (var p in paths.EnumerateObject())
        {
            if (p.Name.Contains('{') || p.Value.ValueKind != JsonValueKind.Object || !p.Value.TryGetProperty("get", out var get)) continue;
            var what = S(get, "summary");
            result.Items.Add(new FoundItem { Kind = "Example endpoint", Value = baseUrl + p.Name, Method = "GET", Note = "GET - from the OpenAPI spec" + (what.Length > 0 ? $": {what}" : "") });
            if (++n >= 5) break;
        }
    }

    private static string SentenceAround(string text, int index, int length)
    {
        int start = index;
        while (start > 0 && index - start < 160 && text[start - 1] is not ('.' or '!' or '?' or '|')) start--;
        int end = index + length;
        while (end < text.Length && end - index < 200 && text[end] is not ('.' or '!' or '?' or '|')) end++;
        return text[start..Math.Min(end + 1, text.Length)].Trim();
    }

    private static string Clean(string html) =>
        Regex.Replace(WebUtility.HtmlDecode(TagRx().Replace(html, " ")), @"\s+", " ").Trim();
}
