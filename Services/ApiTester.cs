using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace ApiScout.Services;

public sealed record ApiTestResult(bool Ok, bool IsJson, string Summary, string Body, string Raw = "", RateInfo? Rate = null);

/// <summary>What the response said about request limits. <see cref="Estimated"/> = the server gave no reset time, so an hour is assumed.</summary>
public sealed record RateInfo(bool Limited, DateTime? ResetAt, bool Estimated, int? Remaining, int? Limit);

/// <param name="Headers">Zero or more "Name: value" lines.</param>
public sealed record ApiTestRequest(string Method, string Url, string Headers, string Body);

/// <summary>Sends one request and returns the response formatted for reading.</summary>
public static class ApiTester
{
    private const int MaxBytes = 2_000_000;
    private const int MaxChars = 200_000;
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static readonly string[] Methods = ["GET", "POST", "PUT", "PATCH", "DELETE"];

    public static bool HasBody(string method) => method is "POST" or "PUT" or "PATCH";

    /// <summary>A plain GET with at most one header.</summary>
    public static Task<ApiTestResult> SendAsync(string url, string? header, CancellationToken ct) =>
        SendAsync(new ApiTestRequest("GET", url, header ?? "", ""), ct);

    public static async Task<ApiTestResult> SendAsync(ApiTestRequest request, CancellationToken ct)
    {
        if (!Uri.TryCreate(request.Url.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return new(false, false, "That is not a valid http(s) URL.", "");
        var method = request.Method.Trim().ToUpperInvariant();
        if (!Methods.Contains(method)) return new(false, false, $"'{request.Method}' is not a supported method.", "");
        if (ParseHeaders(request.Headers, out var headers) is { } badLine)
            return new(false, false, $"This header line is not in 'Name: value' form: {badLine}", "");

        var sw = Stopwatch.StartNew();
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(30));
            using var req = new HttpRequestMessage(new HttpMethod(method), uri);
            req.Headers.Accept.Clear();
            req.Headers.Accept.ParseAdd("application/json, text/plain;q=0.8, */*;q=0.5");

            if (HasBody(method))
            {
                // Content-Type: the user's header wins, otherwise judged from the body
                var bodyType = headers.FirstOrDefault(h => h.Name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)).Value ?? GuessContentType(request.Body);
                req.Content = new StringContent(request.Body, System.Text.Encoding.UTF8);
                req.Content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.TryParse(bodyType, out var parsed) ? parsed : new("text/plain");
                req.Content.Headers.ContentType.CharSet ??= "utf-8";
            }
            foreach (var (name, value) in headers)
            {
                if (name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)) continue;
                bool added = name.StartsWith("Content-", StringComparison.OrdinalIgnoreCase)
                    ? req.Content?.Headers.TryAddWithoutValidation(name, value) ?? true
                    : req.Headers.TryAddWithoutValidation(name, value);
                if (!added) return new(false, false, $"The header '{name}' could not be added.", "");
            }

            using var resp = await Http.Client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            await using var stream = await resp.Content.ReadAsStreamAsync(cts.Token);
            using var ms = new MemoryStream();
            var buffer = new byte[81920];
            int read;
            while (ms.Length < MaxBytes && (read = await stream.ReadAsync(buffer, cts.Token)) > 0) ms.Write(buffer, 0, read);
            sw.Stop();

