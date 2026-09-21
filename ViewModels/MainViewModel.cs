using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using ApiScout.Models;
using ApiScout.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace ApiScout.ViewModels;

public sealed partial class CategoryItem(string name) : ObservableObject
{
    public string Name { get; } = name;
    [ObservableProperty] private int _count;
}

public sealed partial class SourceToggle(SourceInfo info, bool on) : ObservableObject
{
    public SourceInfo Info { get; } = info;
    [ObservableProperty] private bool _isOn = on;
}

/// <summary>One dashboard tile: a number about the whole catalogue that is also a shortcut to the matching filter.</summary>
public sealed partial class DashTile(string id, string title, string colour, bool showBar = false) : ObservableObject
{
    public string Id { get; } = id;
    public string Title { get; } = title;
    /// <summary>"Up", "Accent", "Warn", "Violet" or "Muted" - the window maps it to a theme brush.</summary>
    public string Colour { get; } = colour;
    /// <summary>Tiles without a bar keep its space, so all seven line up.</summary>
    public double BarOpacity { get; } = showBar ? 1 : 0;
    [ObservableProperty] private string _value = "0";
    [ObservableProperty] private string _sub = "";
    [ObservableProperty] private double _percent;
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private string _tip = "";
}

public sealed partial class MainViewModel : ObservableObject
{
    public const string AllCategory = "All APIs";
    public const string FavouritesCategory = "★ Favourites";
    public const string DemoKeyCategory = "🔑 Demo key included";
    public const string NewCategory = "🆕 New (last 14 days)";
    public const string LimitedCategory = "⏳ Rate limited now";
    private const int SpecialCategories = 5;

    /// <summary>Raised when a request was loaded into the Try it card from elsewhere, so the window can scroll to it.</summary>
    public event Action? ShowTestCard;

