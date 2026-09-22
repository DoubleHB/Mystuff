using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ApiScout.Services;

/// <param name="Newer">True when <paramref name="Latest"/> is above the running version.</param>
/// <param name="Download">A URL or file to open, when the feed names one.</param>
/// <param name="Package">The portable zip of that version (a file or a URL) - what "Update and restart" installs.</param>
/// <param name="Sha256">Its checksum, when the source gives one.</param>
public sealed record UpdateInfo(bool Ok, bool Newer, Version? Latest, string Message, string? Download = null, string? Package = null, string? Sha256 = null);

/// <summary>
/// "Is there a newer ApiScout?" - answered from version tags (v1.4.0). The feed is one of:
/// a GitHub "owner/repo" (its tags), a folder holding a git repository (its tags, read straight from .git - no git.exe needed),
/// or a latest.json file / URL as publish.ps1 writes it. Empty = the repository this build was made from.
/// </summary>
public static partial class UpdateChecker
{
    [GeneratedRegex(@"^[A-Za-z0-9][\w.-]*/[\w.-]+$")]
    private static partial Regex GitHubRepoRx();
    // v1.4.0, 1.4.0, or desktop-v1.4.0 (the combined repository holds the Android app too, whose tags are mobile-v…)
    [GeneratedRegex(@"^(?:desktop-)?v?(\d+\.\d+(?:\.\d+){0,2})$", RegexOptions.IgnoreCase)]
    private static partial Regex VersionTagRx();

