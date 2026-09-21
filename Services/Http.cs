using System.Net;
using System.Text;

namespace ApiScout.Services;

public static class Http
{
    public static readonly HttpClient Client = Create(redirects: true);
    /// <summary>For "Test this API": redirects are followed by hand, so a key in a header is never passed on to another host.</summary>
    public static readonly HttpClient NoRedirects = Create(redirects: false);

    /// <summary>Absolute http(s) - what a docs link has to be.</summary>
    public static bool IsWebUrl(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme is "http" or "https";

    /// <summary>
    /// False for localhost and literal private / link-local addresses. The directories are lists anyone can edit, so
    /// the scanners (docs, links, logos) do not let one of them point a request into the user's own network.
    /// </summary>
    public static bool IsPublicWebUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme is not ("http" or "https")) return false;
        if (u.IsLoopback || u.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) || u.Host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase)) return false;
        if (!IPAddress.TryParse(u.Host.Trim('[', ']'), out var ip)) return true;
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6) return !(ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6UniqueLocal);
        var b = ip.GetAddressBytes();
        return !(b[0] is 10 or 127 or 0 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254) || (b[0] == 100 && b[1] is >= 64 and <= 127));
    }

    private static HttpClient Create(bool redirects)
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            MaxConnectionsPerServer = 8,
            AllowAutoRedirect = redirects,
            UseCookies = false, // one jar shared by thousands of sites would make tests depend on what ran before
            ConnectTimeout = TimeSpan.FromSeconds(12),
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(120) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) ApiScout/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/json,text/plain,*/*");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-GB,en;q=0.9");
        return client;
    }

    /// <summary>GET a text body, reading at most <paramref name="maxBytes"/>.</summary>
    public static async Task<string> GetTextAsync(string url, CancellationToken ct, int maxBytes = 32_000_000, int timeoutSeconds = 90)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        using var resp = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        resp.EnsureSuccessStatusCode();
        await using var stream = await resp.Content.ReadAsStreamAsync(cts.Token);
        using var ms = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while (ms.Length < maxBytes && (read = await stream.ReadAsync(buffer, cts.Token)) > 0)
            ms.Write(buffer, 0, read);
        var charset = resp.Content.Headers.ContentType?.CharSet?.Trim('"');
        Encoding enc = Encoding.UTF8;
        if (!string.IsNullOrEmpty(charset))
            try { enc = Encoding.GetEncoding(charset); } catch (ArgumentException) { }
        return enc.GetString(ms.GetBuffer(), 0, (int)ms.Length);
    }
}
