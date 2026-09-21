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
        Store = new Store();
        DispatcherUnhandledException += (_, args) =>
        {
            Store.Log("Unhandled: " + args.Exception);
            MessageBox.Show(args.Exception.Message, "ApiScout hit a problem", MessageBoxButton.OK, MessageBoxImage.Warning);
            args.Handled = true;
        };
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
            // headless re-scan for Task Scheduler: ApiScout.exe --scan
            _ = HeadlessScanAsync();
            return;
        }
        ApplyTheme(Store.Settings.Dark);
        var window = new MainWindow(new MainViewModel(Store));
        MainWindow = window;
        window.Show();
    }

    private async Task HeadlessScanAsync()
    {
        int code = 0;
        try
        {
            var outcome = await Scanner.RunAsync(MainViewModel.EnabledSources(Store.Settings), new Progress<ScanProgress>(), CancellationToken.None);
            if (outcome.Catalog.Entries.Count == 0) throw new InvalidOperationException("nothing found - " + string.Join("; ", outcome.Notes));
            var (added, removed) = Scanner.StampFirstSeen(outcome.Catalog, Store.LoadCatalog());
            Store.SaveCatalog(outcome.Catalog);
            Store.Log($"Headless scan: {outcome.Catalog.Entries.Count:N0} APIs, {added:N0} new, {removed:N0} gone");
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
