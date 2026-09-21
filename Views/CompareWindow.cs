using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ApiScout.Services;
using ApiScout.ViewModels;

namespace ApiScout.Views;

/// <summary>2-4 APIs side by side: what each needs, what is free, and (on request) how fast each answers right now.</summary>
public sealed class CompareWindow : Window
{
    private readonly IReadOnlyList<ApiRow> _apis;
    private readonly MainViewModel _vm;
    private readonly List<(string Label, Func<ApiRow, string> Value)> _facts;
    private readonly Dictionary<(string, int), TextBox> _cells = [];
    private readonly string[] _docsLive, _exampleLive;
    private readonly Button _measure = new() { Content = "⏱  _Measure now", Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 0), ToolTip = "F5" };
    private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };
    private readonly CancellationTokenSource _closed = new();

    private const string DocsRow = "Docs site right now", ExampleRow = "Live example call";

    public CompareWindow(IReadOnlyList<ApiRow> apis, MainViewModel vm)
    {
        _apis = apis;
        _vm = vm;
        _docsLive = [.. apis.Select(a => a.StatusLabel.Length > 0 ? a.StatusLabel : "not measured")];
        _exampleLive = [.. apis.Select(a => a.HasExample ? "not measured" : "no known example request")];
        _facts =
        [
            ("Category", a => a.Category),
            ("To start calling it", a => a.KeyHeadline),
            ("What is free", a => $"{a.AccessLabel} - {a.AccessNote}"),
            ("Demo key", a => a.DemoKey ?? "-"),
            ("How the key is sent", a => a.KeyUsage ?? "-"),
            ("Get a key", a => a.SignupUrl ?? "-"),
            ("How to get a key", a => a.HowTo),
            ("HTTPS / CORS", a => $"{(a.HttpsLabel.Length > 0 ? a.HttpsLabel : "?")} / {(a.Cors.Length > 0 ? a.Cors : "?")}"),
            ("Health (freepublicapis)", a => a.HealthLabel.Length > 0 ? a.HealthLabel : "-"),
            (DocsRow, a => _docsLive[IndexOf(a)]),
            (ExampleRow, a => _exampleLive[IndexOf(a)]),
            ("Example request", a => a.Example ?? "-"),
            ("Docs", a => a.Url),
            ("Found in", a => a.SourcesLabel),
            ("First seen", a => a.FirstSeenLabel.Replace("first seen ", "")),
            ("My tags", a => a.HasTags ? string.Join(", ", a.Tags) : "-"),
            ("My notes", a => a.Note.Length > 0 ? a.Note : "-"),
        ];

        Title = "Compare - " + string.Join("  vs  ", apis.Select(a => a.Name));
        Width = Math.Min(360 + 330 * apis.Count, SystemParameters.WorkArea.Width - 40);
        Height = Math.Min(860, SystemParameters.WorkArea.Height - 40);
        MinWidth = 700; MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "BgBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        Content = Build();
        _measure.Click += async (_, _) => await MeasureAsync();
        PreviewKeyDown += OnKey;
        Loaded += (_, _) => _measure.Focus();
        Closed += (_, _) => { _closed.Cancel(); _closed.Dispose(); };
    }

    /// <summary>F5 measure, Ctrl+Shift+C copy as Markdown, Ctrl+1-4 open that API's docs; the scroll keys work from anywhere but inside a value.</summary>
    private void OnKey(object sender, KeyEventArgs e)
    {
        var mods = Keyboard.Modifiers;
        var key = e.Key;
        if (key == Key.F5 && _measure.IsEnabled) { _ = MeasureAsync(); e.Handled = true; }
        else if (key == Key.C && mods == (ModifierKeys.Control | ModifierKeys.Shift)) { _vm.CopyText(ToMarkdown(), "Comparison"); e.Handled = true; }
        else if (mods == ModifierKeys.Control && key is >= Key.D1 and <= Key.D4 && key - Key.D1 < _apis.Count)
        {
            _vm.OpenUrlCommand.Execute(_apis[key - Key.D1].Url);
            e.Handled = true;
        }
        else if (mods == ModifierKeys.None && e.OriginalSource is not TextBox)
        {
            double page = Math.Max(80, _scroll.ViewportHeight - 60);
            double? to = key switch
            {
                Key.Down => _scroll.VerticalOffset + 60,
                Key.Up => _scroll.VerticalOffset - 60,
                Key.PageDown => _scroll.VerticalOffset + page,
                Key.PageUp => _scroll.VerticalOffset - page,
                Key.Home => 0,
                Key.End => _scroll.ScrollableHeight,
                _ => null,
            };
            if (to is { } offset) { _scroll.ScrollToVerticalOffset(offset); e.Handled = true; }
        }
    }

    private int IndexOf(ApiRow a) { for (int i = 0; i < _apis.Count; i++) if (ReferenceEquals(_apis[i], a)) return i; return 0; }

    private UIElement Build()
    {
        var grid = new Grid { Margin = new Thickness(18, 14, 18, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        foreach (var _ in _apis) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // header row: brand tile, name, domain, open docs
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int c = 0; c < _apis.Count; c++)
        {
            var api = _apis[c];
            var head = new DockPanel { Margin = new Thickness(8, 0, 8, 12) };
            var tile = new ContentControl { Content = api, ContentTemplate = (DataTemplate)Application.Current.Resources["BrandTile"], Focusable = false, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Top };
            DockPanel.SetDock(tile, Dock.Left);
            head.Children.Add(tile);
            var open = new Button { Content = "Open docs", ToolTip = $"Ctrl+{c + 1}", FontSize = 12, Padding = new Thickness(9, 3, 9, 3), Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
            open.Click += (_, _) => _vm.OpenUrlCommand.Execute(api.Url);
            var names = new StackPanel();
            names.Children.Add(new TextBlock { Text = api.Name, FontSize = 17, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap });
            var domain = new TextBlock { Text = api.BrandDomain, FontSize = 12 };
            domain.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            names.Children.Add(domain);
            names.Children.Add(new TextBlock { Text = api.Description, TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Margin = new Thickness(0, 6, 0, 0), MaxHeight = 72, TextTrimming = TextTrimming.CharacterEllipsis });
            names.Children.Add(open);
            head.Children.Add(names);
            Grid.SetColumn(head, c + 1);
            grid.Children.Add(head);
        }

        for (int r = 0; r < _facts.Count; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var (label, value) = _facts[r];
            var band = new Border { CornerRadius = new CornerRadius(6) };
            if (r % 2 == 0) band.SetResourceReference(Border.BackgroundProperty, "CardBrush");
            Grid.SetRow(band, r + 1); Grid.SetColumnSpan(band, _apis.Count + 1);
            grid.Children.Add(band);

            var name = new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, FontSize = 12.5, Margin = new Thickness(10, 8, 8, 8), TextWrapping = TextWrapping.Wrap };
            name.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            Grid.SetRow(name, r + 1);
            grid.Children.Add(name);

            for (int c = 0; c < _apis.Count; c++)
            {
                // read-only text boxes, so any value can be selected and copied
                var cell = new TextBox
                {
                    Text = value(_apis[c]), IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0), Background = Brushes.Transparent,
                    Margin = new Thickness(4, 4, 8, 4), FontSize = 12.5, VerticalAlignment = VerticalAlignment.Top, MaxHeight = 150, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    IsTabStop = false, // 60-odd values would bury the buttons for Tab; a click still selects and copies
                };
                cell.SetResourceReference(ForegroundProperty, "TextBrush");
                _cells[(label, c)] = cell;
                Grid.SetRow(cell, r + 1); Grid.SetColumn(cell, c + 1);
                grid.Children.Add(cell);
            }
        }

        var copy = new Button { Content = "_Copy as Markdown", ToolTip = "Ctrl+Shift+C", Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 0) };
        copy.Click += (_, _) => _vm.CopyText(ToMarkdown(), "Comparison");
        var close = new Button { Content = "Close", Padding = new Thickness(14, 6, 14, 6), IsCancel = true };
        close.Click += (_, _) => Close();
        var hint = new TextBlock { Text = "Measure now (F5) checks each docs site and sends each known example request once.  ·  Ctrl+1-4 opens docs  ·  ↑ ↓ PgUp PgDn scroll  ·  Esc closes", VerticalAlignment = VerticalAlignment.Center, FontSize = 12, TextWrapping = TextWrapping.Wrap };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        var buttons = new DockPanel { Margin = new Thickness(18, 8, 18, 14), LastChildFill = true };
        foreach (var b in new UIElement[] { close, copy, _measure }) { DockPanel.SetDock(b, Dock.Right); buttons.Children.Add(b); }
        buttons.Children.Add(hint);

        var root = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        _scroll.Content = grid;
        root.Children.Add(_scroll);
        return root;
    }

    private async Task MeasureAsync()
    {
        _measure.IsEnabled = false;
        _measure.Content = "Measuring…";
        var ct = _closed.Token;
        try
        {
            await Task.WhenAll(_apis.Select(async (api, i) =>
            {
                // each column stands alone: one API failing must not leave the others unmeasured
                try
                {
                    var link = await Task.Run(() => LinkChecker.CheckAsync(api.Url, ct), ct);
                    api.LatencyMs = link.LatencyMs;
                    api.Status = link.Label;
                    _docsLive[i] = api.StatusLabel;
                    _cells[(DocsRow, i)].Text = _docsLive[i];
                    if (!api.HasExample) return;
                    var test = await Task.Run(() => ApiTester.SendAsync(api.DefaultTestUrl, api.DefaultTestHeader, ct), ct);
                    _vm.RecordOutcome(api, test);
                    _exampleLive[i] = test.Summary.Split('\n')[0] + (api.IsLimited ? $"\n{api.LimitLabel}" : "");
                    _cells[(ExampleRow, i)].Text = _exampleLive[i];
                }
                catch (OperationCanceledException) { } // the window was closed
                catch (Exception ex) { _cells[(api.HasExample && _docsLive[i] != "not measured" ? ExampleRow : DocsRow, i)].Text = "failed: " + ex.Message; }
            }));
        }
        finally { if (!ct.IsCancellationRequested) { _measure.Content = "⏱  _Measure again"; _measure.IsEnabled = true; } }
    }

    public string ToMarkdown() => ToMarkdown(_apis, _facts);

    internal static string ToMarkdown(IReadOnlyList<ApiRow> apis, IEnumerable<(string Label, Func<ApiRow, string> Value)> facts)
    {
        static string Cell(string s) => s.Replace("|", "\\|").Replace("\r", "").Replace("\n", "<br>");
        var sb = new StringBuilder("| |" + string.Concat(apis.Select(a => $" [{Cell(a.Name)}]({a.Url}) |")) + "\n|---|" + string.Concat(apis.Select(_ => "---|")) + "\n");
        foreach (var (label, value) in facts)
            sb.Append($"| **{label}** |").Append(string.Concat(apis.Select(a => $" {Cell(value(a))} |"))).Append('\n');
        return sb.ToString();
    }
}

