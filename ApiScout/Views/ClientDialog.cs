using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using ApiScout.Models;

namespace ApiScout.Views;

/// <summary>"C# client": tick which of the requests that worked become methods.</summary>
public sealed class ClientDialog : Window
{
    private readonly List<(CheckBox Box, TestHistoryEntry Entry)> _rows = [];
    private readonly Button _ok = new() { Content = "Build the client", IsDefault = true, Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(0, 0, 8, 0) };

    public IReadOnlyList<TestHistoryEntry> Picked => [.. _rows.Where(r => r.Box.IsChecked == true).Select(r => r.Entry)];

    /// <param name="worked">Successful tests, newest first.</param>
    /// <param name="preselected">The ones ticked to start with (the newest test of each distinct request).</param>
    public ClientDialog(string apiName, IReadOnlyList<TestHistoryEntry> worked, IReadOnlyCollection<TestHistoryEntry> preselected)
    {
        Title = "C# client for " + apiName;
        Width = 640;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 640;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "BgBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");

        var intro = new TextBlock
        {
            Text = "Each ticked request becomes one method. Two tests of the same endpoint (same method, path and parameter names) make one method - the newer one wins. " +
                   "Test more endpoints in Try it to have more to pick from; the history keeps the last 8 results.",
            TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Margin = new Thickness(0, 0, 0, 10),
        };
        intro.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");

        var list = new StackPanel();
        foreach (var h in worked)
        {
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = $"{h.Method}  {h.Url}", FontFamily = new System.Windows.Media.FontFamily("Cascadia Mono, Consolas"), FontSize = 12.5, TextWrapping = TextWrapping.Wrap });
            var when = new TextBlock { Text = $"{h.At:d MMM HH:mm:ss}  ·  {h.Summary.Split('\n')[0]}", FontSize = 11.5 };
            when.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            text.Children.Add(when);
            var box = new CheckBox { Content = text, IsChecked = preselected.Contains(h), Margin = new Thickness(0, 3, 0, 5) };
            AutomationProperties.SetName(box, $"{h.Method} {h.Url}");
            box.Checked += (_, _) => Refresh();
            box.Unchecked += (_, _) => Refresh();
            _rows.Add((box, h));
            list.Children.Add(box);
        }

        _ok.SetResourceReference(StyleProperty, "AccentButtonStyle");
        _ok.Click += (_, _) => DialogResult = true;
        var cancel = new Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(16, 6, 16, 6) };
        var all = new Button { Content = "Tick _all", FontSize = 12, Padding = new Thickness(9, 3, 9, 3), Margin = new Thickness(0, 0, 6, 0) };
        all.Click += (_, _) => { foreach (var r in _rows) r.Box.IsChecked = true; };
        var none = new Button { Content = "_None", FontSize = 12, Padding = new Thickness(9, 3, 9, 3) };
        none.Click += (_, _) => { foreach (var r in _rows) r.Box.IsChecked = false; };

        var buttons = new DockPanel { Margin = new Thickness(0, 14, 0, 0), LastChildFill = false };
        DockPanel.SetDock(cancel, Dock.Right); DockPanel.SetDock(_ok, Dock.Right);
        buttons.Children.Add(cancel); buttons.Children.Add(_ok); buttons.Children.Add(all); buttons.Children.Add(none);

        var root = new DockPanel { Margin = new Thickness(22, 18, 22, 18) };
        DockPanel.SetDock(intro, Dock.Top); DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(intro); root.Children.Add(buttons);
        root.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false });
        Content = root;
        Refresh();
        Loaded += (_, _) => (_rows.FirstOrDefault().Box as UIElement ?? _ok).Focus();
    }

    private void Refresh() => _ok.IsEnabled = _rows.Any(r => r.Box.IsChecked == true);
}
