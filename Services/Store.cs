using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ApiScout.Models;

namespace ApiScout.Services;

/// <summary>Everything ApiScout keeps on disk, under %LOCALAPPDATA%\ApiScout (APISCOUT_DATA overrides).</summary>
public sealed class Store
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly object _gate = new();

    public string Folder { get; }
    public bool IsPortable { get; }
    public Settings Settings { get; private set; } = new();
    public UserData User { get; private set; } = new();
    public Dictionary<string, DocsScanResult> DocsScans { get; private set; } = [];
    public Dictionary<string, List<TestHistoryEntry>> TestHistory { get; private set; } = [];

    /// <summary>What the last scans changed, newest first.</summary>
    public List<ScanReport> ScanReports { get; private set; } = [];
    public const int ScanReportsKept = 12;

    public void AddScanReport(ScanReport report)
    {
        ScanReports.Insert(0, report);
        if (ScanReports.Count > ScanReportsKept) ScanReports.RemoveRange(ScanReportsKept, ScanReports.Count - ScanReportsKept);
        Save("changes.json", ScanReports, indented: false);
    }

    public const int HistoryPerApi = 8;
    private const int HistoryResponseChars = 60_000;
    private const string HistoryFile = "test-history.dat";

    public Store()
    {
        var over = Environment.GetEnvironmentVariable("APISCOUT_DATA");
        // portable copy: a portable.txt beside the exe keeps everything in a data folder next to it (a USB stick, say)
        IsPortable = string.IsNullOrWhiteSpace(over) && File.Exists(Path.Combine(AppContext.BaseDirectory, "portable.txt"));
        Folder = !string.IsNullOrWhiteSpace(over) ? over
            : IsPortable ? Path.Combine(AppContext.BaseDirectory, "data")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ApiScout");
        Directory.CreateDirectory(Folder);
        Settings = Load<Settings>("settings.json") ?? new();
        User = Load<UserData>("userdata.json") ?? new();
        DocsScans = Load<Dictionary<string, DocsScanResult>>("docs-scans.json") ?? [];
        ScanReports = Load<List<ScanReport>>("changes.json") ?? [];
        LoadHistory();
    }

    /// <summary>Adds a test result (newest first) and saves. Requests may hold a key, so the whole file is DPAPI-encrypted.</summary>
    public TestHistoryEntry AddHistory(string apiKey, TestHistoryEntry entry)
    {
        if (entry.Response.Length > HistoryResponseChars)
            entry.Response = entry.Response[..HistoryResponseChars] + "\n\n… cut for the history";
        if (!TestHistory.TryGetValue(apiKey, out var list)) TestHistory[apiKey] = list = [];
        list.Insert(0, entry);
        if (list.Count > HistoryPerApi) list.RemoveRange(HistoryPerApi, list.Count - HistoryPerApi);
        SaveHistory();
        return entry;
    }

    public void ClearHistory(string apiKey)
    {
        if (TestHistory.Remove(apiKey)) SaveHistory();
    }

    private void LoadHistory()
    {
        var path = Path.Combine(Folder, HistoryFile);
        if (!File.Exists(path)) return;
        try
        {
            if (Unprotect(File.ReadAllText(path)) is { } json)
                TestHistory = JsonSerializer.Deserialize<Dictionary<string, List<TestHistoryEntry>>>(json) ?? [];
            else Log($"Could not decrypt {HistoryFile} (another Windows account, or a damaged file) - starting with an empty history.");
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { Log($"Could not read {HistoryFile}: {ex.Message}"); }
    }

    private void SaveHistory()
    {
        try
        {
            lock (_gate) WriteSafely(Path.Combine(Folder, HistoryFile), Protect(JsonSerializer.Serialize(TestHistory)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log($"Could not save {HistoryFile}: {ex.Message}"); }
    }

    public Catalog? LoadCatalog() => Load<Catalog>("catalog.json");
    public void SaveCatalog(Catalog c) => Save("catalog.json", c, indented: false);
    public void SaveSettings() => Save("settings.json", Settings);
    public void SaveUser() => Save("userdata.json", User);
    public void SaveDocsScans() => Save("docs-scans.json", DocsScans);

    public string? GetMyKey(string apiKey) => Unprotect(User.MyKeys.GetValueOrDefault(apiKey));

    public void SetMyKey(string apiKey, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) User.MyKeys.Remove(apiKey);
        else User.MyKeys[apiKey] = Protect(value.Trim());
        SaveUser();
    }

    public ApiTestRequest? GetTestRequest(string apiKey)
    {
        var plain = Unprotect(User.TestRequests.GetValueOrDefault(apiKey));
        if (plain is null) return null;
        if (plain.StartsWith('{'))
            try { return JsonSerializer.Deserialize<ApiTestRequest>(plain); } catch (JsonException) { return null; }
        // first version stored "url\nheader" for GET only
        return plain.Split('\n', 2) is [var url, var header] ? new("GET", url, header, "") : null;
    }

    public void SetTestRequest(string apiKey, ApiTestRequest request)
    {
        User.TestRequests[apiKey] = Protect(JsonSerializer.Serialize(request));
        SaveUser();
    }

    private static string Protect(string plain) =>
        Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser));

    private static string? Unprotect(string? b64)
    {
        if (b64 is null) return null;
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(b64), null, DataProtectionScope.CurrentUser)); }
        catch (Exception ex) when (ex is CryptographicException or FormatException) { return null; }
    }

    public void Log(string message)
    {
        try
        {
            lock (_gate)
                File.AppendAllText(Path.Combine(Folder, "apiscout.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private T? Load<T>(string file) where T : class
    {
        var path = Path.Combine(Folder, file);
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(path)); }
        catch (JsonException ex)
        {
            // the next save would overwrite it with empty data - favourites, notes and saved keys - so keep what is there
            var aside = $"{path}.unreadable-{DateTime.Now:yyyyMMdd-HHmmss}";
            try { File.Copy(path, aside, overwrite: true); } catch (Exception copy) when (copy is IOException or UnauthorizedAccessException) { }
            Log($"Could not read {file}: {ex.Message}. A copy was kept as {Path.GetFileName(aside)}.");
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log($"Could not read {file}: {ex.Message}"); return null; }
    }

    /// <summary>Write to a temp file of our own, flush it to the disk, then swap it in: a crash leaves the old file or the new one, never half of one.</summary>
    private static void WriteSafely(string path, string text)
    {
        var tmp = $"{path}.{Environment.ProcessId}.tmp";
        using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
        File.Move(tmp, path, overwrite: true);
    }

    private void Save<T>(string file, T value, bool indented = true)
    {
        try
        {
            lock (_gate)
            {
                WriteSafely(Path.Combine(Folder, file), JsonSerializer.Serialize(value, indented ? Json : null));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log($"Could not save {file}: {ex.Message}"); }
    }
}
