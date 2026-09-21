using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows.Media;
using ApiScout.Models;
using ApiScout.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ApiScout.ViewModels;

/// <summary>One grid row: the scanned entry plus key knowledge, link status and the user's own bits.</summary>
public sealed partial class ApiRow : ObservableObject
{
    private readonly KeyHint? _hint;

    public ApiRow(ApiEntry entry, DateTime? baseline = null)
    {
        Entry = entry;
        IsNew = Scanner.IsNew(entry, baseline, DateTime.Now);
        _brand = LogoService.Brand(entry.Url);
        _hint = KeyKnowledge.Find(entry);
        SearchText = $"{entry.Name} {entry.Description} {entry.Category} {entry.RawCategory} {entry.Url}".ToLowerInvariant();
    }

    public ApiEntry Entry { get; }

    /// <summary>UI Automation reads a grid row's name from this.</summary>
    public override string ToString() => Entry.Name;
    public string SearchText { get; }
    /// <summary>First found by a scan in the last 14 days (and not part of the very first scan).</summary>
    public bool IsNew { get; }
    // ---- brand: the provider's site icon, with a coloured initial until (or unless) one is found
    public static bool ShowLogos { get; set; } = true;
    private static readonly Brush[] AvatarBrushes = [.. new[] { "#4F8CFF", "#3DDCB4", "#B48CFF", "#F5B83D", "#FF7A59", "#2BB5D9", "#E0609A", "#6FBF4A" }
        .Select(h => { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(h)); b.Freeze(); return (Brush)b; })];

    private readonly BrandInfo _brand;
    /// <summary>Who is behind the API: a domain, "github.com/owner" or "RapidAPI · by provider".</summary>
    public string BrandDomain => _brand.Label;
    public string Initial => Entry.Name.FirstOrDefault(char.IsLetterOrDigit) is var c and not '\0' ? char.ToUpperInvariant(c).ToString() : "?";
    public Brush AvatarBrush => AvatarBrushes[(int)((uint)StableHash(_brand.Key.Length > 0 ? _brand.Key : Entry.Name) % AvatarBrushes.Length)];

    private ImageSource? _logo;
    private bool _logoRequested;

    /// <summary>Loaded the first time a visible row asks for it, so only what is scrolled into view is ever fetched.</summary>
    public ImageSource? Logo
    {
        get
        {
            if (!ShowLogos) return null;
            if (!_logoRequested) { _logoRequested = true; _ = LoadLogoAsync(); }
            return _logo;
        }
    }

    public void RefreshLogo() => OnPropertyChanged(nameof(Logo));

    // a docs scan may have found the page's own icon: give a still-missing logo another go
    partial void OnDocsScanChanged(DocsScanResult? value)
    {
        if (_logo is null && _logoRequested && value?.IconUrl is not null) { _logoRequested = false; RefreshLogo(); }
    }

    private async Task LoadLogoAsync()
    {
        if (await LogoService.GetAsync(_brand, DocsScan?.IconUrl) is { } image) { _logo = image; OnPropertyChanged(nameof(Logo)); }
    }

    private static int StableHash(string s) { unchecked { int h = 23; foreach (var ch in s) h = h * 31 + ch; return h; } }

    public string FirstSeenLabel => Entry.FirstSeen is { } d ? $"first seen {d:d MMM yyyy}" : "";

    public string Key => Entry.Key;
    public string Name => Entry.Name;
    public string Description => Entry.Description;
    public string Url => Entry.Url;
    public string Category => Entry.Category;
    public string Cors => Entry.Cors;
    public string? SpecUrl => Entry.SpecUrl;
    public bool HasSpec => !string.IsNullOrEmpty(Entry.SpecUrl);
    public string HttpsLabel => Entry.Https switch { true => "Yes", false => "No", _ => "" };
    public string SourcesLabel => string.Join(", ", Entry.Sources);
    public string HealthLabel => Entry.Health is { } h ? $"{h}%" : "";

    public string? DemoKey => _hint?.DemoKey;
    public bool HasDemoKey => _hint?.DemoKey is not null;
    public string? KeyUsage => _hint?.KeyUsage;
    public bool HasKeyUsage => _hint?.KeyUsage is not null;
    public string? SignupUrl => _hint?.SignupUrl;
    public bool HasSignupUrl => _hint?.SignupUrl is not null;
    public string? Example => _hint?.Example;
    public bool HasExample => _hint?.Example is not null;
    public bool KeylessWorks => _hint?.KeylessWorks == true || Entry.Auth == AuthKind.None;

    public string AuthLabel => Entry.Auth switch
    {
        AuthKind.None => "No key",
        AuthKind.ApiKey => "API key",
        AuthKind.OAuth => "OAuth",
        AuthKind.Other => Entry.AuthRaw,
        _ => "Unknown",
    };

    /// <summary>Short badge for the grid: what it takes to start calling this API.</summary>
    public string KeyBadge =>
        HasDemoKey ? "Demo key" :
        Entry.Auth == AuthKind.None ? "Open" :
        _hint?.KeylessWorks == true ? "Key optional" :
        Entry.Auth == AuthKind.ApiKey ? (HasSignupUrl ? "Free key" : "Key needed") :
        Entry.Auth == AuthKind.OAuth ? "OAuth" :
        Entry.Auth == AuthKind.Other ? "Other" : "?";

    public string KeyHeadline =>
        HasDemoKey ? "A public demo key is available" :
        Entry.Auth == AuthKind.None ? "No key needed" :
        _hint?.KeylessWorks == true ? "Works without a key - a free key lifts the limits" :
        Entry.Auth == AuthKind.ApiKey ? "Needs a free API key" :
        Entry.Auth == AuthKind.OAuth ? "Needs an OAuth app registration" :
        Entry.Auth == AuthKind.Other ? $"Auth: {Entry.AuthRaw}" : "Key requirements not stated";

    public string HowTo => _hint?.HowTo ?? KeyKnowledge.GenericHowTo(Entry);

    // ---- how much is free: curated knowledge first, then the directory's own flags, then wording, then a docs scan
    private static readonly Regex FreeTierWords = new(@"\b(free (tier|plan|account|quota|usage)|freemium|free for (non-?commercial|personal)|limited free)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PaidWords = new(@"\b(free trial|\d+[- ]day trial|trial (version|period|account|key|plan)|paid|premium|subscription)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public AccessLevel Access
    {
        get
        {
            if (_hint is not null) return KeyKnowledge.AccessFor(_hint);
            if (Entry.Pricing == "paid") return AccessLevel.TrialOnly;
            if (Entry.Pricing == "open" || Entry.Auth == AuthKind.None) return AccessLevel.FullFree;
            if (FreeTierWords.IsMatch(Entry.Description)) return AccessLevel.FreeTier;
            if (PaidWords.IsMatch(Entry.Description)) return AccessLevel.TrialOnly;
            return AccessFromDocs(DocsScan);
        }
    }

    /// <summary>What a docs scan says: a free plan or stated limits = free tier; only trials or only paid plans = trial only.</summary>
    internal static AccessLevel AccessFromDocs(DocsScanResult? scan)
    {
        if (scan is null) return AccessLevel.Unknown;
        var pricing = scan.Items.Where(i => i.Kind == "Pricing").Select(i => i.Value).ToList();
        var limits = scan.Items.Where(i => i.Kind == "Free tier / limits").Select(i => i.Value).ToList();
        bool IsTrial(string v) => v.Contains("trial", StringComparison.OrdinalIgnoreCase);
        if (pricing.Any(v => v.StartsWith("No free plan"))) return limits.Any(v => !IsTrial(v) && v.Contains("free", StringComparison.OrdinalIgnoreCase)) ? AccessLevel.FreeTier : AccessLevel.TrialOnly;
        var all = pricing.Concat(limits).ToList();
        if (all.Count == 0) return AccessLevel.Unknown;
        return all.Any(v => !IsTrial(v)) ? AccessLevel.FreeTier : AccessLevel.TrialOnly;
    }

    public string AccessLabel => Access switch
    {
        AccessLevel.FullFree => "Full free access",
        AccessLevel.FreeTier => "Free tier (limited)",
        AccessLevel.TrialOnly => "Demo / trial only",
        _ => "Not stated",
    };

    public string AccessNote => Access switch
    {
        AccessLevel.FullFree when _hint is not null => "The whole API is free - a key (if any) is only for fair-use limits.",
        AccessLevel.FullFree when Entry.Pricing == "open" => "Flagged as open source / open access by the directory that listed it.",
        AccessLevel.FullFree => "Listed as needing no key, so everything it offers is open to call.",
        AccessLevel.FreeTier when _hint is not null => "Free to keep using within the limits above; paid plans lift them.",
        AccessLevel.FreeTier => "The description or docs mention a free plan or usage limits - expect paid plans above them.",
        AccessLevel.TrialOnly when _hint is not null => "The free part is a demo or trial - real use needs a paid plan.",
        AccessLevel.TrialOnly when Entry.Pricing == "paid" => "Flagged as paid / trial by the directory that listed it.",
        AccessLevel.TrialOnly => "The description or docs talk about trials or paid plans, with no free plan mentioned.",
        _ => "The directories do not say what is free. 'Scan docs for key info' may find out.",
    };

    [ObservableProperty] private bool _isFavourite;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private int? _latencyMs;
    [ObservableProperty] private string _myKey = "";
    [ObservableProperty] private string _note = "";

    /// <summary>The user's labels, comma separated ("work, maps").</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Tags), nameof(HasTags), nameof(TagsLabel))] private string _tagsText = "";
    public IReadOnlyList<string> Tags => ParseTags(TagsText);
    public bool HasTags => TagsText.Trim().Length > 0;
    public string TagsLabel => HasTags ? "🏷 " + string.Join(", ", Tags) : "";

    /// <summary>Split on , or ; - trimmed, no duplicates (ignoring case), at most 8 tags of 24 characters.</summary>
    public static List<string> ParseTags(string text) =>
        [.. text.Split([" , ", ",", ";"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.Length > 24 ? t[..24].Trim() : t).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Take(8)];
    [ObservableProperty] private bool _isScanningDocs;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasDocsScan), nameof(DocsScanSummary), nameof(Access), nameof(AccessLabel), nameof(AccessNote))] private DocsScanResult? _docsScan;

    // "Test this API" - starts from the known working example, otherwise the docs URL for the user to replace
    [ObservableProperty] private string _testUrl = "";
    [ObservableProperty] private string _testHeader = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ShowTestBody))] private string _testMethod = "GET";
    [ObservableProperty] private string _testBody = "";
    public bool ShowTestBody => ApiTester.HasBody(TestMethod);

    // earlier results for this API, newest first (loaded from the store when the row is first selected)
    public ObservableCollection<TestHistoryEntry> History { get; } = [];
    public bool HistoryLoaded { get; set; }
    public bool HasHistory => History.Count > 0;
    public void HistoryChanged() => OnPropertyChanged(nameof(HasHistory));
    [ObservableProperty] private TestHistoryEntry? _selectedHistory;
    /// <summary>The last real response as received - what "C# classes" is generated from.</summary>
    public string TestRaw { get; set; } = "";
    [ObservableProperty] private string _testClasses = "";
    [ObservableProperty] private bool _isTesting;
    [ObservableProperty] private bool _testOk;
    [ObservableProperty] private string _testSummary = "";
    [ObservableProperty] private string _testResponse = "";

    public string DefaultTestUrl => Example ?? Url;
    public string DefaultTestHeader => KeyUsage?.StartsWith("Header: ") == true ? KeyUsage[8..] : "";
    public string TestHint => HasExample
        ? "Pre-filled with a request that works as it is - press Test this API (Ctrl+T)."
        : "ApiScout only knows this API's docs page. Paste an endpoint URL from the docs, pick the method, then press Test this API.";

    // ---- rate limit memory + last test, for the Try it card, the grid and the shortlist
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsLimited), nameof(LimitLabel), nameof(LimitShort))] private DateTime? _limitedUntil;
    public bool LimitEstimated { get; set; }
    public bool IsLimited => LimitedUntil is { } u && u > DateTime.Now;
    public string LimitLabel => IsLimited ? $"⏳ Rate limited - should work again {ApiTester.When(LimitedUntil!.Value)}{(LimitEstimated ? " (estimate)" : "")}" : "";
    public string LimitShort => IsLimited ? $"⏳ until {LimitedUntil:HH:mm}" + (LimitedUntil!.Value.Date != DateTime.Today ? $" {LimitedUntil:d MMM}" : "") : "";
    public void RefreshLimit() { OnPropertyChanged(nameof(IsLimited)); OnPropertyChanged(nameof(LimitLabel)); OnPropertyChanged(nameof(LimitShort)); }

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasLastTest))] private string _lastTestLabel = "";
    [ObservableProperty] private bool _lastTestOk;
    public bool HasLastTest => LastTestLabel.Length > 0;
    public void SetLastTest(TestHistoryEntry? h)
    {
        LastTestOk = h?.Ok == true;
        LastTestLabel = h is null ? "" : $"{h.At:d MMM HH:mm} · {h.Method} · {h.Summary.Split('\n')[0]}";
    }

    public bool HasMyKey => MyKey.Trim().Length > 0;
    partial void OnMyKeyChanged(string value) => OnPropertyChanged(nameof(HasMyKey));
    public string NoteShort => Note.Length > 140 ? Note[..137] + "…" : Note;
    partial void OnNoteChanged(string value) => OnPropertyChanged(nameof(NoteShort));

    public bool HasDocsScan => DocsScan is not null;
    public string DocsScanSummary => DocsScan is null ? "" :
        DocsScan.Error ?? $"Read {DocsScan.ScannedAt:d MMM HH:mm} - {DocsScan.Items.Count} finding{(DocsScan.Items.Count == 1 ? "" : "s")}";

    public string StatusLabel => Status.Length == 0 ? "" : LatencyMs is { } ms && Status is "Online" or "Restricted" ? $"{Status} · {ms} ms" : Status;
    partial void OnStatusChanged(string value) => OnPropertyChanged(nameof(StatusLabel));
    partial void OnLatencyMsChanged(int? value) => OnPropertyChanged(nameof(StatusLabel));
}
