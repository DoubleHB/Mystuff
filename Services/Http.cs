using System.Net;
using System.Text;

namespace ApiScout.Services;

public static class Http
{
    public static readonly HttpClient Client = Create();

    private static HttpClient Create()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            MaxConnectionsPerServer = 8,
            AllowAutoRedirect = true,
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
