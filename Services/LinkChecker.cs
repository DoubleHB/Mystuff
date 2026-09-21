using System.Diagnostics;

namespace ApiScout.Services;

public sealed record LinkStatus(string Label, int? Code, int LatencyMs);

public static class LinkChecker
{
    /// <summary>Online = answered OK; Restricted = reachable but wants auth / blocks bots; Down = error or no answer.</summary>
    public static async Task<LinkStatus> CheckAsync(string url, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(15));
            using var resp = await Http.Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            int code = (int)resp.StatusCode;
            var label = code < 400 ? "Online" : code is 401 or 403 or 405 or 406 or 429 ? "Restricted" : "Down";
            return new(label, code, (int)sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return new("Timeout", null, (int)sw.ElapsedMilliseconds); }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or UriFormatException) { return new("Down", null, (int)sw.ElapsedMilliseconds); }
    }
}
