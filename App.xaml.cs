using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using ApiScout.Services;
using ApiScout.ViewModels;

namespace ApiScout;

public partial class App : Application
{
    public static Store Store { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // a safety net under every regex that reads downloaded text: a match that runs this long is a hostile or broken page
        AppDomain.CurrentDomain.SetData("REGEX_DEFAULT_MATCH_TIMEOUT", TimeSpan.FromSeconds(5));
        DispatcherUnhandledException += (_, args) =>
        {
            Store?.Log("Unhandled: " + args.Exception);
            MessageBox.Show(args.Exception.Message, "ApiScout hit a problem", MessageBoxButton.OK, MessageBoxImage.Warning);
            args.Handled = true;
        };
        try { Store = new Store(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            MessageBox.Show("ApiScout cannot use its data folder:\n" + ex.Message, "ApiScout", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(2);
            return;
        }
        if (e.Args.FirstOrDefault(x => x.StartsWith("--background-scan=", StringComparison.OrdinalIgnoreCase)) is { } sw)
        {
            // ApiScout.exe --background-scan=weekly|daily|off : same as the tick box in Sources, for scripts
            var mode = sw[(sw.IndexOf('=') + 1)..].ToLowerInvariant();
            var (ok, message) = mode == "off" ? ScheduledScan.Unregister() : ScheduledScan.Register(mode == "daily");
            if (ok)
            {
                Store.Settings.BackgroundScan = mode != "off";
                if (mode != "off") Store.Settings.AutoRescan = mode == "daily" ? "Daily" : "Weekly";
                Store.SaveSettings();
            }
            Store.Log("Background scan switch: " + message);
            Shutdown(ok ? 0 : 1);
            return;
        }
        if (e.Args.Contains("--scan", StringComparer.OrdinalIgnoreCase))
        {
            // headless re-scan for Task Scheduler: ApiScout.exe --scan. An open window re-scans by itself, and two writers would trip over catalog.json.
            _windowOpen = WindowMutex(Store.Folder);
            if (!TryTakeWindowMutex()) { Store.Log("Headless scan skipped: ApiScout is open."); Shutdown(0); return; }
            _windowOpen.ReleaseMutex();
            _ = HeadlessScanAsync();
            return;
        }
        Updater.CleanUp(); // the exe an update replaced
        _windowOpen = WindowMutex(Store.Folder);
        _holdsMutex = TryTakeWindowMutex();
        // started by "Update and restart": the old instance may need a moment to let go
        for (int i = 0; !_holdsMutex && e.Args.Contains("--after-update") && i < 40; i++) { Thread.Sleep(250); _holdsMutex = TryTakeWindowMutex(); }
        if (!_holdsMutex)
        {
            // two windows on one data folder would each rewrite userdata.json with their own idea of it
            // a window hidden in the tray has no handle to bring forward, so it is asked by name to show itself
            if (EventWaitHandle.TryOpenExisting(ShowSignalName(Store.Folder), out var ask)) { ask.Set(); ask.Dispose(); }
            foreach (var other in System.Diagnostics.Process.GetProcessesByName("ApiScout"))
                if (other.Id != Environment.ProcessId && other.MainWindowHandle != IntPtr.Zero) { if (IsIconic(other.MainWindowHandle)) ShowWindow(other.MainWindowHandle, 9); SetForegroundWindow(other.MainWindowHandle); break; }
            Shutdown(0);
            return;
        }
        ApplyTheme(Store.Settings.Dark);
        var window = new MainWindow(new MainViewModel(Store));
        MainWindow = window;
        window.Show();

        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSignalName(Store.Folder));
        var listener = new Thread(() =>
        {
            while (_showSignal.WaitOne()) { if (_exiting) return; Dispatcher.BeginInvoke(window.ShowFromTray); }
        }) { IsBackground = true, Name = "show-signal" };
        listener.Start();
    }

    // held for as long as a window of this user's data folder is open
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command); // 9 = restore

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);

    private EventWaitHandle? _showSignal;
    private volatile bool _exiting;

    private static string ShowSignalName(string folder)
    {
        uint h = 2166136261;
        foreach (var c in folder.ToLowerInvariant()) h = (h ^ c) * 16777619;
        return $"ApiScout.Show.{h:x8}";
    }

    private Mutex? _windowOpen;
    private bool _holdsMutex;

    /// <summary>One name per data folder (string.GetHashCode differs between processes, so the hash is spelled out).</summary>
    private static Mutex WindowMutex(string folder)
    {
        uint h = 2166136261;
        foreach (var c in folder.ToLowerInvariant()) h = (h ^ c) * 16777619;
        return new Mutex(false, $"ApiScout.Window.{h:x8}");
    }

    private bool TryTakeWindowMutex()
    {
        try { return _windowOpen!.WaitOne(0); }
        catch (AbandonedMutexException) { return true; } // the last holder crashed; it is ours now
    }

    /// <summary>After an update: hand the data folder over to the new exe and leave.</summary>
    public void RestartInto(string exe)
    {
        if (_holdsMutex) { _windowOpen?.ReleaseMutex(); _holdsMutex = false; }
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, "--after-update") { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe) ?? "" });
        Shutdown(0);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _exiting = true;
        _showSignal?.Set(); // lets the listener thread end
        if (_holdsMutex) _windowOpen?.ReleaseMutex();
        _windowOpen?.Dispose();
        base.OnExit(e);
    }

    private async Task HeadlessScanAsync()
    {
        int code = 0;
        try
        {
            var outcome = await Scanner.RunAsync(MainViewModel.EnabledSources(Store.Settings), new Progress<ScanProgress>(), CancellationToken.None);
            if (outcome.Catalog.Entries.Count == 0) throw new InvalidOperationException("nothing found - " + string.Join("; ", outcome.Notes));
            var previous = Store.LoadCatalog();
            int kept = outcome.FailedSources > 0 ? Scanner.KeepUnreadable(outcome.Catalog, previous) : 0;
            var (added, removed) = Scanner.StampFirstSeen(outcome.Catalog, previous);
            if (previous is { Entries.Count: > 0 }) Store.AddScanReport(ChangeLog.Build(outcome.Catalog, previous, Store.DocsScans, "Background scan", outcome.FailedSources));
            Store.SaveCatalog(outcome.Catalog);
            Store.Log($"Headless scan: {outcome.Catalog.Entries.Count:N0} APIs, {added:N0} new, {removed:N0} gone" + (outcome.FailedSources > 0 ? $", {outcome.FailedSources} source(s) failed ({kept:N0} kept): {string.Join("; ", outcome.Notes.Where(n => n.Contains("failed")))}" : ""));
            if (added > 0)
            {
                var fresh = outcome.Catalog.Entries.Where(x => x.FirstSeen == outcome.Catalog.ScannedAt).Select(x => x.Name).Take(4).ToList();
                await NotifyAsync($"{added:N0} new free API{(added == 1 ? "" : "s")} found",
                    string.Join(", ", fresh) + (added > fresh.Count ? $" and {added - fresh.Count:N0} more" : "") + ". Click to open ApiScout.");
            }
        }
        catch (Exception ex) { Store.Log("Headless scan failed: " + ex.Message); code = 1; }
        Shutdown(code);
    }

    /// <summary>Tray notification from the headless scan; clicking it opens ApiScout. Stays up to 12 seconds.</summary>
    private static async Task NotifyAsync(string title, string text)
    {
        using var icon = new System.Windows.Forms.NotifyIcon
        {
            Icon = Environment.ProcessPath is { } exe ? System.Drawing.Icon.ExtractAssociatedIcon(exe) : System.Drawing.SystemIcons.Information,
            Text = "ApiScout", Visible = true,
        };
        var clicked = new TaskCompletionSource();
        icon.BalloonTipClicked += (_, _) => clicked.TrySetResult();
        icon.ShowBalloonTip(10000, title, text, System.Windows.Forms.ToolTipIcon.Info);
        if (await Task.WhenAny(clicked.Task, Task.Delay(12000)) == clicked.Task && Environment.ProcessPath is { } path)
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        icon.Visible = false;
    }

    public static void ApplyTheme(bool dark)
    {
        var app = Current;
        var window = app.MainWindow;
        var state = window?.WindowState;
        app.ThemeMode = dark ? ThemeMode.Dark : ThemeMode.Light;
        // Switching Fluent ThemeMode at runtime can briefly minimise the window
        if (window is not null && state is { } s && s != WindowState.Minimized)
            app.Dispatcher.BeginInvoke(() => { if (window.WindowState == WindowState.Minimized) window.WindowState = s; }, DispatcherPriority.Background);

        void Set(string key, string hex) => app.Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        if (dark)
        {
            Set("BgBrush", "#121418"); Set("NavBrush", "#181B21"); Set("CardBrush", "#1C2027"); Set("CardAltBrush", "#232833");
            Set("CardBorderBrush", "#2C323D"); Set("TextBrush", "#ECEFF4"); Set("MutedBrush", "#8B93A3");
            Set("AccentBrush", "#4F8CFF"); Set("AccentSoftBrush", "#264F8CFF");
            Set("UpBrush", "#3DDC84"); Set("DownBrush", "#FF5C6C"); Set("WarnBrush", "#F5B83D"); Set("VioletBrush", "#B48CFF");
        }
        else
        {
            Set("BgBrush", "#F3F5F9"); Set("NavBrush", "#FFFFFF"); Set("CardBrush", "#FFFFFF"); Set("CardAltBrush", "#EEF1F6");
            Set("CardBorderBrush", "#DDE2EA"); Set("TextBrush", "#171B22"); Set("MutedBrush", "#5D6676");
            Set("AccentBrush", "#2563EB"); Set("AccentSoftBrush", "#1F2563EB");
            Set("UpBrush", "#12834A"); Set("DownBrush", "#D12D43"); Set("WarnBrush", "#A86A00"); Set("VioletBrush", "#7443D6");
        }
    }
}