    /// <summary>The source folder recorded at build time (AssemblyMetadata "UpdateFeed").</summary>
    public static string DefaultFeed =>
        Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "UpdateFeed")?.Value ?? "";

    public static Version Current =>
        Version.TryParse((Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0").Split('+', '-')[0], out var v) ? v : new Version(0, 0);

    /// <param name="token">A GitHub token for a private repository. Only ever sent to api.github.com.</param>
    public static async Task<UpdateInfo> CheckAsync(string? feed, Version current, CancellationToken ct, string? token = null)
    {
        feed = string.IsNullOrWhiteSpace(feed) ? DefaultFeed : feed.Trim().Trim('"');
        if (feed.Length == 0) return new(false, false, null, "No update source is set. Enter a GitHub owner/repo, a folder with the ApiScout git repository, or a latest.json.");
        try
        {
            if (Http.IsWebUrl(feed)) return FromLatestJson(await Http.GetTextAsync(feed, ct, maxBytes: 200_000, timeoutSeconds: 15), current, feed);
            if (Directory.Exists(feed))
            {
                var json = Path.Combine(feed, "latest.json");
                if (File.Exists(json)) return FromLatestJson(await File.ReadAllTextAsync(json, ct), current, feed);
                var tags = GitTags(feed);
                if (tags is null) return new(false, false, null, $"{feed} holds neither a git repository nor a latest.json.");
                var fromTags = FromTags(tags, current, feed);
                // publish.ps1 puts the zip of a version into ..\ApiScout-Dist - if it is the tagged version, it can be installed from there
                var dist = Path.Combine(Path.GetDirectoryName(feed.TrimEnd('\\', '/')) ?? "", "ApiScout-Dist");
                if (fromTags.Newer && File.Exists(Path.Combine(dist, "latest.json")) &&
                    FromLatestJson(await File.ReadAllTextAsync(Path.Combine(dist, "latest.json"), ct), current, dist) is { Newer: true } built && built.Latest == fromTags.Latest)
                    return fromTags with { Download = built.Download, Package = built.Package, Sha256 = built.Sha256 };
                return fromTags;
            }
            if (File.Exists(feed)) return FromLatestJson(await File.ReadAllTextAsync(feed, ct), current, Path.GetDirectoryName(feed) ?? feed);
            if (GitHubRepoRx().IsMatch(feed) && !feed.Contains('\\'))
            {
                using var doc = JsonDocument.Parse(await GitHubAsync($"https://api.github.com/repos/{feed}/tags?per_page=100", token, ct));
                var names = doc.RootElement.ValueKind == JsonValueKind.Array
                    ? doc.RootElement.EnumerateArray().Select(t => t.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "").ToList() : [];
                var info = FromTags(names, current, "github.com/" + feed);
                if (!info.Newer) return info;
                info = info with { Download = $"https://github.com/{feed}/releases" };
                // the release of that tag may carry the portable zip: the tag is desktop-v1.6.0 in the combined repository, v1.6.0 in a desktop-only one
                var tagged = names.Contains($"desktop-v{Full(info.Latest!)}") ? $"desktop-v{Full(info.Latest!)}" : $"v{Full(info.Latest!)}";
                try
                {
                    using var rel = JsonDocument.Parse(await GitHubAsync($"https://api.github.com/repos/{feed}/releases/tags/{tagged}", token, ct));
                    if (rel.RootElement.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                        foreach (var a in assets.EnumerateArray())
                        {
                            string S(string n) => a.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";
                            if (!S("name").EndsWith("portable.zip", StringComparison.OrdinalIgnoreCase)) continue;
                            // a private repository only hands the file out through the API address, with the token
                            var package = string.IsNullOrEmpty(token) ? S("browser_download_url") : S("url");
                            var digest = S("digest");
                            return info with { Package = package, Sha256 = digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest[7..] : null };
                        }
                }
                catch (HttpRequestException) { } // no release for the tag: the version is still worth reporting
                return info;
            }
            return new(false, false, null, $"The update source cannot be reached from this PC: {feed}");
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return new(false, false, null, $"GitHub does not show {feed} - wrong name, or a private repository (then a token with read access is needed below).");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or JsonException or TaskCanceledException or NotSupportedException)
        {
            return new(false, false, null, $"Could not check {feed}: {ex.Message}");
        }
    }

    private static async Task<string> GitHubAsync(string url, string? token, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(15));
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Accept.ParseAdd("application/vnd.github+json");
        if (!string.IsNullOrWhiteSpace(token)) req.Headers.Authorization = new("Bearer", token.Trim());
        using var resp = await Http.Client.SendAsync(req, cts.Token);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync(cts.Token);
    }

    /// <summary>Tag names of the repository in (or at) <paramref name="folder"/>: loose refs plus packed-refs. Null when it is not a repository.</summary>
    internal static List<string>? GitTags(string folder)
    {
        var git = Directory.Exists(Path.Combine(folder, ".git")) ? Path.Combine(folder, ".git") : File.Exists(Path.Combine(folder, "HEAD")) ? folder : null;
        if (git is null) return null;
        var tags = new List<string>();
        var loose = Path.Combine(git, "refs", "tags");
        if (Directory.Exists(loose))
            tags.AddRange(Directory.EnumerateFiles(loose, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(loose, f).Replace('\\', '/')));
        var packed = Path.Combine(git, "packed-refs");
        if (File.Exists(packed))
            foreach (var line in File.ReadLines(packed))
                if (line.IndexOf(" refs/tags/", StringComparison.Ordinal) is > 0 and var at) tags.Add(line[(at + 11)..].Trim());
        return tags;
    }

    internal static UpdateInfo FromTags(IEnumerable<string> tags, Version current, string where)
    {
        var latest = tags.Select(t => VersionTagRx().Match(t.Trim()) is { Success: true } m && Version.TryParse(m.Groups[1].Value, out var v) ? v : null).OfType<Version>().Max();
        return latest is null ? new(false, false, null, $"No version tags (like v1.4.0) found in {where}.") : Compare(latest, current, where, null);
    }

    internal static UpdateInfo FromLatestJson(string json, Version current, string where)
    {
        using var doc = JsonDocument.Parse(json.TrimStart((char)0xFEFF)); // PowerShell writes a byte-order mark
        var root = doc.RootElement;
        string S(string n) => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";
        if (VersionTagRx().Match(S("version")) is not { Success: true } m || !Version.TryParse(m.Groups[1].Value, out var latest))
            return new(false, false, null, $"The latest.json in {where} has no \"version\".");
        var download = S("download");
        if (download.Length > 0 && !Http.IsWebUrl(download) && !Path.IsPathRooted(download))
            download = Http.IsWebUrl(where) ? new Uri(new Uri(where), download).ToString() : Directory.Exists(where) ? Path.Combine(where, download) : download;
        var info = Compare(latest, current, where, download.Length > 0 ? download : null);
        bool isZip = download.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
        return info.Newer && isZip ? info with { Package = download, Sha256 = S("sha256") is { Length: 64 } sum ? sum : null } : info;
    }

    // 1.4 and 1.4.0 are the same version
    private static Version Full(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));

    private static UpdateInfo Compare(Version latest, Version current, string where, string? download)
    {
        return Full(latest) > Full(current)
            ? new(true, true, latest, $"Version {Full(latest)} is available - you have {Full(current)}. Source: {where}", download)
            : new(true, false, latest, $"You have the newest version ({Full(current)}). Checked: {where}");
    }
}
