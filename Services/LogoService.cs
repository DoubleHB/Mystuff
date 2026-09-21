using System.Collections.Concurrent;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ApiScout.Services;

/// <summary>
/// Brand icons for API providers: the site's favicon, fetched once through a public icon service and kept on disk
/// (logos\ in the data folder). Only the provider's domain name is sent. Sites without an icon are remembered too.
/// </summary>
public static class LogoService
{
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

    public static Task<ImageSource?> GetAsync(string domain) =>
        domain.Length == 0 || Folder.Length == 0 ? Task.FromResult<ImageSource?>(null) : Cache.GetOrAdd(domain, LoadAsync);

    private static async Task<ImageSource?> LoadAsync(string domain)
    {
        var safe = string.Concat(domain.Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' ? c : '_'));
        var file = Path.Combine(Folder, safe + ".png");
        var none = Path.Combine(Folder, safe + ".none");
        try
        {
            if (File.Exists(file)) return Decode(await File.ReadAllBytesAsync(file));
            if (File.Exists(none) && DateTime.Now - File.GetLastWriteTime(none) < TimeSpan.FromDays(30)) return null;

            await Gate.WaitAsync();
            try
            {
                foreach (var url in new[] { $"https://www.google.com/s2/favicons?domain={Uri.EscapeDataString(domain)}&sz=64", $"https://icons.duckduckgo.com/ip3/{Uri.EscapeDataString(domain)}.ico" })
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(12));
                    using var resp = await Http.Client.GetAsync(url, cts.Token);
                    if (!resp.IsSuccessStatusCode) continue;
                    var bytes = await resp.Content.ReadAsByteArrayAsync(cts.Token);
                    if (bytes.Length is < 100 or > 400_000 || Decode(bytes) is not { } image) continue;
                    Directory.CreateDirectory(Folder);
                    await File.WriteAllBytesAsync(file, bytes);
                    return image;
                }
                Directory.CreateDirectory(Folder);
                await File.WriteAllBytesAsync(none, []);
                return null;
            }
            finally { Gate.Release(); }
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException or UnauthorizedAccessException)
        {
            Cache.TryRemove(domain, out _); // offline or busy: try again next time this row is shown
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
