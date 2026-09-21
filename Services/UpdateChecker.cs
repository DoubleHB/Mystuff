using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ApiScout.Services;

/// <param name="Newer">True when <paramref name="Latest"/> is above the running version.</param>
/// <param name="Download">A URL or file to open, when the feed names one.</param>
public sealed record UpdateInfo(bool Ok, bool Newer, Version? Latest, string Message, string? Download = null);

/// <summary>
/// "Is there a newer ApiScout?" - answered from version tags (v1.4.0). The feed is one of:
/// a GitHub "owner/repo" (its tags), a folder holding a git repository (its tags, read straight from .git - no git.exe needed),
/// or a latest.json file / URL as publish.ps1 writes it. Empty = the repository this build was made from.
/// </summary>
public static partial class UpdateChecker
{
    [GeneratedRegex(@"^[A-Za-z0-9][\w.-]*/[\w.-]+$")]
    private static partial Regex GitHubRepoRx();
    [GeneratedRegex(@"^v?(\d+\.\d+(?:\.\d+){0,2})$", RegexOptions.IgnoreCase)]
    private static partial Regex VersionTagRx();

    /// <summary>The source folder recorded at build time (AssemblyMetadata "UpdateFeed").</summary>
    public static string DefaultFeed =>
        Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "UpdateFeed")?.Value ?? "";

    public static Version Current =>
        Version.TryParse((Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0").Split('+', '-')[0], out var v) ? v : new Version(0, 0);

    public static async Task<UpdateInfo> CheckAsync(string? feed, Version current, CancellationToken ct)
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
                return tags is null
                    ? new(false, false, null, $"{feed} holds neither a git repository nor a latest.json.")
                    : FromTags(tags, current, feed);
            }
            if (File.Exists(feed)) return FromLatestJson(await File.ReadAllTextAsync(feed, ct), current, Path.GetDirectoryName(feed) ?? feed);
            if (GitHubRepoRx().IsMatch(feed) && !feed.Contains('\\'))
            {
                using var doc = JsonDocument.Parse(await Http.GetTextAsync($"https://api.github.com/repos/{feed}/tags?per_page=100", ct, maxBytes: 1_000_000, timeoutSeconds: 15));
                var names = doc.RootElement.ValueKind == JsonValueKind.Array
                    ? doc.RootElement.EnumerateArray().Select(t => t.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "").ToList() : [];
                var info = FromTags(names, current, "github.com/" + feed);
                return info.Newer ? info with { Download = $"https://github.com/{feed}/releases" } : info;
            }
            return new(false, false, null, $"The update source cannot be reached from this PC: {feed}");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or JsonException or TaskCanceledException or NotSupportedException)
        {
            return new(false, false, null, $"Could not check {feed}: {ex.Message}");
        }
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
        if (download.Length > 0 && !Http.IsWebUrl(download) && !Path.IsPathRooted(download) && Directory.Exists(where)) download = Path.Combine(where, download);
        return Compare(latest, current, where, download.Length > 0 ? download : null);
    }

    private static UpdateInfo Compare(Version latest, Version current, string where, string? download)
    {
        // 1.4 and 1.4.0 are the same version
        static Version Full(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));
        return Full(latest) > Full(current)
            ? new(true, true, latest, $"Version {Full(latest)} is available - you have {Full(current)}. Source: {where}", download)
            : new(true, false, latest, $"You have the newest version ({Full(current)}). Checked: {where}");
    }
}