    private readonly Store _store;
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(2.2) };
    private readonly DispatcherTimer _searchTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private List<ApiRow> _all = [];
    private CancellationTokenSource? _cts;
    private bool _showingHistory; // true while code (not the user) moves the history selection
    private Catalog? _catalog;
    private readonly DispatcherTimer _rescanTimer = new() { Interval = TimeSpan.FromMinutes(30) };

    public MainViewModel(Store store)
    {
        _store = store;
        ApiRow.ShowLogos = store.Settings.ShowLogos;
        LogoService.Folder = Path.Combine(store.Folder, "logos");
        foreach (var s in Sources.All)
            SourceToggles.Add(new SourceToggle(s, store.Settings.Sources.TryGetValue(s.Id, out var on) ? on : s.DefaultOn));
        CustomSources = string.Join(Environment.NewLine, store.Settings.CustomSources);
        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); Toast = ""; };
        _searchTimer.Tick += (_, _) => { _searchTimer.Stop(); ApplyFilter(); };

        if (store.LoadCatalog() is { Entries.Count: > 0 } cached)
        {
            Load(cached);
            StatusText = $"Loaded {cached.Entries.Count:N0} APIs from the last scan ({cached.ScannedAt:d MMM yyyy HH:mm})" +
                         (cached.LastAdded > 0 ? $" - it found {cached.LastAdded:N0} new, see \"{NewCategory}\"." : ". Press Scan to refresh.");
        }
        else StatusText = "Press Scan the internet to find free APIs.";

        // automatic re-scan: shortly after start-up, then checked every half hour while the app stays open
        _rescanTimer.Tick += (_, _) => AutoRescanIfDue();
        var minute = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        minute.Tick += (_, _) =>
        {
            // "until 20:45" expires by itself
            bool expired = false;
            foreach (var r in _all.Where(x => x.LimitedUntil is not null))
            {
                if (r.LimitedUntil > DateTime.Now) { r.RefreshLimit(); continue; }
                r.LimitedUntil = null; // over: forget it, here and on disk
                _store.User.RateLimits.Remove(r.Key);
                expired = true;
            }
            if (expired) { _store.SaveUser(); LimitsChanged(); }
        };
        minute.Start();
        _rescanTimer.Start();
        Application.Current?.Dispatcher.BeginInvoke(AutoRescanIfDue, DispatcherPriority.ApplicationIdle);
    }

    public string[] AutoRescanOptions { get; } = ["Never", "Daily", "Weekly"];

    public bool ShowLogos
    {
        get => _store.Settings.ShowLogos;
        set
        {
            _store.Settings.ShowLogos = ApiRow.ShowLogos = value;
            _store.SaveSettings();
            OnPropertyChanged();
            foreach (var r in _all) r.RefreshLogo();
        }
    }

    public bool ShowDashboard
    {
        get => _store.Settings.ShowDashboard;
        set { _store.Settings.ShowDashboard = value; _store.SaveSettings(); OnPropertyChanged(); }
    }

    // ---------------------------------------------------------------- dashboard

    public IReadOnlyList<DashTile> Dashboard { get; } =
    [
        new("full", "Full free access", "Up", showBar: true),
        new("tier", "Free tier (limited)", "Accent", showBar: true),
        new("trial", "Demo / trial only", "Warn", showBar: true),
        new("unknown", "Not stated", "Muted", showBar: true),
        new("new", "New this week", "Violet"),
        new("limited", "Rate limited now", "Warn"),
        new("links", "Docs links online", "Up", showBar: true),
    ];

    /// <summary>The tiles count the whole catalogue, whatever the filters say.</summary>
    private void RefreshDashboard()
    {
        int total = _all.Count;
        var access = _all.GroupBy(r => r.Access).ToDictionary(g => g.Key, g => g.Count());
        foreach (var (id, level, what) in new[]
                 {
                     ("full", AccessLevel.FullFree, "everything they offer is free"), ("tier", AccessLevel.FreeTier, "a free plan with limits"),
                     ("trial", AccessLevel.TrialOnly, "only a demo or a trial is free"), ("unknown", AccessLevel.Unknown, "the directories do not say - 'Scan docs for Not stated' can find out"),
                 })
        {
            var tile = Tile(id);
            int n = access.GetValueOrDefault(level);
            tile.Value = n.ToString("N0");
            tile.Percent = total == 0 ? 0 : 100.0 * n / total;
            tile.Sub = total == 0 ? "" : $"{tile.Percent:0}%";
            tile.Tip = $"{n:N0} of {total:N0} APIs: {what}. Click to list them, click again to clear.";
        }

        var weekAgo = DateTime.Now.AddDays(-7);
        int newAll = _all.Count(r => r.IsNew), newWeek = _all.Count(r => r.IsNew && r.Entry.FirstSeen >= weekAgo);
        var fresh = Tile("new");
        fresh.Value = newWeek.ToString("N0");
        fresh.Sub = $"{newAll:N0} in 14 days";
        fresh.Tip = $"APIs a scan found for the first time: {newWeek:N0} in the last 7 days, {newAll:N0} in the last 14. Click to open \"{NewCategory}\".";

        int limited = _all.Count(r => r.IsLimited);
        var lim = Tile("limited");
        lim.Value = limited.ToString("N0");
        lim.Sub = limited == 0 ? "all clear" : "click to see when";
        lim.Tip = "APIs that answered your tests with 'too many requests' and have not reset yet. Click to list them.";

        int looked = _all.Count(r => r.Status.Length > 0), online = _all.Count(r => r.Status == "Online"), restricted = _all.Count(r => r.Status == "Restricted");
        var links = Tile("links");
        links.Value = looked == 0 ? "-" : $"{100.0 * online / looked:0}%";
        links.Percent = looked == 0 ? 0 : 100.0 * online / looked;
        links.Sub = looked == 0 ? "not checked yet" : $"of {looked:N0} checked";
        links.Tip = looked == 0 ? "Click to run 'Check links' on the listed APIs."
            : $"{online:N0} online, {restricted:N0} restricted (want a key or block bots), {looked - online - restricted:N0} slow or down - of the {looked:N0} checked this session. Click for 'Online only'.";
    }

    private DashTile Tile(string id) => Dashboard.First(t => t.Id == id);

    private static readonly Dictionary<string, string> TileAccess = new() { ["full"] = "Full free access", ["tier"] = "Free tier (limited)", ["trial"] = "Demo / trial only", ["unknown"] = "Not stated" };

    [RelayCommand]
    private void DashboardTile(string? id)
    {
        if (id is null || _all.Count == 0) return;
        ShowShortlist = false;
        if (TileAccess.TryGetValue(id, out var label)) AccessFilter = AccessFilter == label ? "Any free access" : label;
        else if (id is "new" or "limited")
        {
            var name = id == "new" ? NewCategory : LimitedCategory;
            SelectedCategory = Categories.FirstOrDefault(c => c.Name == (SelectedCategory?.Name == name ? AllCategory : name));
        }
        else if (id == "links")
        {
            if (_all.Any(r => r.Status.Length > 0)) OnlineOnly = !OnlineOnly;
            else CheckLinksCommand.Execute(null);
        }
    }

    private void MarkActiveTiles()
    {
        foreach (var t in Dashboard)
            t.IsActive = t.Id switch
            {
                "new" => SelectedCategory?.Name == NewCategory,
                "limited" => SelectedCategory?.Name == LimitedCategory,
                "links" => OnlineOnly,
                _ => TileAccess[t.Id] == AccessFilter,
            };
    }

    public string AutoRescan
    {
        get => _store.Settings.AutoRescan;
        set
        {
            _store.Settings.AutoRescan = value;
            _store.SaveSettings();
            OnPropertyChanged();
            if (BackgroundScan) BackgroundScan = value != "Never"; // keep the Windows task in step (or remove it)
            AutoRescanIfDue();
        }
    }

    /// <summary>Tick box "also when ApiScout is closed": registers / removes the Windows scheduled task.</summary>
    public bool BackgroundScan
    {
        get => _store.Settings.BackgroundScan;
        set => _ = SetBackgroundScanAsync(value);
    }

    /// <summary>schtasks.exe can take seconds (or hang behind antivirus), so it runs off the UI thread; the tick box settles when it answers.</summary>
    private async Task SetBackgroundScanAsync(bool value)
    {
        bool ok; string message;
        if (value && AutoRescan == "Never") (ok, message) = (false, "Choose Daily or Weekly first.");
        else
        {
            bool daily = AutoRescan == "Daily";
            StatusText = "Asking Windows Task Scheduler…";
            (ok, message) = await Task.Run(() => value ? ScheduledScan.Register(daily) : ScheduledScan.Unregister());
        }
        _store.Settings.BackgroundScan = value && ok;
        _store.SaveSettings();
        StatusText = message;
        OnPropertyChanged(nameof(BackgroundScan));
    }

    public static bool RescanDue(string setting, DateTime? lastScan, DateTime now) => lastScan is { } at && setting switch
    {
        "Daily" => now - at >= TimeSpan.FromDays(1),
        "Weekly" => now - at >= TimeSpan.FromDays(7),
        _ => false,
    };

    private void AutoRescanIfDue()
    {
        if (IsBusy || !RescanDue(_store.Settings.AutoRescan, _catalog?.ScannedAt, DateTime.Now)) return;
        // offline, or stopped with Esc: try again in a few hours, not every half hour; and never pull the rows away under a running test
        if (DateTime.Now - _lastAutoAttempt < TimeSpan.FromHours(6) || _all.Any(r => r.IsTesting)) return;
        _lastAutoAttempt = DateTime.Now;
        _autoScan = true;
        ScanCommand.Execute(null);
    }

    private bool _autoScan;
    private DateTime _lastAutoAttempt = DateTime.MinValue;

    /// <summary>The ticked sources plus the user's own list URLs.</summary>
    public static List<(string Id, string Name)> EnabledSources(Settings settings)
    {
        var list = Sources.All.Where(s => settings.Sources.TryGetValue(s.Id, out var on) ? on : s.DefaultOn).Select(s => (s.Id, s.Name)).ToList();
        foreach (var url in settings.CustomSources)
            list.Add((url, Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host + u.AbsolutePath : url));
        return list;
    }

    public ObservableCollection<SourceToggle> SourceToggles { get; } = [];
    public ObservableCollection<CategoryItem> Categories { get; } = [];
    public const string AnyTag = "Any tag";
    public ObservableCollection<string> TagFilters { get; } = [AnyTag];
    [ObservableProperty] private string? _tagFilter = AnyTag;
    [ObservableProperty] private bool _hasAnyTags;
    partial void OnTagFilterChanged(string? value) => ApplyFilter();

    public string[] AccessFilters { get; } = ["Any free access", "Full free access", "Free tier (limited)", "Demo / trial only", "Not stated"];
    public string[] TestMethods => ApiTester.Methods;
    public string[] AuthFilters { get; } = ["Any auth", "No key needed", "Demo key included", "Key optional or none", "API key", "OAuth", "Unknown"];

    [ObservableProperty] private IReadOnlyList<ApiRow> _rows = [];
    [ObservableProperty] private ApiRow? _selected;
    [ObservableProperty] private CategoryItem? _selectedCategory;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _authFilter = "Any auth";
    [ObservableProperty] private string _accessFilter = "Any free access";
    [ObservableProperty] private bool _httpsOnly;
    [ObservableProperty] private bool _corsOnly;
    [ObservableProperty] private bool _onlineOnly;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _countText = "";
    [ObservableProperty] private string _toast = "";
    [ObservableProperty] private string _customSources = "";
    [ObservableProperty] private bool _showMyKey;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsIdle))] private bool _isBusy;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private bool _hasSelection;

    public bool IsIdle => !IsBusy;
    public bool IsEmpty => _all.Count == 0;

    partial void OnSearchTextChanged(string value) { _searchTimer.Stop(); _searchTimer.Start(); }
    partial void OnAuthFilterChanged(string value) => ApplyFilter();
    partial void OnAccessFilterChanged(string value) => ApplyFilter();
    partial void OnHttpsOnlyChanged(bool value) => ApplyFilter();
    partial void OnCorsOnlyChanged(bool value) => ApplyFilter();
    partial void OnOnlineOnlyChanged(bool value) => ApplyFilter();
    partial void OnSelectedCategoryChanged(CategoryItem? value) => ApplyFilter(countCategories: false);

    partial void OnSelectedChanged(ApiRow? oldValue, ApiRow? newValue)
    {
        if (oldValue is not null) { oldValue.PropertyChanged -= SelectedRowChanged; SaveTypedKey(oldValue); }
        HasSelection = newValue is not null;
        ShowMyKey = false;
        if (newValue is null) return;
        EnsureRowLoaded(newValue);
        newValue.RefreshLimit();
        newValue.PropertyChanged += SelectedRowChanged;
    }

    /// <summary>The key box writes to the row as you type and the rest of the app already treats that as "your key", so it is stored when you leave the row or close the window - not only by the Save button.</summary>
    private void SaveTypedKey(ApiRow row)
    {
        if (!row.KeyLoaded || row.MyKey.Trim() == (_store.GetMyKey(row.Key) ?? "")) return;
        _store.SetMyKey(row.Key, row.MyKey);
    }

    /// <summary>Raised before rows are replaced or re-filtered: the window commits the text box being typed in (notes and tags save on leaving the box).</summary>
    public event Action? CommitEdits;

    /// <summary>Window closing.</summary>
    public void Flush()
    {
        CommitEdits?.Invoke();
        if (Selected is { } r) SaveTypedKey(r);
    }

    /// <summary>Badge colours come from converters that hand out the brush of the moment, so after a theme switch the bound items are asked again.</summary>
    public void ThemeChanged()
    {
        CommitEdits?.Invoke();
        var keep = Selected;
        Selected = null;
        Selected = keep;
        if (ShowShortlist) RefreshShortlist();
    }

    /// <summary>Pulls a row's saved key, test request and history out of the store the first time they are needed.</summary>
    private void EnsureRowLoaded(ApiRow row)
    {
        if (!row.KeyLoaded) { row.KeyLoaded = true; if (row.MyKey.Length == 0) row.MyKey = _store.GetMyKey(row.Key) ?? ""; }
        if (row.TestUrl.Length == 0)
        {
            var saved = _store.GetTestRequest(row.Key);
            row.TestUrl = saved?.Url ?? row.DefaultTestUrl;
            row.TestHeader = saved?.Headers ?? row.DefaultTestHeader;
            row.TestMethod = saved?.Method ?? "GET";
            row.TestBody = saved?.Body ?? "";
        }
        if (!row.HistoryLoaded)
        {
            row.HistoryLoaded = true;
            foreach (var h in _store.TestHistory.GetValueOrDefault(row.Key) ?? []) row.History.Add(h);
            row.HistoryChanged();
        }
    }

    /// <summary>Remembers "rate limited until…" (or forgets it after a success). Also used by the Compare window.</summary>
    public void RecordOutcome(ApiRow row, ApiTestResult result)
    {
        if (result.Rate is { Limited: true, ResetAt: { } until })
        {
            row.LimitEstimated = result.Rate.Estimated;
            row.LimitedUntil = until;
            _store.User.RateLimits[row.Key] = new RateLimitNote { Until = until, Estimated = result.Rate.Estimated };
            _store.SaveUser();
        }
        else if (result.Ok && _store.User.RateLimits.Remove(row.Key))
        {
            row.LimitedUntil = null;
            _store.SaveUser();
        }
        else return;
        LimitsChanged();
    }

    /// <summary>A rate limit started or ended: tile, sidebar count and - only if that is the list being shown - the list.</summary>
    private void LimitsChanged()
    {
        RefreshDashboard();
        if (SelectedCategory?.Name == LimitedCategory) ApplyFilter();
        else CountCategories(Filtered());
    }

    private void SelectedRowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not ApiRow row) return;
        if (e.PropertyName == nameof(ApiRow.Note))
        {
            if (row.Note.Length == 0) _store.User.Notes.Remove(row.Key); else _store.User.Notes[row.Key] = row.Note;
            _store.SaveUser();
        }
        else if (e.PropertyName == nameof(ApiRow.TagsText)) SaveTags(row);
        else if (e.PropertyName == nameof(ApiRow.SelectedHistory) && !_showingHistory && row.SelectedHistory is { } entry)
        {
            // the user picked an earlier result: show it in the response box
            row.TestOk = entry.Ok;
            row.TestSummary = (entry == row.History.FirstOrDefault() ? "" : $"Earlier result from {entry.At:d MMM HH:mm:ss}\n") + entry.Summary;
            row.TestResponse = row.TestRaw = entry.Response;
            row.TestClasses = "";
        }
    }

    // ---------------------------------------------------------------- shortlist page

    [ObservableProperty] private bool _showShortlist;
    [ObservableProperty] private IReadOnlyList<ApiRow> _shortlistRows = [];
    [ObservableProperty] private string _shortlistSummary = "";
    /// <summary>The card the keyboard is on. Ctrl+T / Ctrl+D / Ctrl+K / Ctrl+U act on it while the page is open.</summary>
    [ObservableProperty] private ApiRow? _shortlistSelected;
    private ApiRow? Target => ShowShortlist ? ShortlistSelected : Selected;

    /// <summary>Raised when the list should scroll to the selected row (coming back from the shortlist).</summary>
    public event Action? ScrollToSelected;

    partial void OnShowShortlistChanged(bool value) { if (value) RefreshShortlist(); }

    private void RefreshShortlist()
    {
        var rows = _all.Where(r => r.IsFavourite || r.HasTags).OrderByDescending(r => r.IsFavourite).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var r in rows) { EnsureRowLoaded(r); r.RefreshLimit(); }
        var keep = ShortlistSelected;
        int at = keep is null ? 0 : Math.Max(0, ShortlistRows.ToList().IndexOf(keep));
        ShortlistRows = rows;
        // a card that just left the page hands the selection to its neighbour
        ShortlistSelected = keep is not null && rows.Contains(keep) ? keep : rows.ElementAtOrDefault(Math.Min(at, rows.Count - 1));
        ShortlistSummary = rows.Count == 0
            ? "Nothing here yet. Star an API (Ctrl+D) or give it a tag and it shows up on this page with its key and last test result."
            : $"{rows.Count:N0} API{(rows.Count == 1 ? "" : "s")}: {rows.Count(r => r.IsFavourite):N0} favourite(s), {rows.Count(r => r.HasTags):N0} tagged, {rows.Count(r => r.HasMyKey):N0} with your own key saved.";
    }

    [RelayCommand(AllowConcurrentExecutions = true)] // each row guards itself with IsTesting
    private async Task TestRowAsync(ApiRow? row)
    {
        if (row is null) return;
        ShortlistSelected = row;
        row.LastTestLabel = "Testing…";
        await RunTestAsync(row);
    }

    [RelayCommand]
    private void CopyRowKey(ApiRow? row)
    {
        if (row is null) return;
        if (ShowShortlist) ShortlistSelected = row;
        EnsureRowLoaded(row);
        if (row.HasMyKey) CopyText(row.MyKey.Trim(), "Your key");
        else if (row.HasDemoKey) CopyText(row.DemoKey!, "Demo key");
        else ShowToast(row.KeylessWorks ? "No key needed for this one" : "No key saved for this API yet");
    }

    [RelayCommand]
    private void OpenRow(ApiRow? row) { if (row is not null) ShortlistSelected = row; OpenUrl(row?.Url); }

    [RelayCommand]
    private void ToggleRowFavourite(ApiRow? row)
    {
        if (row is null) return;
        ShortlistSelected = row;
        row.IsFavourite = !row.IsFavourite;
        if (row.IsFavourite) _store.User.Favourites.Add(row.Key); else _store.User.Favourites.Remove(row.Key);
        _store.SaveUser();
        ApplyFilter();
        RefreshShortlist();
    }

    // ---------------------------------------------------------------- what changed

    public IReadOnlyList<ScanReport> ScanReports => _store.ScanReports;

    public bool HasApi(string key) => _all.Any(r => r.Key == key);

    /// <summary>Selects an API by key in the main list (from the What changed window).</summary>
    public void ShowApi(string key)
    {
        if (_all.FirstOrDefault(r => r.Key == key) is { } row) ShowRow(row);
    }

    /// <summary>"Details": back to the list with this API selected.</summary>
    [RelayCommand]
    private void ShowRow(ApiRow? row)
    {
        if (row is null) return;
        ShowShortlist = false;
        if (!Rows.Contains(row)) ClearFilters();
        Selected = row;
        ScrollToSelected?.Invoke();
    }

    // ---------------------------------------------------------------- about / moving data

    public string DataSummary =>
        $"{_all.Count:N0} APIs from the last scan ({(_catalog is null ? "never" : _catalog.ScannedAt.ToString("d MMM yyyy HH:mm"))})  ·  {_store.User.Favourites.Count:N0} favourites  ·  " +
        $"{_store.User.Tags.Count:N0} tagged  ·  {_store.User.Notes.Count:N0} notes  ·  {_store.User.MyKeys.Count:N0} saved keys  ·  " +
        $"re-scan {AutoRescan.ToLowerInvariant()}{(BackgroundScan ? " (also when closed)" : "")}";

    /// <summary>After an import: rebuild the rows so they pick up the merged favourites, tags, notes and keys.</summary>
    public void ReloadUserData()
    {
        if (_catalog is not null) Load(_catalog);
        if (ShowShortlist) RefreshShortlist();
        OnPropertyChanged(nameof(DataSummary));
    }

    // ---------------------------------------------------------------- tags

    private void SaveTags(ApiRow row)
    {
        var tags = ApiRow.ParseTags(row.TagsText);
        var tidy = string.Join(", ", tags);
        if (tidy != row.TagsText) { row.TagsText = tidy; return; } // comes straight back here with the tidy text
        if (tags.Count == 0) _store.User.Tags.Remove(row.Key); else _store.User.Tags[row.Key] = tags;
        _store.SaveUser();
        RefreshTagFilters();
        if (TagFilter is not (null or AnyTag)) ApplyFilter();
        if (ShowShortlist) RefreshShortlist();
    }

    /// <summary>Adds one tag to every given row (the grid's multi-selection).</summary>
    public void AddTag(IReadOnlyList<ApiRow> rows, string tag)
    {
        tag = tag.Trim().Trim(',', ';');
        if (tag.Length == 0 || rows.Count == 0) return;
        foreach (var r in rows)
        {
            var tags = ApiRow.ParseTags(r.TagsText + ", " + tag);
            if (r != Selected) { _store.User.Tags[r.Key] = tags; }
            r.TagsText = string.Join(", ", tags); // the selected row saves itself through SelectedRowChanged
        }
        _store.SaveUser();
        RefreshTagFilters();
        ShowToast($"🏷 {tag} added to {rows.Count} API{(rows.Count == 1 ? "" : "s")}");
    }

    private void RefreshTagFilters()
    {
        var wanted = _store.User.Tags.Values.SelectMany(t => t).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList();
        // edit in place so the ComboBox keeps its selection
        for (int i = TagFilters.Count - 1; i >= 1; i--)
            if (!wanted.Contains(TagFilters[i], StringComparer.OrdinalIgnoreCase))
            {
                if (TagFilter == TagFilters[i]) TagFilter = AnyTag;
                TagFilters.RemoveAt(i);
            }
        foreach (var t in wanted)
            if (!TagFilters.Contains(t, StringComparer.OrdinalIgnoreCase))
            {
                int at = 1;
                while (at < TagFilters.Count && string.Compare(TagFilters[at], t, StringComparison.OrdinalIgnoreCase) < 0) at++;
                TagFilters.Insert(at, t);
            }
        HasAnyTags = TagFilters.Count > 1;
    }

    // ---------------------------------------------------------------- scanning

    [RelayCommand]
    private async Task ScanAsync()
    {
        if (IsBusy) return;
        SaveSourceSettings();
        var sources = EnabledSources(_store.Settings);
        bool auto = _autoScan;
        _autoScan = false;
        if (sources.Count == 0) { StatusText = "Tick at least one source first (Sources button)."; return; }

        IsBusy = true;
        Progress = 0;
        _cts = new CancellationTokenSource();
        var progress = new Progress<ScanProgress>(p =>
        {
            StatusText = p.Message;
            Progress = p.Total == 0 ? 0 : 100.0 * p.Done / p.Total;
        });
        try
        {
            var token = _cts.Token;
            var outcome = await Task.Run(() => Scanner.RunAsync(sources, progress, token), token);
            foreach (var n in outcome.Notes) _store.Log("Scan - " + n);
            if (outcome.Catalog.Entries.Count == 0)
            {
                StatusText = "The scan found nothing - are you online? " + string.Join("; ", outcome.Notes);
                return;
            }
            int before = _all.Count;
            int kept = outcome.FailedSources > 0 ? Scanner.KeepUnreadable(outcome.Catalog, _catalog) : 0;
            var (added, removed) = Scanner.StampFirstSeen(outcome.Catalog, _catalog);
            ScanReport? report = null;
            if (_catalog is { Entries.Count: > 0 } previous)
            {
                report = ChangeLog.Build(outcome.Catalog, previous, _store.DocsScans, auto ? "Automatic re-scan" : "Scan", outcome.FailedSources);
                _store.AddScanReport(report);
            }
            _store.SaveCatalog(outcome.Catalog);
            Load(outcome.Catalog);
            if (report is not null) OnPropertyChanged(nameof(ScanReports)); // after Load, so an open What changed window finds the new rows
            var failed = outcome.Notes.Where(n => n.Contains("failed")).ToList();
            StatusText = $"{(auto ? "Automatic re-scan" : "Scan")} finished: {_all.Count:N0} unique APIs in {Categories.Count - SpecialCategories} categories" +
                         (before > 0 ? $" - {added:N0} new, {removed:N0} gone, {report?.Changed.Count ?? 0:N0} changed since the last scan{(report is { IsEmpty: false } ? " (see What changed, Ctrl+H)" : "")}" : "") +
                         (failed.Count > 0 ? $". {failed.Count} source(s) failed{(kept > 0 ? $" ({kept:N0} APIs kept from the last scan)" : "")}: {string.Join("; ", failed)}" : ".");
            if (before > 0 && added > 0) ShowToast($"🆕 {added:N0} new API{(added == 1 ? "" : "s")} since the last scan");
        }
        catch (OperationCanceledException) { StatusText = "Scan cancelled."; }
        catch (Exception ex) { StatusText = "Scan failed: " + ex.Message; _store.Log("Scan failed: " + ex); }
        finally { IsBusy = false; Progress = 0; _cts?.Dispose(); _cts = null; }
    }

    [RelayCommand]
    private void Cancel()
    {
        // Esc: stop what is running, otherwise leave the shortlist page
        if (_cts is not null) _cts.Cancel();
        else if (ShowShortlist) ShowShortlist = false;
    }

    private void Load(Catalog catalog)
    {
        CommitEdits?.Invoke(); // a note being typed belongs to a row that is about to be replaced
        var keep = Selected?.Key;
        var old = _all.ToDictionary(r => r.Key);
        _catalog = catalog;
        _all = [.. catalog.Entries.Select(e => new ApiRow(e, catalog.BaselineAt))];
        foreach (var r in _all)
        {
            r.IsFavourite = _store.User.Favourites.Contains(r.Key);
            if (_store.User.Notes.TryGetValue(r.Key, out var note)) r.Note = note;
            if (_store.User.Tags.TryGetValue(r.Key, out var tags)) r.TagsText = string.Join(", ", tags);
            if (_store.User.RateLimits.TryGetValue(r.Key, out var limit) && limit.Until > DateTime.Now) { r.LimitEstimated = limit.Estimated; r.LimitedUntil = limit.Until; }
            if (_store.TestHistory.TryGetValue(r.Key, out var past) && past.Count > 0) r.SetLastTest(past[0]);
            if (_store.DocsScans.TryGetValue(r.Key, out var scan)) r.DocsScan = scan;
            if (old.TryGetValue(r.Key, out var was)) r.CopySessionFrom(was); // link status, and whatever is typed or shown in Try it
        }

        var selectedName = SelectedCategory?.Name ?? AllCategory;
        Categories.Clear();
        Categories.Add(new CategoryItem(AllCategory));
        Categories.Add(new CategoryItem(FavouritesCategory));
        Categories.Add(new CategoryItem(DemoKeyCategory));
        Categories.Add(new CategoryItem(NewCategory));
        Categories.Add(new CategoryItem(LimitedCategory));
        foreach (var name in _all.Select(r => r.Category).Distinct().OrderBy(n => n == "Other").ThenBy(n => n, StringComparer.OrdinalIgnoreCase))
            Categories.Add(new CategoryItem(name));
        SelectedCategory = Categories.FirstOrDefault(c => c.Name == selectedName) ?? Categories[0];
        RefreshTagFilters();
        OnPropertyChanged(nameof(IsEmpty));
        RefreshDashboard();
        ApplyFilter();
        if (keep is not null) Selected = Rows.FirstOrDefault(r => r.Key == keep);
        // the cards must not keep pointing at the rows that were just replaced
        if (ShowShortlist) RefreshShortlist(); else { ShortlistRows = []; ShortlistSelected = null; }
    }

    // ---------------------------------------------------------------- filtering

    private void ApplyFilter(bool countCategories = true)
    {
        var pre = Filtered();
        if (countCategories) CountCategories(pre);
        ShowCategory(pre);
    }

    /// <summary>Everything the filters let through, before the category is applied.</summary>
    private List<ApiRow> Filtered()
    {
        var words = SearchText.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return _all.Where(r =>
            (!HttpsOnly || r.Entry.Https == true) &&
            (!CorsOnly || r.Cors == "Yes") &&
            (!OnlineOnly || r.Status == "Online") &&
            MatchesAuth(r) &&
            (AccessFilter == "Any free access" || r.AccessLabel == AccessFilter) &&
            (TagFilter is null or AnyTag || r.Tags.Contains(TagFilter, StringComparer.OrdinalIgnoreCase)) &&
            words.All(w => r.SearchText.Contains(w) || r.TagsText.Contains(w, StringComparison.OrdinalIgnoreCase))).ToList();
    }

    private void CountCategories(List<ApiRow> pre)
    {
        {
            var counts = pre.GroupBy(r => r.Category).ToDictionary(g => g.Key, g => g.Count());
            foreach (var c in Categories)
                c.Count = c.Name switch
                {
                    AllCategory => pre.Count,
                    FavouritesCategory => pre.Count(r => r.IsFavourite),
                    DemoKeyCategory => pre.Count(r => r.HasDemoKey),
                    NewCategory => pre.Count(r => r.IsNew),
                    LimitedCategory => pre.Count(r => r.IsLimited),
                    _ => counts.GetValueOrDefault(c.Name),
                };
        }
    }

    private void ShowCategory(List<ApiRow> pre)
    {
        var cat = SelectedCategory?.Name ?? AllCategory;
        var keep = Selected;
        Rows = cat switch
        {
            AllCategory => pre,
            FavouritesCategory => [.. pre.Where(r => r.IsFavourite)],
            DemoKeyCategory => [.. pre.Where(r => r.HasDemoKey)],
            NewCategory => [.. pre.Where(r => r.IsNew)],
            LimitedCategory => [.. pre.Where(r => r.IsLimited)],
            _ => [.. pre.Where(r => r.Category == cat)],
        };
        MarkActiveTiles();
        if (keep is not null && Rows.Contains(keep)) Selected = keep;
        CountText = $"{Rows.Count:N0} of {_all.Count:N0} APIs";
    }

    private bool MatchesAuth(ApiRow r) => AuthFilter switch
    {
        "No key needed" => r.Entry.Auth == AuthKind.None,
        "Demo key included" => r.HasDemoKey,
        "Key optional or none" => r.KeylessWorks || r.HasDemoKey,
        "API key" => r.Entry.Auth == AuthKind.ApiKey,
        "OAuth" => r.Entry.Auth == AuthKind.OAuth,
        "Unknown" => r.Entry.Auth is AuthKind.Unknown or AuthKind.Other,
        _ => true,
    };

    [RelayCommand]
    private void ClearFilters()
    {
        SearchText = ""; AuthFilter = "Any auth"; AccessFilter = "Any free access"; TagFilter = AnyTag; HttpsOnly = CorsOnly = OnlineOnly = false;
        SelectedCategory = Categories.FirstOrDefault();
    }

    // ---------------------------------------------------------------- copy

    [RelayCommand]
    private void Copy(string? what)
    {
        if (Target is not { } r) return;
        if (what == "key") { CopyRowKey(r); return; } // Ctrl+K: your own key if one is saved, otherwise the demo key
        var (text, label) = what switch
        {
            "name" => (r.Name, "Name"),
            "url" => (r.Url, "Docs URL"),
            "spec" => (r.SpecUrl ?? "", "OpenAPI spec URL"),
            "demokey" => (r.DemoKey ?? "", "Demo key"),
            "usage" => (r.KeyUsage ?? "", "Key usage"),
            "signup" => (r.SignupUrl ?? "", "Sign-up link"),
            "example" => (r.Example ?? "", "Example request"),
            "howto" => (r.HowTo, "How-to"),
            "mykey" => (r.MyKey, "Your key"),
            "response" => (r.TestResponse, "Response"),
            "classes" => (r.TestClasses, "C# code"),
            "testcurl" => (ApiTester.ToCurl(new ApiTestRequest(r.TestMethod, r.TestUrl, r.TestHeader, r.TestBody)), "cURL command"),
            "markdown" => (Exporter.Markdown(r), "Markdown"),
            "json" => (Exporter.JsonOne(r), "JSON"),
            "curl" => (Exporter.Curl(r), "cURL command"),
            "csharp" => (Exporter.CSharp(r), "C# snippet"),
            _ => (Exporter.Text(r), "Details"),
        };
        CopyText(text, label);
    }

    [RelayCommand]
    private void CopyValue(string? value) => CopyText(value ?? "", "Value");

    [RelayCommand]
    private void CopyList(string? format)
    {
        if (Rows.Count == 0) return;
        var text = format switch
        {
            "csv" => Exporter.Csv(Rows),
            "json" => Exporter.JsonMany(Rows),
            "urls" => Exporter.Urls(Rows),
            _ => Exporter.MarkdownTable(Rows),
        };
        CopyText(text, $"{Rows.Count:N0} APIs");
    }

    /// <summary>Copies the rows selected in the grid (Ctrl+C) as tab-separated text that pastes into Excel.</summary>
    public void CopyRows(IReadOnlyList<ApiRow> rows)
    {
        if (rows.Count == 0) return;
        if (rows.Count == 1) { CopyText(Exporter.Text(rows[0]), "Details"); return; }
        var sb = new StringBuilder("Name\tCategory\tAuth\tDemo key\tGet a key\tDocs URL\tDescription\r\n");
        foreach (var r in rows)
            sb.Append(string.Join('\t', new[] { r.Name, r.Category, r.AuthLabel, r.DemoKey, r.SignupUrl, r.Url, r.Description }.Select(Exporter.Cell))).Append("\r\n");
        CopyText(sb.ToString(), $"{rows.Count} rows");
    }

    public void CopyText(string text, string label)
    {
        if (string.IsNullOrEmpty(text)) { ShowToast("Nothing to copy"); return; }
        for (int attempt = 0; ; attempt++)
        {
            try { Clipboard.SetDataObject(text, true); break; }
            catch (COMException) when (attempt < 1) { Thread.Sleep(60); } // another app has the clipboard open (WPF has already retried for about a second)
            catch (COMException) { ShowToast("Clipboard is busy - try again"); return; }
        }
        ShowToast($"✓ {label} copied");
    }

    public void Notify(string message) => ShowToast(message);

    private void ShowToast(string message)
    {
        Toast = message;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    [RelayCommand]
    private void Export()
    {
        if (Rows.Count == 0) return;
        var dlg = new SaveFileDialog
        {
            Title = "Export the listed APIs",
            FileName = $"free-apis-{DateTime.Now:yyyy-MM-dd}",
            Filter = "CSV (Excel)|*.csv|Markdown table|*.md|JSON|*.json",
        };
        if (dlg.ShowDialog() != true) return;
        var text = Path.GetExtension(dlg.FileName).ToLowerInvariant() switch
        {
            ".md" => Exporter.MarkdownTable(Rows),
            ".json" => Exporter.JsonMany(Rows),
            _ => Exporter.Csv(Rows),
        };
        try
        {
            File.WriteAllText(dlg.FileName, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            ShowToast($"✓ Exported {Rows.Count:N0} APIs");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { StatusText = "Export failed: " + ex.Message; }
    }

    // ---------------------------------------------------------------- per-API actions

    [RelayCommand]
    private void Open(string? what)
    {
        if (Target is not { } r) return;
        OpenUrl(what switch { "signup" => r.SignupUrl, "spec" => r.SpecUrl, "example" => r.Example, _ => r.Url });
    }

    [RelayCommand]
    private void OpenUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme is not ("http" or "https")) return;
        try { Process.Start(new ProcessStartInfo(u.ToString()) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { StatusText = "Could not open the browser: " + ex.Message; }
    }

    [RelayCommand]
    private void ToggleFavourite()
    {
        if (ShowShortlist) { ToggleRowFavourite(ShortlistSelected); return; }
        if (Selected is not { } r) return;
        CommitEdits?.Invoke(); // in the Favourites list the row may now leave, taking a half-typed note with it
        r.IsFavourite = !r.IsFavourite;
        if (r.IsFavourite) _store.User.Favourites.Add(r.Key); else _store.User.Favourites.Remove(r.Key);
        _store.SaveUser();
        // only the Favourites list itself changes; everywhere else the counts are enough (and the list keeps its scroll position)
        if (SelectedCategory?.Name == FavouritesCategory) ApplyFilter(); else CountCategories(Filtered());
    }

    [RelayCommand]
    private void SaveMyKey()
    {
        if (Selected is not { } r) return;
        _store.SetMyKey(r.Key, r.MyKey);
        ShowToast(r.MyKey.Trim().Length == 0 ? "Your key was removed" : "✓ Your key was saved (encrypted)");
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task ScanDocsAsync()
    {
        if (Selected is not { } r || r.IsScanningDocs) return;
        r.IsScanningDocs = true;
        try
        {
            var entry = r.Entry;
            r.DocsScan = await Task.Run(() => DocsScanner.ScanAsync(entry, CancellationToken.None));
            _store.DocsScans[r.Key] = r.DocsScan;
            _store.SaveDocsScans();
        }
        catch (Exception ex) { r.DocsScan = new DocsScanResult { ScannedAt = DateTime.Now, PageUrl = r.Url, Error = ex.Message }; }
        finally { r.IsScanningDocs = false; RefreshDashboard(); }
    }

    /// <summary>"Test this API": sends the request in the box; {key} stands for the key saved under My key.</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task TestApiAsync() => ShowShortlist ? TestRowAsync(ShortlistSelected) : Selected is { } r ? RunTestAsync(r) : Task.CompletedTask;

    private async Task RunTestAsync(ApiRow r)
    {
        if (r.IsTesting) return;
        EnsureRowLoaded(r);
        r.IsTesting = true;
        r.TestSummary = "Sending…";
        try
        {
            string Fill(string s) => s.Replace("{key}", r.MyKey.Trim(), StringComparison.OrdinalIgnoreCase);
            var body = r.ShowTestBody ? r.TestBody : "";
            if ((r.TestUrl + r.TestHeader + body).Contains("{key}", StringComparison.OrdinalIgnoreCase) && r.MyKey.Trim().Length == 0)
            {
                r.TestOk = false; r.TestResponse = "";
                r.LastTestOk = false; r.LastTestLabel = "Needs your own key - save one under My key first";
                r.TestSummary = "The request uses {key} but no key is saved under 'My key' below. Save one first.";
                return;
            }
            if (r.TestUrl.Trim() != r.DefaultTestUrl || r.TestHeader.Trim() != r.DefaultTestHeader || r.TestMethod != "GET" || r.TestBody.Trim().Length > 0)
                _store.SetTestRequest(r.Key, new ApiTestRequest(r.TestMethod, r.TestUrl.Trim(), r.TestHeader.Trim(), r.TestBody.Trim()));
            else if (_store.User.TestRequests.Remove(r.Key)) _store.SaveUser(); // back to the suggested request by hand: forget the saved one
            var request = new ApiTestRequest(r.TestMethod, Fill(r.TestUrl), Fill(r.TestHeader), Fill(body));
            var result = await Task.Run(() => ApiTester.SendAsync(request, CancellationToken.None));
            r.TestOk = result.Ok;
            r.TestSummary = result.Summary;
            r.TestResponse = result.Body;
            r.TestRaw = result.Raw;
            r.TestClasses = "";
            RecordOutcome(r, result);

            // history keeps the request as typed, so {key} stays a placeholder on disk too
            var entry = _store.AddHistory(r.Key, new TestHistoryEntry
            {
                At = DateTime.Now, Method = r.TestMethod, Url = r.TestUrl.Trim(), Headers = r.TestHeader.Trim(), Body = body.Trim(),
                Ok = result.Ok, Summary = result.Summary, Response = result.Body,
            });
            r.History.Insert(0, entry);
            while (r.History.Count > Store.HistoryPerApi) r.History.RemoveAt(r.History.Count - 1);
            r.HistoryChanged();
            r.SetLastTest(entry);
            _showingHistory = true;
            r.SelectedHistory = entry;
            _showingHistory = false;
        }
        catch (Exception ex) { r.TestOk = r.LastTestOk = false; r.TestSummary = r.LastTestLabel = "Request failed: " + ex.Message; r.TestResponse = ""; }
        finally { r.IsTesting = false; }
    }

    /// <summary>Line diff: the picked earlier result against the latest one (or the latest against the one before it).</summary>
    [RelayCommand]
    private void CompareHistory()
    {
        if (Selected is not { } r) return;
        if (r.History.Count < 2) { ShowToast("Run the test at least twice to compare"); return; }
        var newer = r.History[0];
        var older = r.SelectedHistory is { } picked && picked != newer ? picked : r.History[1];
        var diff = LineDiff.Compare(older.Response, newer.Response);
        r.TestClasses = "";
        r.TestOk = true;
        r.TestSummary = $"Comparing {older.At:d MMM HH:mm:ss} (- lines) with the latest, {newer.At:HH:mm:ss} (+ lines)\n" +
                        (diff.Added + diff.Removed == 0 ? "The two responses are identical." : $"{diff.Added:N0} line(s) added, {diff.Removed:N0} removed.");
        r.TestResponse = diff.Text;
    }

    /// <summary>"Test" beside an endpoint the docs scan found: load it into the Try it card; plain GETs are sent straight away.</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task UseEndpointAsync(FoundItem? item)
    {
        if (Selected is not { } r || item is null) return;
        r.TestMethod = ApiTester.Methods.Contains(item.Method) ? item.Method : "GET";
        r.TestUrl = item.Value;
        r.TestBody = "";
        ShowTestCard?.Invoke();
        if (ApiTester.HasBody(r.TestMethod)) ShowToast($"{r.TestMethod} request loaded - add a body, then press Test this API");
        else if (item.Value.Contains('{') && !item.Value.Contains("{key}", StringComparison.OrdinalIgnoreCase)) ShowToast("Request loaded - fill in the {…} parts, then press Test this API");
        else await TestApiAsync();
    }

    [RelayCommand]
    private void ReuseRequest()
    {
        if (Selected is not { } r || r.SelectedHistory is not { } h) return;
        r.TestMethod = h.Method; r.TestUrl = h.Url; r.TestHeader = h.Headers; r.TestBody = h.Body;
        ShowToast("Request restored - press Test this API");
    }

    [RelayCommand]
    private void ClearHistory()
    {
        if (Selected is not { } r) return;
        _store.ClearHistory(r.Key);
        r.History.Clear();
        r.HistoryChanged();
        r.SelectedHistory = null;
        r.SetLastTest(null);
    }

    [RelayCommand]
    private void GenerateClasses()
    {
        if (Selected is not { } r) return;
        var rootName = JsonToCSharp.Pascal(r.Name).TrimStart('@', '_') + "Response";
        var code = JsonToCSharp.Generate(r.TestRaw.Length > 0 ? r.TestRaw : r.TestResponse, rootName);
        if (code is null) { ShowToast("Classes need a JSON object or array of objects"); return; }
        r.TestCodeTitle = ApiRow.ClassesTitle;
        r.TestCodeFile = rootName + ".cs";
        r.TestClasses = code;
    }

    /// <summary>A small typed HttpClient class from every request in this API's history that worked.</summary>
    [RelayCommand]
    private void GenerateClient()
    {
        if (Selected is not { } r) return;
        var usable = ClientGenerator.Usable(r.History);
        if (usable.Count == 0) { ShowToast("Get one request to work first - the client is built from your successful tests"); return; }
        // more than one to choose from: the window asks which become methods
        IReadOnlyList<TestHistoryEntry>? picked = usable;
        if (usable.Count > 1 && PickClientRequests is not null) picked = PickClientRequests(r.Name, usable, ClientGenerator.NewestOfEach(usable));
        if (picked is null || picked.Count == 0) return;
        var code = ClientGenerator.Generate(r.Name, picked, r.DemoKey);
        if (code is null) return;
        r.TestCodeTitle = ApiRow.ClientTitle;
        r.TestCodeFile = ClientGenerator.ClassNameFor(r.Name) + ".cs";
        r.TestClasses = code;
        int methods = ClientGenerator.NewestOfEach(picked).Count;
        ShowToast($"C# client with {methods} method{(methods == 1 ? "" : "s")} - test other endpoints to add more");
    }

    /// <summary>Set by the window: shows the tick list and returns the chosen tests (null = cancelled).</summary>
    public Func<string, IReadOnlyList<TestHistoryEntry>, IReadOnlyCollection<TestHistoryEntry>, IReadOnlyList<TestHistoryEntry>?>? PickClientRequests { get; set; }

    [RelayCommand]
    private void SaveCode()
    {
        if (Selected is not { TestClasses.Length: > 0 } r) return;
        var dlg = new SaveFileDialog { Title = "Save the C# code", FileName = r.TestCodeFile, Filter = "C# file|*.cs|All files|*.*", DefaultExt = ".cs" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            File.WriteAllText(dlg.FileName, r.TestClasses.ReplaceLineEndings() + Environment.NewLine, new UTF8Encoding(false));
            ShowToast("✓ Saved " + Path.GetFileName(dlg.FileName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException) { StatusText = "Could not save the file: " + ex.Message; }
    }

    /// <summary>Docs scan for every listed API whose free access is "Not stated" and that has not been scanned yet.</summary>
    [RelayCommand]
    private async Task ScanDocsBatchAsync()
    {
        if (IsBusy) return;
        var rows = Rows.Where(r => r.Access == AccessLevel.Unknown && r.DocsScan is null).ToList();
        if (rows.Count == 0) { ShowToast("Nothing to scan - every listed 'Not stated' API has been scanned"); return; }

        IsBusy = true;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        int done = 0, resolved = 0;
        try
        {
            await Parallel.ForEachAsync(rows, new ParallelOptions { MaxDegreeOfParallelism = 6, CancellationToken = token }, async (row, ct) =>
            {
                DocsScanResult scan;
                try { scan = await DocsScanner.ScanAsync(row.Entry, ct); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { scan = new DocsScanResult { ScannedAt = DateTime.Now, PageUrl = row.Url, Error = ex.Message }; }
                int n = Interlocked.Increment(ref done);
                _ = Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    row.DocsScan = scan;
                    _store.DocsScans[row.Key] = scan;
                    if (row.Access != AccessLevel.Unknown) resolved++;
                    if (n % 40 == 0) _store.SaveDocsScans();
                    Progress = 100.0 * n / rows.Count;
                    StatusText = $"Reading docs and pricing pages… {n:N0} of {rows.Count:N0}  ({resolved:N0} now have a free-access level)";
                });
            });
            await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
            StatusText = $"Read {rows.Count:N0} docs pages: {resolved:N0} now have a free-access level, {rows.Count - resolved:N0} still say nothing.";
        }
        catch (OperationCanceledException)
        {
            await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
            StatusText = $"Stopped after {done:N0} of {rows.Count:N0} docs pages ({resolved:N0} now have a free-access level).";
        }
        finally
        {
            _store.SaveDocsScans();
            IsBusy = false; Progress = 0; _cts?.Dispose(); _cts = null;
            RefreshDashboard();
            ApplyFilter();
        }
    }

    [RelayCommand]
    private void TidyBody()
    {
        if (Selected is not { } r) return;
        if (ApiTester.TidyJson(r.TestBody) is { } tidy) r.TestBody = tidy;
        else ShowToast("The body is not valid JSON");
    }

    [RelayCommand]
    private void ResetTest()
    {
        if (Selected is not { } r) return;
        r.TestUrl = r.DefaultTestUrl;
        r.TestHeader = r.DefaultTestHeader;
        r.TestMethod = "GET";
        r.TestBody = "";
        r.TestSummary = r.TestResponse = "";
        if (_store.User.TestRequests.Remove(r.Key)) _store.SaveUser();
    }

    [RelayCommand]
    private async Task CheckLinksAsync()
    {
        if (IsBusy || Rows.Count == 0) return;
        var rows = Rows.ToList();
        IsBusy = true;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        int done = 0, online = 0;
        try
        {
            await Parallel.ForEachAsync(rows, new ParallelOptions { MaxDegreeOfParallelism = 12, CancellationToken = token }, async (row, ct) =>
            {
                var s = await LinkChecker.CheckAsync(row.Url, ct);
                if (s.Label == "Online") Interlocked.Increment(ref online);
                int n = Interlocked.Increment(ref done);
                _ = Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    row.LatencyMs = s.LatencyMs;
                    row.Status = s.Label;
                    Progress = 100.0 * n / rows.Count;
                    StatusText = $"Checking links… {n:N0} of {rows.Count:N0}";
                });
            });
            await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
            StatusText = $"Checked {rows.Count:N0} links: {online:N0} online, {rows.Count - online:N0} restricted, slow or down.";
        }
        catch (OperationCanceledException)
        {
            await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
            StatusText = $"Link check stopped after {done:N0} of {rows.Count:N0}.";
        }
        finally { IsBusy = false; Progress = 0; _cts?.Dispose(); _cts = null; RefreshDashboard(); if (OnlineOnly) ApplyFilter(); }
    }

    // ---------------------------------------------------------------- settings

    public void SaveSourceSettings()
    {
        foreach (var t in SourceToggles) _store.Settings.Sources[t.Info.Id] = t.IsOn;
        _store.Settings.CustomSources = [.. CustomSources.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => Uri.TryCreate(l, UriKind.Absolute, out var u) && u.Scheme is "http" or "https").Distinct()];
        _store.SaveSettings();
    }
}
