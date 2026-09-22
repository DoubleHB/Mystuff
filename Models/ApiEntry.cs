namespace ApiScout.Models;

public enum AuthKind { None, ApiKey, OAuth, Other, Unknown }

/// <summary>How much you get without paying. An estimate - directories rarely state pricing.</summary>
public enum AccessLevel { FullFree, FreeTier, TrialOnly, Unknown }

/// <summary>One API found by a scan. Merged across sources by <see cref="Key"/>.</summary>
public sealed class ApiEntry
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Url { get; set; } = "";
    public string Category { get; set; } = "Other";
    public string RawCategory { get; set; } = "";
    public AuthKind Auth { get; set; } = AuthKind.Unknown;
    public string AuthRaw { get; set; } = "";
    public bool? Https { get; set; }
    public string Cors { get; set; } = "";
    public string? SpecUrl { get; set; }
    public int? Health { get; set; }
    /// <summary>"open" or "paid" when the source directory flags it (n0shake's Open/Trial column); otherwise empty.</summary>
    public string Pricing { get; set; } = "";
    /// <summary>When a scan first found this API (carried over from scan to scan).</summary>
    public DateTime? FirstSeen { get; set; }
    public List<string> Sources { get; set; } = [];
}

/// <summary>What the on-demand docs scan found for one API.</summary>
public sealed class DocsScanResult
{
    public DateTime ScannedAt { get; set; }
    public string PageUrl { get; set; } = "";
    public string? Error { get; set; }
    /// <summary>The icon the docs page declares for itself (apple-touch-icon / rel=icon), when it is a format WPF can show.</summary>
    public string? IconUrl { get; set; }
    public List<FoundItem> Items { get; set; } = [];
}

public sealed class FoundItem
{
    /// <summary>"Sample key", "Placeholder", "Sign-up link", "Free tier", "Auth scheme"</summary>
    public string Kind { get; set; } = "";
    public string Value { get; set; } = "";
    public string Note { get; set; } = "";
    /// <summary>HTTP method, for the endpoint kinds.</summary>
    public string Method { get; set; } = "";
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsEndpoint => Kind == "Example endpoint";
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsLink => Value.StartsWith("http", StringComparison.OrdinalIgnoreCase);
}

/// <summary>One earlier "Test this API" result. The request is kept as typed, so {key} stays a placeholder.</summary>
public sealed class TestHistoryEntry
{
    public DateTime At { get; set; }
    public string Method { get; set; } = "GET";
    public string Url { get; set; } = "";
    public string Headers { get; set; } = "";
    public string Body { get; set; } = "";
    public bool Ok { get; set; }
    public string Summary { get; set; } = "";
    public string Response { get; set; } = "";

    [System.Text.Json.Serialization.JsonIgnore]
    public string Label => $"{At:d MMM HH:mm:ss}  ·  {Method}  ·  {Summary.Split('\n')[0]}";
    public override string ToString() => Label;
}

/// <summary>One line of a scan report. <see cref="What"/> is only set for changed APIs ("Auth: API key → No key").</summary>
public sealed class ScanChange
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public string Category { get; set; } = "";
    public string What { get; set; } = "";
}

/// <summary>What one scan changed compared with the catalogue before it (changes.json keeps the last few).</summary>
public sealed class ScanReport
{
    public DateTime At { get; set; }
    public DateTime ComparedWith { get; set; }
    /// <summary>"Scan", "Automatic re-scan" or "Background scan".</summary>
    public string Trigger { get; set; } = "Scan";
    public int Total { get; set; }
    public int FailedSources { get; set; }
    public List<ScanChange> Added { get; set; } = [];
    public List<ScanChange> Removed { get; set; } = [];
    public List<ScanChange> Changed { get; set; } = [];

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsEmpty => Added.Count + Removed.Count + Changed.Count == 0;
    [System.Text.Json.Serialization.JsonIgnore]
    public string Label => $"{At:d MMM yyyy HH:mm}  ·  {Trigger}  ·  " + (IsEmpty ? "no changes" : $"{Added.Count:N0} new, {Removed.Count:N0} gone, {Changed.Count:N0} changed");
    public override string ToString() => Label;
}

public sealed class Catalog
{
    public DateTime ScannedAt { get; set; }
    /// <summary>The first scan ever: everything found then is the starting point, not "new".</summary>
    public DateTime? BaselineAt { get; set; }
    /// <summary>How many APIs this scan found that the one before it did not have.</summary>
    public int LastAdded { get; set; }
    public List<ApiEntry> Entries { get; set; } = [];
}

public sealed class Settings
{
    public bool Dark { get; set; } = true;
    /// <summary>Where "Check for updates" looks: a GitHub owner/repo, a folder with a git repo, or a latest.json path / URL. Empty = the repo this build came from.</summary>
    public string UpdateFeed { get; set; } = "";
    public DateTime? LastUpdateCheck { get; set; }
    /// <summary>Minimising hides the window into the notification area; the tray icon has a menu and announces new APIs.</summary>
    public bool TrayMode { get; set; }
    /// <summary>The first-run tour has been shown (or skipped).</summary>
    public bool TourSeen { get; set; }
    /// <summary>"Never", "Daily" or "Weekly": re-scan on start-up / while open once the last scan is that old.</summary>
    public string AutoRescan { get; set; } = "Weekly";
    /// <summary>Fetch provider favicons through a public icon service (only the domain name is sent).</summary>
    public bool ShowLogos { get; set; } = true;
    public bool ShowDashboard { get; set; } = true;
    /// <summary>A Windows scheduled task re-scans (Daily/Weekly, as AutoRescan says) even when API Free is closed.</summary>
    public bool BackgroundScan { get; set; }
    public Dictionary<string, bool> Sources { get; set; } = [];
    public List<string> CustomSources { get; set; } = [];
    public double Width { get; set; } = 1500;
    public double Height { get; set; } = 900;
    public bool Maximised { get; set; }
}

public sealed class RateLimitNote
{
    public DateTime Until { get; set; }
    public bool Estimated { get; set; }
}

public sealed class UserData
{
    public HashSet<string> Favourites { get; set; } = [];
    /// <summary>The user's own keys, DPAPI-encrypted (current Windows user), base64.</summary>
    public Dictionary<string, string> MyKeys { get; set; } = [];
    public Dictionary<string, string> Notes { get; set; } = [];
    /// <summary>The user's own labels per API.</summary>
    public Dictionary<string, List<string>> Tags { get; set; } = [];
    /// <summary>Request variables per API: name → value, filled in wherever a test request says {name}.</summary>
    public Dictionary<string, Dictionary<string, string>> Variables { get; set; } = [];
    /// <summary>GitHub token for a private update repository, DPAPI-encrypted like the keys. Never exported.</summary>
    public string? UpdateToken { get; set; }
    /// <summary>Named groups of APIs (collection name → API keys, in the order they were added).</summary>
    public Dictionary<string, List<string>> Collections { get; set; } = [];
    /// <summary>APIs that answered "rate limited", and until when.</summary>
    public Dictionary<string, RateLimitNote> RateLimits { get; set; } = [];
    /// <summary>Edited "Test this API" requests (URL + header), DPAPI-encrypted because they may hold a key.</summary>
    public Dictionary<string, string> TestRequests { get; set; } = [];
}
