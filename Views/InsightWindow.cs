using System.Windows;
using System.Windows.Controls;
using ApiScout.Services;
using ApiScout.ViewModels;

namespace ApiScout.Views;

/// <summary>"More about this API": the benefits ApiScout can state from what it knows, then what the provider's own page says.</summary>
public sealed class InsightWindow : Window
{
    private static readonly Dictionary<string, ApiInfo> Cache = []; // per session: the page is read once per API
    private readonly ApiRow _api;
    private readonly MainViewModel _vm;
    private readonly StackPanel _body = new();
    private readonly CancellationTokenSource _closed = new();
    private ApiInfo? _info;

    public InsightWindow(ApiRow api, MainViewModel vm)
    {
        _api = api;
        _vm = vm;
        Title = "More about " + api.Name;
        Width = 680;
        Height = Math.Min(820, SystemParameters.WorkArea.Height - 60);
        MinWidth = 480; MinHeight = 360;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "BgBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");

        var head = new DockPanel { Margin = new Thickness(22, 18, 22, 6) };
        var tile = new ContentControl { Content = api, ContentTemplate = (DataTemplate)Application.Current.Resources["BrandTile"], Focusable = false, Margin = new Thickness(0, 2, 12, 0), VerticalAlignment = VerticalAlignment.Top };
        DockPanel.SetDock(tile, Dock.Left);
        head.Children.Add(tile);
        var names = new StackPanel();
        names.Children.Add(new TextBlock { Text = api.Name, FontSize = 21, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap });
        names.Children.Add(Muted($"{api.BrandDomain}  ·  {api.Category}"));
        names.Children.Add(new TextBlock { Text = api.Description, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
        head.Children.Add(names);

        var open = new Button { Content = "_Open the docs", Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 0) };
        open.Click += (_, _) => _vm.OpenUrlCommand.Execute(api.Url);
        var copy = new Button { Content = "_Copy as Markdown", Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 0) };
        copy.Click += (_, _) => _vm.CopyText(ApiInsight.ToMarkdown(_api, _info), "About this API");
        var close = new Button { Content = "Close", IsCancel = true, Padding = new Thickness(14, 6, 14, 6) };
        close.Click += (_, _) => Close();
        var buttons = new DockPanel { Margin = new Thickness(22, 8, 22, 14), LastChildFill = false };
        foreach (var b in new[] { close, copy, open }) { DockPanel.SetDock(b, Dock.Right); buttons.Children.Add(b); }

        var root = new DockPanel();
        DockPanel.SetDock(head, Dock.Top); DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(head); root.Children.Add(buttons);
        root.Children.Add(new ScrollViewer { Content = _body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(22, 0, 16, 0), Focusable = false });
        Content = root;

        Closed += (_, _) => { _closed.Cancel(); _closed.Dispose(); };
        Loaded += async (_, _) => { close.Focus(); await LoadAsync(); };
        Render(reading: true);
    }

    private async Task LoadAsync()
    {
        if (!Cache.TryGetValue(_api.Key, out _info))
        {
            try
            {
                var entry = _api.Entry;
                var token = _closed.Token;
                _info = await Task.Run(() => ApiInsight.ReadAsync(entry, token), token);
                if (_info.Error is null) Cache[_api.Key] = _info;
            }
            catch (OperationCanceledException) { return; } // closed while reading
        }
        Render(reading: false);
    }

    private void Render(bool reading)
    {
        _body.Children.Clear();
        Section("AT A GLANCE  -  what it means for you", "UpBrush");
        foreach (var b in ApiInsight.Benefits(_api)) Bullet(b);

        Section("IN THE PROVIDER'S WORDS", "AccentBrush");
        if (reading) { _body.Children.Add(Muted("Reading the provider's page…")); return; }
        if (_info is null) return;
        if (_info.Summary.Length > 0) _body.Children.Add(new TextBlock { Text = _info.Summary, TextWrapping = TextWrapping.Wrap, LineHeight = 21 });
        if (_info.Error is not null) _body.Children.Add(Muted(_info.Error));
        else if (_info.Summary.Length == 0) _body.Children.Add(Muted("The page has no summary paragraph ApiScout could pick out."));

        if (_info.Features.Count > 0)
        {
            Section("FEATURES  -  from the page's own lists", "VioletBrush");
            foreach (var f in _info.Features) Bullet(f);
        }
        if (_info.Sections.Count > 0)
        {
            Section("THE DOCS COVER", "MutedBrush");
            _body.Children.Add(new TextBlock { Text = string.Join("   ·   ", _info.Sections), TextWrapping = TextWrapping.Wrap, LineHeight = 21 });
        }
        var source = Muted($"Read just now from {_info.Source} - shown as the provider wrote it, nothing added.");
        source.Margin = new Thickness(0, 16, 0, 12);
        _body.Children.Add(source);
    }

    private void Section(string title, string brush)
    {
        var t = new TextBlock { Text = title, FontSize = 12, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 18, 0, 8) };
        t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        _body.Children.Add(t);
    }

    private void Bullet(string text)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var dot = new TextBlock { Text = "•", Margin = new Thickness(2, 0, 10, 0) };
        dot.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        DockPanel.SetDock(dot, Dock.Left);
        row.Children.Add(dot);
        row.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, LineHeight = 20 });
        _body.Children.Add(row);
    }

    private static TextBlock Muted(string text)
    {
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12.5 };
        t.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        return t;
    }
}