/// <summary>One-line prompt used for "Tag the selected rows…".</summary>
public sealed class InputDialog : Window
{
    private readonly TextBox _box = new() { MinWidth = 300, Margin = new Thickness(0, 8, 0, 14) };
    private readonly PasswordBox _secret = new() { MinWidth = 300, Margin = new Thickness(0, 8, 0, 14) };
    private readonly bool _isSecret;
    public string Value => _isSecret ? _secret.Password : _box.Text.Trim();

    /// <param name="secret">Masked input (a passphrase); an empty answer is then allowed and means "without".</param>
    public InputDialog(string title, string prompt, IEnumerable<string> suggestions, string okText = "Add tag", bool secret = false)
    {
        _isSecret = secret;
        Title = title;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "BgBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");

        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = prompt, TextWrapping = TextWrapping.Wrap, MaxWidth = 340 });
        panel.Children.Add(secret ? _secret : _box);
        var existing = suggestions.ToList();
        if (existing.Count > 0)
        {
            var chips = new WrapPanel { Margin = new Thickness(0, -6, 0, 12), MaxWidth = 340 };
            foreach (var tag in existing.Take(12))
            {
                var chip = new Button { Content = tag, FontSize = 12, Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 6, 6) };
                chip.Click += (_, _) => { _box.Text = tag; _box.CaretIndex = tag.Length; _box.Focus(); };
                chips.Children.Add(chip);
            }
            panel.Children.Add(chips);
        }
        var ok = new Button { Content = okText, IsDefault = true, Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(0, 0, 8, 0) };
        ok.SetResourceReference(StyleProperty, "AccentButtonStyle");
        ok.Click += (_, _) => DialogResult = secret || Value.Length > 0;
        var cancel = new Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(16, 6, 16, 6) };
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        row.Children.Add(ok); row.Children.Add(cancel);
        panel.Children.Add(row);
        Content = panel;
        Loaded += (_, _) => { if (secret) _secret.Focus(); else _box.Focus(); };
    }
}
