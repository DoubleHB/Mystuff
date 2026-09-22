using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using ApiScout.Models;
using ApiScout.Services;
using ApiScout.ViewModels;

namespace ApiScout.Views;

/// <summary>"What changed": per scan, the APIs that are new, gone, or have a different auth / free-access level.</summary>
public sealed class ChangesWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly ComboBox _scans = new() { MinWidth = 420, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Margin = new Thickness(0, 8, 0, 0) };
    private readonly StackPanel _body = new();
    private const int ShownPerSection = 400;

    public ChangesWindow(MainViewModel vm)
    {
        _vm = vm;
        Title = "What changed - ApiScout";
        Width = 760;
        Height = Math.Min(820, SystemParameters.WorkArea.Height - 60);
        MinWidth = 560; MinHeight = 360;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "BgBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        _summary.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");

        var top = new StackPanel { Margin = new Thickness(20, 16, 20, 10) };
        top.Children.Add(new TextBlock { Text = "What changed", FontSize = 22, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 8) });
        AutomationProperties.SetName(_scans, "Scan");
        _scans.ItemsSource = vm.ScanReports;
        _scans.SelectionChanged += (_, _) => Show(_scans.SelectedItem as ScanReport);
        top.Children.Add(_scans);
        top.Children.Add(_summary);

        var copy = new Button { Content = "_Copy as Markdown", Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 0) };
        copy.Click += (_, _) => { if (_scans.SelectedItem is ScanReport r) _vm.CopyText(ChangeLog.ToMarkdown(r), "Scan report"); };
        var close = new Button { Content = "Close", IsCancel = true, Padding = new Thickness(14, 6, 14, 6) };
        close.Click += (_, _) => Close();
        var hint = new TextBlock { Text = $"The last {Store.ScanReportsKept} scans are kept, background scans included.", FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        var buttons = new DockPanel { Margin = new Thickness(20, 8, 20, 14) };
        DockPanel.SetDock(close, Dock.Right); DockPanel.SetDock(copy, Dock.Right);
        buttons.Children.Add(close); buttons.Children.Add(copy); buttons.Children.Add(hint);

        var root = new DockPanel();
        DockPanel.SetDock(top, Dock.Top); DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(top); root.Children.Add(buttons);
        root.Children.Add(new ScrollViewer { Content = _body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(20, 0, 14, 0), Focusable = false });
        Content = root;

        if (vm.ScanReports.Count > 0) _scans.SelectedIndex = 0; else Show(null);
        Loaded += (_, _) => _scans.Focus();
        // a scan that finishes while this is open goes straight to the top
        void OnVm(object? s, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(MainViewModel.ScanReports)) return;
            _scans.Visibility = Visibility.Visible;
            _scans.ItemsSource = null; _scans.ItemsSource = vm.ScanReports; _scans.SelectedIndex = 0;
        }
        vm.PropertyChanged += OnVm;
        Closed += (_, _) => vm.PropertyChanged -= OnVm;
    }

    private void Show(ScanReport? r)
    {
        _body.Children.Clear();
        if (r is null)
        {
            _scans.Visibility = Visibility.Collapsed;
            _summary.Text = "Nothing to compare yet. The first scan is the baseline - from the second scan on, this window lists what is new, what is gone, " +
                            "and which APIs changed how they are accessed.";
            return;
        }
        _summary.Text = $"{r.Total:N0} APIs, compared with the scan of {r.ComparedWith:d MMM yyyy HH:mm}." +
                        (r.IsEmpty ? " Nothing changed." : "") +
                        (r.FailedSources > 0 ? $" {r.FailedSources} source(s) could not be read that time - what only they knew was kept, not counted as gone." : "");
        Section("New", "UpBrush", r.Added, c => c.Category, canShow: true);
        Section("Changed", "AccentBrush", r.Changed, c => c.What, canShow: true);
        Section("Gone", "DownBrush", r.Removed, c => c.Category, canShow: false);
    }

    private void Section(string title, string brush, List<ScanChange> list, Func<ScanChange, string> detail, bool canShow)
    {
        if (list.Count == 0) return;
        var head = new TextBlock { Text = $"{title.ToUpperInvariant()}  ·  {list.Count:N0}", FontWeight = FontWeights.Bold, FontSize = 12.5, Margin = new Thickness(0, 14, 0, 6) };
        head.SetResourceReference(TextBlock.ForegroundProperty, brush);
        _body.Children.Add(head);

        foreach (var c in list.Take(ShownPerSection))
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
            var card = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 7, 8, 7), Margin = new Thickness(0, 0, 0, 4), Child = row };
            card.SetResourceReference(Border.BackgroundProperty, "CardBrush");

            // still in today's list? then it can be opened there; a gone API only has its old docs link
            bool here = canShow && _vm.HasApi(c.Key);
            var action = new Button { Content = here ? "Show in list" : "Open docs", FontSize = 12, Padding = new Thickness(9, 3, 9, 3), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(action, $"{(here ? "Show" : "Open docs of")} {c.Name}");
            action.Click += (_, _) => { if (here) { _vm.ShowApi(c.Key); Owner?.Activate(); } else _vm.OpenUrlCommand.Execute(c.Url); };
            DockPanel.SetDock(action, Dock.Right);
            row.Children.Add(action);

            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = c.Name, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            var sub = new TextBlock { Text = detail(c) is { Length: > 0 } d ? d : c.Url, FontSize = 12, TextWrapping = TextWrapping.Wrap };
            sub.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            text.Children.Add(sub);
            row.Children.Add(text);
            _body.Children.Add(card);
        }
        if (list.Count > ShownPerSection)
        {
            var more = new TextBlock { Text = $"… and {list.Count - ShownPerSection:N0} more - Copy as Markdown has them all.", FontSize = 12, Margin = new Thickness(0, 4, 0, 0) };
            more.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            _body.Children.Add(more);
        }
    }
}
