using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using ApiScout.Services;
using ApiScout.ViewModels;
using Microsoft.Win32;

namespace ApiScout.Views;

/// <summary>Version, where the data lives, and moving your own data (favourites, tags, notes, keys) between PCs.</summary>
public sealed class AboutWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly Store _store;
    private readonly TextBox _feed = new() { FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _updateResult = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Margin = new Thickness(0, 6, 0, 0) };
    private readonly Button _openUpdate;
    private string? _download;

    private async Task CheckForUpdateAsync()
    {
        _store.Settings.UpdateFeed = _feed.Text.Trim();
        _updateResult.Text = "Checking…";
        var info = await UpdateChecker.CheckAsync(_store.Settings.UpdateFeed, UpdateChecker.Current, CancellationToken.None);
        _store.Settings.LastUpdateCheck = DateTime.Now;
        _store.SaveSettings();
        _updateResult.Text = info.Message;
        _updateResult.SetResourceReference(TextBlock.ForegroundProperty, !info.Ok ? "DownBrush" : info.Newer ? "WarnBrush" : "UpBrush");
        _download = info.Newer ? info.Download : null;
        _openUpdate.Visibility = _download is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OpenDownload()
    {
        if (_download is null) return;
        if (Http.IsWebUrl(_download)) _vm.OpenUrlCommand.Execute(_download);
        else if (File.Exists(_download)) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{_download}\"") { UseShellExecute = true });
    }

    private readonly TextBlock _dataSummary = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _result = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0), FontSize = 12.5 };

    public static string VersionText =>
        (Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?").Split('+')[0];

    public AboutWindow(MainViewModel vm, Store store)
    {
        _vm = vm;
        _store = store;
        Title = "About ApiScout";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "BgBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");

        var panel = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
        panel.Children.Add(new TextBlock { Text = "ApiScout", FontSize = 24, FontWeight = FontWeights.Bold });
        panel.Children.Add(Muted($"free API finder  ·  version {VersionText}  ·  {RuntimeInformation.FrameworkDescription}"));

        panel.Children.Add(Heading("YOUR DATA"));
        _dataSummary.Text = vm.DataSummary;
        panel.Children.Add(_dataSummary);
        var folder = new TextBox { Text = store.Folder, IsReadOnly = true, FontFamily = new System.Windows.Media.FontFamily("Cascadia Mono, Consolas"), FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(folder, "Data folder");
        var open = Small("Open folder", () => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{store.Folder}\"") { UseShellExecute = true }));
        var copy = Small("Copy", () => vm.CopyText(store.Folder, "Data folder"));
        var folderRow = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(open, Dock.Right); DockPanel.SetDock(copy, Dock.Right);
        folderRow.Children.Add(open); folderRow.Children.Add(copy); folderRow.Children.Add(folder);
        panel.Children.Add(folderRow);

        panel.Children.Add(Heading("UPDATES"));
        _feed.Text = store.Settings.UpdateFeed;
        AutomationProperties.SetName(_feed, "Update source");
        _feed.ToolTip = "A GitHub owner/repo, a folder holding the ApiScout git repository, or a latest.json file / URL. Empty = " + (UpdateChecker.DefaultFeed.Length > 0 ? UpdateChecker.DefaultFeed : "not set");
        var check = Small("Check now", () => _ = CheckForUpdateAsync());
        _openUpdate = Small("Open", () => OpenDownload());
        _openUpdate.Visibility = Visibility.Collapsed;
        var feedRow = new DockPanel();
        DockPanel.SetDock(check, Dock.Right); DockPanel.SetDock(_openUpdate, Dock.Right);
        feedRow.Children.Add(_openUpdate); feedRow.Children.Add(check); feedRow.Children.Add(_feed);
        panel.Children.Add(feedRow);
        _updateResult.Text = "Looks at version tags (v1.4.0): empty = the repository this build came from" + (UpdateChecker.DefaultFeed.Length > 0 ? $" ({UpdateChecker.DefaultFeed})" : "") +
                             "; or type a GitHub owner/repo, a folder, or a latest.json. Checked once a day at start-up." + (store.IsPortable ? "  ·  This is a portable copy: its data sits in the data folder beside the exe." : "");
        _updateResult.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        panel.Children.Add(_updateResult);

        panel.Children.Add(Heading("KEYBOARD"));
        panel.Children.Add(Muted("F5 scan  ·  Esc stop / back  ·  Ctrl+F search  ·  Ctrl+L my shortlist  ·  Ctrl+T test  ·  Ctrl+D favourite  ·  Ctrl+K copy key (yours, else the demo key)  ·  Ctrl+U copy docs URL  ·  " +
                                 "Ctrl+C copy the selected rows  ·  Ctrl+G tag them  ·  Ctrl+E add them to a collection  ·  Ctrl+M compare 2-4 of them  ·  Ctrl+H what changed  ·  F1 this box\n" +
                                 "Shortlist cards: arrows, Enter details, T test, K copy key, O open docs, U copy URL, D favourite.  " +
                                 "Compare: F5 measure, Ctrl+Shift+C copy as Markdown, Ctrl+1-4 open docs."));

        panel.Children.Add(Heading("MOVE MY DATA TO ANOTHER PC"));
        panel.Children.Add(Muted("Export writes your favourites, tags, collections and notes to one file. Saved keys and edited test requests are tied to this Windows " +
                                 "account, so they are only included if you give a passphrase - they are then encrypted with it (AES-256). " +
                                 "Import merges: nothing already on this PC is overwritten."));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        buttons.Children.Add(Big("Export my data…", Export));
        buttons.Children.Add(Big("Import…", Import));
        panel.Children.Add(buttons);
        panel.Children.Add(_result);

        var close = new Button { Content = "Close", IsCancel = true, IsDefault = true, Padding = new Thickness(18, 6, 18, 6), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        close.Click += (_, _) => Close();
        panel.Children.Add(close);
        Content = panel;
    }

    private void Export()
    {
        var ask = new InputDialog("Export my data", "Passphrase for your saved keys. Leave it empty to export without the keys.\n\nYou will need the same passphrase to import them - it cannot be recovered.", [], "Continue", secret: true) { Owner = this };
        if (ask.ShowDialog() != true) return;
        var dlg = new SaveFileDialog { Title = "Export my ApiScout data", FileName = $"apiscout-my-data-{DateTime.Now:yyyy-MM-dd}.json", Filter = "ApiScout export|*.json" };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            File.WriteAllText(dlg.FileName, Backup.Export(_store, ask.Value), new UTF8Encoding(false));
            Say($"Exported to {dlg.FileName}" + (ask.Value.Length > 0 ? " - saved keys included, encrypted with your passphrase." : " - without saved keys."), ok: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Say("Export failed: " + ex.Message, ok: false); }
    }

    private void Import()
    {
        var dlg = new OpenFileDialog { Title = "Import ApiScout data", Filter = "ApiScout export|*.json|All files|*.*" };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            var file = Backup.Read(File.ReadAllText(dlg.FileName));
            string? passphrase = null;
            if (file.Secrets is not null)
            {
                var ask = new InputDialog("Import", $"This file holds {file.SecretCount:N0} encrypted key(s) / test request(s).\nPassphrase (leave empty to import everything except those):", [], "Import", secret: true) { Owner = this };
                if (ask.ShowDialog() != true) return;
                passphrase = ask.Value;
            }
            var summary = Backup.Import(_store, file, passphrase);
            _vm.ReloadUserData();
            _dataSummary.Text = _vm.DataSummary;
            Say(summary.ToString(), ok: true);
        }
        catch (CryptographicException) { Say("That passphrase does not open this file. Nothing was imported.", ok: false); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or FormatException) { Say("Import failed: " + ex.Message, ok: false); }
    }

    private void Say(string text, bool ok)
    {
        _result.Text = text;
        _result.SetResourceReference(TextBlock.ForegroundProperty, ok ? "UpBrush" : "DownBrush");
    }

    private static TextBlock Muted(string text)
    {
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Margin = new Thickness(0, 2, 0, 0) };
        t.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        return t;
    }

    private static TextBlock Heading(string text)
    {
        var t = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 20, 0, 6) };
        t.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        return t;
    }

    private static Button Small(string text, Action click)
    {
        var b = new Button { Content = text, FontSize = 12, Padding = new Thickness(9, 3, 9, 3), Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        b.Click += (_, _) => click();
        return b;
    }

    private static Button Big(string text, Action click)
    {
        var b = new Button { Content = text, Padding = new Thickness(16, 7, 16, 7), Margin = new Thickness(0, 0, 8, 0) };
        b.Click += (_, _) => click();
        return b;
    }
}