            var raw = System.Text.Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
            var type = resp.Content.Headers.ContentType?.MediaType ?? "unknown type";
            var (body, isJson) = Format(raw);
            var summary = $"{(int)resp.StatusCode} {resp.ReasonPhrase} · {sw.ElapsedMilliseconds:N0} ms · {type} · {Size(ms.Length)}{(ms.Length >= MaxBytes ? "+ (cut off)" : "")}";
            if (!isJson && type.Contains("html"))
                summary += "\nThis is a web page, not JSON - probably the docs. Paste an endpoint URL from the docs above and send again.";
            else if ((int)resp.StatusCode is 401 or 403)
                summary += "\nThe API wants a key. Add it to the URL or a header - {key} inserts the key saved under 'My key'.";
            else if ((int)resp.StatusCode == 405)
                summary += "\nThis endpoint does not accept " + method + " - check the docs for the right method.";
            else if ((int)resp.StatusCode == 415)
                summary += "\nThe API did not like the body's Content-Type (" + req.Content?.Headers.ContentType + "). Add a Content-Type header to override it.";
            var rate = ReadRate((int)resp.StatusCode, name => resp.Headers.TryGetValues(name, out var v) ? v.FirstOrDefault() : null, DateTime.Now);
            if (rate is { Limited: true, ResetAt: { } until })
                summary += $"\nRate limited - should work again {When(until)}{(rate.Estimated ? " (an estimate - the server did not say when)" : " (the server said so)")}. Shared demo keys run out quickly; your own key has its own allowance.";
            else if (rate is { Remaining: { } left })
                summary += $"\nRequests left: {left:N0}{(rate.Limit is { } max ? $" of {max:N0}" : "")}{(rate.ResetAt is { } reset ? $" - resets {When(reset)}" : "")}";
            return new(resp.IsSuccessStatusCode, isJson, summary, body, raw, rate);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return new(false, false, "No answer within 30 seconds.", ""); }
        catch (HttpRequestException ex) { return new(false, false, "Request failed: " + (ex.InnerException?.Message ?? ex.Message), ""); }
    }

    public static string When(DateTime t) =>
        t.Date == DateTime.Today ? $"around {t:HH:mm}" : t.Date == DateTime.Today.AddDays(1) ? $"tomorrow around {t:HH:mm}" : $"on {t:d MMM} around {t:HH:mm}";

    /// <summary>Retry-After and the (X-)RateLimit-* family. Null when the response says nothing about limits.</summary>
    internal static RateInfo? ReadRate(int status, Func<string, string?> header, DateTime now)
    {
        static int? LeadingInt(string? s) => s is not null && System.Text.RegularExpressions.Regex.Match(s.Trim(), @"^\d{1,12}") is { Success: true } m && long.Parse(m.Value) <= int.MaxValue ? int.Parse(m.Value) : null;
        string? First(params string[] names) => names.Select(header).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

        DateTime? reset = null;
        if (First("Retry-After") is { } retry)
            reset = int.TryParse(retry.Trim(), out var secs) ? now.AddSeconds(secs)
                  : DateTimeOffset.TryParse(retry, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var date) ? date.LocalDateTime : null;
        if (reset is null && First("X-RateLimit-Reset", "RateLimit-Reset", "X-Rate-Limit-Reset") is { } r && long.TryParse(r.Trim().Split('.', ';')[0], out var n) && n > 0)
            reset = n > 100_000_000_000 ? DateTimeOffset.FromUnixTimeMilliseconds(n).LocalDateTime   // epoch ms
                  : n > 1_000_000_000 ? DateTimeOffset.FromUnixTimeSeconds(n).LocalDateTime          // epoch s
                  : now.AddSeconds(n);                                                                // seconds from now
        var remaining = LeadingInt(First("X-RateLimit-Remaining", "RateLimit-Remaining", "X-Rate-Limit-Remaining"));
        var limit = LeadingInt(First("X-RateLimit-Limit", "RateLimit-Limit", "X-Rate-Limit-Limit"));

        bool limited = status == 429 || (remaining == 0 && status >= 400);
        if (!limited) return remaining is null && limit is null ? null : new(false, reset > now ? reset : null, false, remaining, limit);
        bool estimated = reset is null;
        var until = reset ?? now.AddHours(1);
        if (until <= now) until = now.AddMinutes(1);
        if (until > now.AddDays(31)) until = now.AddDays(31);
        return new(true, until, estimated, remaining, limit);
    }

    /// <summary>Pretty-prints JSON; anything else comes back as it was. Both are capped for display.</summary>
    internal static (string Body, bool IsJson) Format(string raw)
    {
        var text = raw.Trim().TrimStart((char)0xFEFF);
        bool isJson = false;
        if (text.Length > 0 && text[0] is '{' or '[' or '"')
        {
            try
            {
                using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
                text = JsonSerializer.Serialize(doc.RootElement, Pretty);
                isJson = true;
            }
            catch (JsonException) { } // cut-off or malformed: show as received
        }
        if (text.Length > MaxChars) text = text[..MaxChars] + $"\n\n… cut after {MaxChars:N0} characters";
        return (text, isJson);
    }

    /// <summary>Returns the first malformed line, or null when every line parsed.</summary>
    internal static string? ParseHeaders(string text, out List<(string Name, string Value)> headers)
    {
        headers = [];
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (SplitHeader(line) is not { } h) return line;
            headers.Add(h);
        }
        return null;
    }

    internal static string GuessContentType(string body)
    {
        var b = body.Trim();
        if (b.Length == 0 || b[0] is '{' or '[') return "application/json";
        if (b[0] == '<') return "application/xml";
        return !b.Contains('\n') && System.Text.RegularExpressions.Regex.IsMatch(b, @"^[^=&\s]+=[^&\s]*(&[^=&\s]+=[^&\s]*)*$")
            ? "application/x-www-form-urlencoded" : "text/plain";
    }

    /// <summary>The request as a bash-style curl command. {key} placeholders are left in, so a copied command never carries the saved key.</summary>
    public static string ToCurl(ApiTestRequest r)
    {
        static string Q(string s) => "'" + s.Replace("'", "'\\''") + "'";
        var method = r.Method.Trim().ToUpperInvariant();
        var sb = new System.Text.StringBuilder("curl");
        if (method != "GET") sb.Append(" -X ").Append(method);
        sb.Append(' ').Append(Q(r.Url.Trim()));
        ParseHeaders(r.Headers, out var headers);
        if (HasBody(method) && r.Body.Trim().Length > 0 && !headers.Any(h => h.Name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)))
            headers.Add(("Content-Type", GuessContentType(r.Body)));
        foreach (var (name, value) in headers) sb.Append(" \\\n  -H ").Append(Q($"{name}: {value}"));
        if (HasBody(method) && r.Body.Trim().Length > 0) sb.Append(" \\\n  --data-raw ").Append(Q(r.Body.Trim()));
        return sb.ToString();
    }

    /// <summary>Pretty-prints a JSON body; null when it is not valid JSON.</summary>
    public static string? TidyJson(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            return JsonSerializer.Serialize(doc.RootElement, Pretty);
        }
        catch (JsonException) { return null; }
    }

    internal static (string Name, string Value)? SplitHeader(string? header)
    {
        if (string.IsNullOrWhiteSpace(header)) return null;
        int colon = header.IndexOf(':');
        if (colon <= 0) return null;
        var name = header[..colon].Trim();
        var value = header[(colon + 1)..].Trim();
        return name.Length == 0 || value.Length == 0 ? null : (name, value);
    }

    private static string Size(long bytes) => bytes < 1024 ? $"{bytes} B" : bytes < 1_048_576 ? $"{bytes / 1024.0:F1} KB" : $"{bytes / 1_048_576.0:F1} MB";
}
