using System.IO.Compression;
using System.Security.Cryptography;

namespace ApiScout.Services;

/// <summary>
/// "Update and restart" for the portable copy: fetch the zip the update check found, take ApiScout.exe out of it and
/// swap it in. A running exe cannot be overwritten but it can be renamed, so the old one becomes ApiScout.exe.old and
/// is deleted the next time ApiScout starts.
/// </summary>
public static class Updater
{
    private const long MaxZipBytes = 400_000_000;

    /// <summary>The single-file portable exe. The ordinary build is a folder of files that publish.ps1 replaces as a whole.</summary>
    public static bool IsSingleFile => string.IsNullOrEmpty(typeof(Updater).Assembly.Location);

    public static string? ExePath => Environment.ProcessPath;

    /// <summary>Removes what the last update left behind. Called at start-up.</summary>
    public static void CleanUp()
    {
        if (ExePath is not { } exe) return;
        foreach (var leftover in new[] { exe + ".old", exe + ".new" })
            try { if (File.Exists(leftover)) File.Delete(leftover); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } // still held by the instance that is closing: next time
    }

    /// <summary>Downloads (or copies) the package, checks it and swaps the exe. The caller restarts on success.</summary>
    /// <param name="token">GitHub token for a private repository - only sent when the package is an api.github.com address.</param>
    public static async Task<(bool Ok, string Message)> InstallAsync(UpdateInfo info, string? token, IProgress<double> progress, CancellationToken ct, string? exePath = null)
    {
        exePath ??= ExePath;
        if (info.Package is not { Length: > 0 } package) return (false, "The update source names no portable zip for that version.");
        if (exePath is null || !File.Exists(exePath)) return (false, "Could not work out where ApiScout.exe is.");
        var zip = Path.Combine(Path.GetTempPath(), $"apiscout-update-{Guid.NewGuid():N}.zip");
        var fresh = exePath + ".new";
        var old = exePath + ".old";
        try
        {
            if (Http.IsWebUrl(package)) await DownloadAsync(package, token, zip, progress, ct);
            else
            {
                if (!File.Exists(package)) return (false, $"The zip is not there: {package}");
                await using var from = File.OpenRead(package);
                await CopyAsync(from, zip, from.Length, progress, ct);
            }

            if (info.Sha256 is { Length: 64 } expected)
            {
                await using var check = File.OpenRead(zip);
                var actual = Convert.ToHexString(await SHA256.HashDataAsync(check, ct));
                if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase)) return (false, "The downloaded zip does not match its checksum - nothing was changed.");
            }

            using (var archive = ZipFile.OpenRead(zip))
            {
                var entry = archive.Entries.FirstOrDefault(e => e.FullName.Equals("ApiScout.exe", StringComparison.OrdinalIgnoreCase));
                if (entry is null || entry.Length < 1_000_000) return (false, "The zip holds no ApiScout.exe - nothing was changed.");
                entry.ExtractToFile(fresh, overwrite: true);
            }

            if (File.Exists(old)) File.Delete(old);
            File.Move(exePath, old);
            try { File.Move(fresh, exePath); }
            catch { File.Move(old, exePath); throw; } // put the working exe back
            return (true, $"Version {info.Latest} is in place.");
        }
        catch (OperationCanceledException) { return (false, "Update cancelled - nothing was changed."); }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
        {
            return (false, $"The update did not work: {ex.Message} Nothing was changed.");
        }
        finally
        {
            foreach (var temp in new[] { zip, fresh })
                try { if (File.Exists(temp)) File.Delete(temp); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private static async Task DownloadAsync(string url, string? token, string target, IProgress<double> progress, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (new Uri(url).Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase))
        {
            // a release asset of a private repository: asked for as a file, with the token (HttpClient drops it when GitHub redirects to its file store)
            req.Headers.Accept.ParseAdd("application/octet-stream");
            if (!string.IsNullOrWhiteSpace(token)) req.Headers.Authorization = new("Bearer", token.Trim());
        }
        using var resp = await Http.Client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        if (resp.Content.Headers.ContentLength > MaxZipBytes) throw new InvalidDataException("The file is far too big to be ApiScout.");
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        await CopyAsync(stream, target, resp.Content.Headers.ContentLength ?? 0, progress, ct);
    }

    private static async Task CopyAsync(Stream from, string target, long length, IProgress<double> progress, CancellationToken ct)
    {
        await using var to = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);
        var buffer = new byte[1 << 16];
        long total = 0;
        int read;
        while ((read = await from.ReadAsync(buffer, ct)) > 0)
        {
            await to.WriteAsync(buffer.AsMemory(0, read), ct);
            total += read;
            if (total > MaxZipBytes) throw new InvalidDataException("The file is far too big to be ApiScout.");
            if (length > 0) progress.Report(100.0 * total / length);
        }
    }
}
