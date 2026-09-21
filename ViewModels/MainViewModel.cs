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

public sealed partial class MainViewModel : ObservableObject
{
    public const string AllCategory = "All APIs";
    public const string FavouritesCategory = "★ Favourites";
    public const string DemoKeyCategory = "🔑 Demo key included";
    public const string NewCategory = "🆕 New (last 14 days)";
    private const int SpecialCategories = 4;

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
            StatusText = $"Loaded {cached.Entries.Count:N0} APIs from the last scan ({cached.ScannedAt:d MMM yyyy HH:mm}). Press Scan to refresh.";
        }
        else StatusText = "Press Scan the internet to find free APIs.";

        // automatic re-scan: shortly after start-up, then checked every half hour while the app stays open
        _rescanTimer.Tick += (_, _) => AutoRescanIfDue();
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

    public string AutoRescan
    {
        get => _store.Settings.AutoRescan;
        set { _store.Settings.AutoRescan = value; _store.SaveSettings(); OnPropertyChanged(); AutoRescanIfDue(); }
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
        _autoScan = true;
        ScanCommand.Execute(null);
    }

    private bool _autoScan;

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
        if (oldValue is not null) oldValue.PropertyChanged -= SelectedRowChanged;
        HasSelection = newValue is not null;
        ShowMyKey = false;
        if (newValue is null) return;
        if (newValue.MyKey.Length == 0) newValue.MyKey = _store.GetMyKey(newValue.Key) ?? "";
        if (newValue.TestUrl.Length == 0)
        {
            var saved = _store.GetTestRequest(newValue.Key);
            newValue.TestUrl = saved?.Url ?? newValue.DefaultTestUrl;
            newValue.TestHeader = saved?.Headers ?? newValue.DefaultTestHeader;
            newValue.TestMethod = saved?.Method ?? "GET";
            newValue.TestBody = saved?.Body ?? "";
        }
        if (!newValue.HistoryLoaded)
        {
            newValue.HistoryLoaded = true;
            foreach (var h in _store.TestHistory.GetValueOrDefault(newValue.Key) ?? []) newValue.History.Add(h);
            newValue.HistoryChanged();
        }
        newValue.PropertyChanged += SelectedRowChanged;
    }

    private void SelectedRowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not ApiRow row) return;
        if (e.PropertyName == nameof(ApiRow.Note))
        {
            if (row.Note.Length == 0) _store.User.Notes.Remove(row.Key); else _store.User.Notes[row.Key] = row.Note;
            _store.SaveUser();
        }
        else if (e.PropertyName == nameof(ApiRow.SelectedHistory) && !_showingHistory && row.SelectedHistory is { } entry)
        {
            // the user picked an earlier result: show it in the response box
            row.TestOk = entry.Ok;
            row.TestSummary = (entry == row.History.FirstOrDefault() ? "" : $"Earlier result from {entry.At:d MMM HH:mm:ss}\n") + entry.Summary;
            row.TestResponse = row.TestRaw = entry.Response;
            row.TestClasses = "";
        }
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
            var (added, removed) = Scanner.StampFirstSeen(outcome.Catalog, _catalog);
            _store.SaveCatalog(outcome.Catalog);
            Load(outcome.Catalog);
            var failed = outcome.Notes.Where(n => n.Contains("failed")).ToList();
            StatusText = $"{(auto ? "Automatic re-scan" : "Scan")} finished: {_all.Count:N0} unique APIs in {Categories.Count - SpecialCategories} categories" +
                         (before > 0 ? $" - {added:N0} new, {removed:N0} gone since the last scan" : "") +
                         (failed.Count > 0 ? $". {failed.Count} source(s) failed: {string.Join("; ", failed)}" : ".");
            if (before > 0 && added > 0) ShowToast($"🆕 {added:N0} new API{(added == 1 ? "" : "s")} since the last scan");
        }
        catch (OperationCanceledException) { StatusText = "Scan cancelled."; }
        catch (Exception ex) { StatusText = "Scan failed: " + ex.Message; _store.Log("Scan failed: " + ex); }
        finally { IsBusy = false; Progress = 0; _cts = null; }
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    private void Load(Catalog catalog)
    {
        var keep = Selected?.Key;
        var old = _all.ToDictionary(r => r.Key);
        _catalog = catalog;
        _all = [.. catalog.Entries.Select(e => new ApiRow(e, catalog.BaselineAt))];
        foreach (var r in _all)
        {
            r.IsFavourite = _store.User.Favourites.Contains(r.Key);
            if (_store.User.Notes.TryGetValue(r.Key, out var note)) r.Note = note;
            if (_store.DocsScans.TryGetValue(r.Key, out var scan)) r.DocsScan = scan;
            if (old.TryGetValue(r.Key, out var was)) { r.Status = was.Status; r.LatencyMs = was.LatencyMs; }
        }

        var selectedName = SelectedCategory?.Name ?? AllCategory;
        Categories.Clear();
        Categories.Add(new CategoryItem(AllCategory));
        Categories.Add(new CategoryItem(FavouritesCategory));
        Categories.Add(new CategoryItem(DemoKeyCategory));
        Categories.Add(new CategoryItem(NewCategory));
        foreach (var name in _all.Select(r => r.Category).Distinct().OrderBy(n => n == "Other").ThenBy(n => n, StringComparer.OrdinalIgnoreCase))
            Categories.Add(new CategoryItem(name));
        SelectedCategory = Categories.FirstOrDefault(c => c.Name == selectedName) ?? Categories[0];
        OnPropertyChanged(nameof(IsEmpty));
        ApplyFilter();
        if (keep is not null) Selected = Rows.FirstOrDefault(r => r.Key == keep);
    }

    // ---------------------------------------------------------------- filtering

    private void ApplyFilter(bool countCategories = true)
    {
        var words = SearchText.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var pre = _all.Where(r =>
            (!HttpsOnly || r.Entry.Https == true) &&
            (!CorsOnly || r.Cors == "Yes") &&
            (!OnlineOnly || r.Status == "Online") &&
            MatchesAuth(r) &&
            (AccessFilter == "Any free access" || r.AccessLabel == AccessFilter) &&
            words.All(w => r.SearchText.Contains(w))).ToList();

        if (countCategories)
        {
            var counts = pre.GroupBy(r => r.Category).ToDictionary(g => g.Key, g => g.Count());
            foreach (var c in Categories)
                c.Count = c.Name switch
                {
                    AllCategory => pre.Count,
                    FavouritesCategory => pre.Count(r => r.IsFavourite),
                    DemoKeyCategory => pre.Count(r => r.HasDemoKey),
                    NewCategory => pre.Count(r => r.IsNew),
                    _ => counts.GetValueOrDefault(c.Name),
                };
        }

        var cat = SelectedCategory?.Name ?? AllCategory;
        var keep = Selected;
        Rows = cat switch
        {
            AllCategory => pre,
            FavouritesCategory => [.. pre.Where(r => r.IsFavourite)],
            DemoKeyCategory => [.. pre.Where(r => r.HasDemoKey)],
            NewCategory => [.. pre.Where(r => r.IsNew)],
            _ => [.. pre.Where(r => r.Category == cat)],
        };
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
        SearchText = ""; AuthFilter = "Any auth"; AccessFilter = "Any free access"; HttpsOnly = CorsOnly = OnlineOnly = false;
        SelectedCategory = Categories.FirstOrDefault();
    }

    // ---------------------------------------------------------------- copy

    [RelayCommand]
    private void Copy(string? what)
    {
        if (Selected is not { } r) return;
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
            "classes" => (r.TestClasses, "C# classes"),
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
            sb.Append($"{r.Name}\t{r.Category}\t{r.AuthLabel}\t{r.DemoKey}\t{r.SignupUrl}\t{r.Url}\t{r.Description.Replace('\t', ' ')}\r\n");
        CopyText(sb.ToString(), $"{rows.Count} rows");
    }

    public void CopyText(string text, string label)
    {
        if (string.IsNullOrEmpty(text)) { ShowToast("Nothing to copy"); return; }
        for (int attempt = 0; ; attempt++)
        {
            try { Clipboard.SetDataObject(text, true); break; }
            catch (COMException) when (attempt < 5) { Thread.Sleep(40); } // another app has the clipboard open
            catch (COMException) { ShowToast("Clipboard is busy - try again"); return; }
        }
        ShowToast($"✓ {label} copied");
    }

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
        if (Selected is not { } r) return;
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
        if (Selected is not { } r) return;
        r.IsFavourite = !r.IsFavourite;
        if (r.IsFavourite) _store.User.Favourites.Add(r.Key); else _store.User.Favourites.Remove(r.Key);
        _store.SaveUser();
        ApplyFilter();
    }

    [RelayCommand]
    private void SaveMyKey()
    {
        if (Selected is not { } r) return;
        _store.SetMyKey(r.Key, r.MyKey);
        ShowToast(r.MyKey.Trim().Length == 0 ? "Your key was removed" : "✓ Your key was saved (encrypted)");
    }

    [RelayCommand]
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
        finally { r.IsScanningDocs = false; }
    }

    /// <summary>"Test this API": sends the request in the box; {key} stands for the key saved under My key.</summary>
    [RelayCommand]
    private async Task TestApiAsync()
    {
        if (Selected is not { } r || r.IsTesting) return;
        r.IsTesting = true;
        r.TestSummary = "Sending…";
        try
        {
            string Fill(string s) => s.Replace("{key}", r.MyKey.Trim(), StringComparison.OrdinalIgnoreCase);
            var body = r.ShowTestBody ? r.TestBody : "";
            if ((r.TestUrl + r.TestHeader + body).Contains("{key}", StringComparison.OrdinalIgnoreCase) && r.MyKey.Trim().Length == 0)
            {
                r.TestOk = false; r.TestResponse = "";
                r.TestSummary = "The request uses {key} but no key is saved under 'My key' below. Save one first.";
                return;
            }
            if (r.TestUrl.Trim() != r.DefaultTestUrl || r.TestHeader.Trim() != r.DefaultTestHeader || r.TestMethod != "GET" || r.TestBody.Trim().Length > 0)
                _store.SetTestRequest(r.Key, new ApiTestRequest(r.TestMethod, r.TestUrl.Trim(), r.TestHeader.Trim(), r.TestBody.Trim()));
            var request = new ApiTestRequest(r.TestMethod, Fill(r.TestUrl), Fill(r.TestHeader), Fill(body));
            var result = await Task.Run(() => ApiTester.SendAsync(request, CancellationToken.None));
            r.TestOk = result.Ok;
            r.TestSummary = result.Summary;
            r.TestResponse = result.Body;
            r.TestRaw = result.Raw;
            r.TestClasses = "";

            // history keeps the request as typed, so {key} stays a placeholder on disk too
            var entry = _store.AddHistory(r.Key, new TestHistoryEntry
            {
                At = DateTime.Now, Method = r.TestMethod, Url = r.TestUrl.Trim(), Headers = r.TestHeader.Trim(), Body = body.Trim(),
                Ok = result.Ok, Summary = result.Summary, Response = result.Body,
            });
            r.History.Insert(0, entry);
            while (r.History.Count > Store.HistoryPerApi) r.History.RemoveAt(r.History.Count - 1);
            r.HistoryChanged();
            _showingHistory = true;
            r.SelectedHistory = entry;
            _showingHistory = false;
        }
        catch (Exception ex) { r.TestOk = false; r.TestSummary = "Request failed: " + ex.Message; r.TestResponse = ""; }
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
    [RelayCommand]
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
    }

    [RelayCommand]
    private void GenerateClasses()
    {
        if (Selected is not { } r) return;
        var rootName = JsonToCSharp.Pascal(r.Name).TrimStart('@', '_') + "Response";
        var code = JsonToCSharp.Generate(r.TestRaw.Length > 0 ? r.TestRaw : r.TestResponse, rootName);
        if (code is null) { ShowToast("Classes need a JSON object or array of objects"); return; }
        r.TestClasses = code;
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
            IsBusy = false; Progress = 0; _cts = null;
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
        catch (OperationCanceledException) { StatusText = $"Link check stopped after {done:N0} of {rows.Count:N0}."; }
        finally { IsBusy = false; Progress = 0; _cts = null; }
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
