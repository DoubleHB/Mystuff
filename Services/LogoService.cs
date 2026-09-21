using System.Collections.Concurrent;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ApiScout.Services;

/// <summary>
/// Brand icons for API providers: the site's favicon, fetched once through a public icon service and kept on disk
/// (logos\ in the data folder). Only the provider's domain name is sent. Sites without an icon are remembered too.
/// </summary>
/// <param name="Key">Cache key / file name.</param>
/// <param name="Label">Shown under the API name: a domain, "github.com/owner" or "RapidAPI · by provider".</param>
public sealed record BrandInfo(string Key, string Label, string[] IconUrls);

public static class LogoService
{
    private static readonly HashSet<string> GitHubPages = new(StringComparer.OrdinalIgnoreCase)
        { "features", "topics", "marketplace", "settings", "orgs", "sponsors", "about", "pricing", "collections", "explore", "apps", "login", "join", "search" };

    /// <summary>
    /// Who is behind this docs URL. APIs hosted on a code host or marketplace are credited to their real owner where
    /// the host tells us: a GitHub repo shows its owner's avatar; RapidAPI names the provider but publishes no logo.
    /// </summary>
    public static BrandInfo Brand(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return new("", "", []);
        var host = u.Host.ToLowerInvariant();
        var seg = u.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        string? owner = host is "github.com" or "www.github.com" && seg.Length >= 1 && !GitHubPages.Contains(seg[0]) ? seg[0]
                      : host.EndsWith(".github.io", StringComparison.Ordinal) ? host[..^".github.io".Length] : null;
        if (owner is not null && System.Text.RegularExpressions.Regex.IsMatch(owner, @"^[A-Za-z\d](?:[A-Za-z\d-]{0,38})$"))
            return new("github.com_" + owner.ToLowerInvariant(), "github.com/" + owner, [$"https://github.com/{owner}.png?size=64"]);

        var domain = BrandDomain(url);
        var label = domain == "rapidapi.com" && seg.Length >= 1 && seg[0] != "collection" ? $"RapidAPI · by {seg[0]}" : domain;
        return new(domain, label, domain.Length == 0 ? [] :
            [$"https://www.google.com/s2/favicons?domain={Uri.EscapeDataString(domain)}&sz=64", $"https://icons.duckduckgo.com/ip3/{Uri.EscapeDataString(domain)}.ico"]);
    }

    private static readonly ConcurrentDictionary<string, Task<ImageSource?>> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly SemaphoreSlim Gate = new(6);
    private static readonly string[] Prefixes = ["www.", "api.", "apis.", "docs.", "doc.", "developer.", "developers.", "dev.", "app.", "portal."];

    public static string Folder { get; set; } = "";

    /// <summary>"api.foo.com" and "docs.foo.com" show foo.com's icon - that is where the brand lives.</summary>
    public static string BrandDomain(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return "";
        var host = u.Host.ToLowerInvariant();
        for (bool cut = true; cut;)
        {
            cut = false;
            foreach (var p in Prefixes)
                if (host.StartsWith(p, StringComparison.Ordinal) && host[p.Length..].Contains('.')) { host = host[p.Length..]; cut = true; }
        }
        return host;
    }

    public static Task<ImageSource?> GetAsync(string domain) => GetAsync(Brand("https://" + domain));

    /// <param name="pageIcon">The icon the docs page itself declares (from a docs scan) - tried when the icon services have nothing.</param>
    public static Task<ImageSource?> GetAsync(BrandInfo brand, string? pageIcon = null) =>
        brand.Key.Length == 0 || Folder.Length == 0 ? Task.FromResult<ImageSource?>(null)
        : Cache.GetOrAdd(brand.Key + (pageIcon is null ? "" : "|" + pageIcon), _ => LoadAsync(brand, pageIcon));

    private const int MaxIconBytes = 400_000;

    private static async Task<ImageSource?> LoadAsync(BrandInfo brand, string? pageIcon)
    {
        var domain = brand.Key;
        var safe = string.Concat(domain.Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' ? c : '_'));
        var file = Path.Combine(Folder, safe + ".png");
        var none = Path.Combine(Folder, safe + ".none");
        try
        {
            if (File.Exists(file))
            {
                if (Decode(await File.ReadAllBytesAsync(file)) is { } cached) return cached;
                File.Delete(file); // a damaged cache file: fetch it again
            }
            bool knownNone = File.Exists(none) && DateTime.Now - File.GetLastWriteTime(none) < TimeSpan.FromDays(30);
            if (knownNone && pageIcon is null) return null;
            string[] urls = knownNone ? [pageIcon!] : pageIcon is null ? brand.IconUrls : [.. brand.IconUrls, pageIcon];

            await Gate.WaitAsync();
            try
            {
                bool definite = true; // every service said "no such icon", rather than "busy" or "try later"
                foreach (var url in urls)
                {
                    if (!Http.IsPublicWebUrl(url)) continue; // the page's own icon link is whatever the page says
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(12));
                    using var resp = await Http.Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                    if (!resp.IsSuccessStatusCode) { definite &= (int)resp.StatusCode is 404 or 410 or 400; continue; }
                    if (resp.Content.Headers.ContentLength > MaxIconBytes) continue;
                    // read with a ceiling: the link could point at anything
                    await using var stream = await resp.Content.ReadAsStreamAsync(cts.Token);
                    using var ms = new MemoryStream();
                    var buffer = new byte[16384];
                    int read;
                    while (ms.Length <= MaxIconBytes && (read = await stream.ReadAsync(buffer, cts.Token)) > 0) ms.Write(buffer, 0, read);
                    var bytes = ms.ToArray();
                    if (bytes.Length is < 100 or > MaxIconBytes || Decode(bytes) is not { } image) continue;
                    Directory.CreateDirectory(Folder);
                    await File.WriteAllBytesAsync(file, bytes);
                    return image;
                }
                if (definite)
                {
                    Directory.CreateDirectory(Folder);
                    await File.WriteAllBytesAsync(none, []);
                }
                return null;
            }
            finally { Gate.Release(); }
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException or UnauthorizedAccessException)
        {
            Cache.TryRemove(domain + (pageIcon is null ? "" : "|" + pageIcon), out _); // offline or busy: try again next time this row is shown
            return null;
        }
    }

    internal static ImageSource? Decode(byte[] bytes)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 64;
            image.StreamSource = new MemoryStream(bytes);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or ArgumentException or InvalidOperationException) { return null; }
    }
}
