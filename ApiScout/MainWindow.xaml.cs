using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ApiScout.ViewModels;

namespace ApiScout;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        DataContext = _vm = vm;

        var s = App.Store.Settings;
        if (s.Width >= MinWidth && s.Height >= MinHeight)
        {
            Width = Math.Min(s.Width, SystemParameters.VirtualScreenWidth);
            Height = Math.Min(s.Height, SystemParameters.VirtualScreenHeight);
        }
        if (s.Maximised) WindowState = WindowState.Maximized;

        _vm.ScrollToSelected += () => Dispatcher.BeginInvoke(() => { if (ResultsGrid.SelectedItem is { } item) ResultsGrid.ScrollIntoView(item); }, System.Windows.Threading.DispatcherPriority.Background);
        _vm.ShowTestCard += () => Dispatcher.BeginInvoke(() => TestCard.BringIntoView(), System.Windows.Threading.DispatcherPriority.Background);
        InputBindings.Add(new KeyBinding(new ActionCommand(() => { _vm.ShowShortlist = false; SearchBox.Focus(); SearchBox.SelectAll(); }), Key.F, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new ActionCommand(() => _vm.ShowShortlist = !_vm.ShowShortlist), Key.L, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new ActionCommand(() => { if (!_vm.ShowShortlist) Compare_Click(this, new RoutedEventArgs()); }), Key.M, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new ActionCommand(() => { if (!_vm.ShowShortlist) TagSelected_Click(this, new RoutedEventArgs()); }), Key.G, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new ActionCommand(() => About_Click(this, new RoutedEventArgs())), Key.F1, ModifierKeys.None));
        InputBindings.Add(new KeyBinding(new ActionCommand(() => { if (!_vm.ShowShortlist) Insight_Click(this, new RoutedEventArgs()); }), Key.I, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new ActionCommand(() => { if (!_vm.ShowShortlist) AddToCollection_Click(this, new RoutedEventArgs()); }), Key.E, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new ActionCommand(() => Changes_Click(this, new RoutedEventArgs())), Key.H, ModifierKeys.Control));
        SizeChanged += (_, _) => ShowTourStop();
        Loaded += (_, _) => { if (!App.Store.Settings.TourSeen && _vm.IsEmpty) Dispatcher.BeginInvoke(StartTour, System.Windows.Threading.DispatcherPriority.ApplicationIdle); };
        // ---- tray mode
        if (_vm.TrayMode) _tray = new UI.TrayIcon(this, _vm);
        StateChanged += (_, _) =>
        {
            if (WindowState != WindowState.Minimized) _restoreTo = WindowState;
            else if (_tray is not null) Hide(); // off the taskbar; the tray icon brings it back
        };
        Closed += (_, _) => _tray?.Dispose();
        _vm.NewApisFound += (added, names) =>
        {
            if (_tray is null || (IsVisible && IsActive)) return; // looking at the window: the toast there is enough
            var text = string.Join(", ", names) + (added > names.Count ? $" and {added - names.Count:N0} more" : "") + ". Click to see them.";
            _tray.Notify($"{added:N0} new free API{(added == 1 ? "" : "s")} found", text, _vm.ShowNewCategory);
            _vm.Log($"Tray notification: {added} new - {text}");
        };
        _vm.CommitEdits += CommitFocusedTextBox;
        // opening the popup leaves the focus on its button, so Esc is caught here rather than inside the popup
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && SourcesPopup.IsOpen) { SourcesButton.IsChecked = false; e.Handled = true; } };

        // Replacing the grid's ItemsSource (every filter change does) makes WPF drop the sort the user clicked: note it, put it back
        _vm.PropertyChanging += (_, e) =>
        {
            if (e.PropertyName != nameof(MainViewModel.Rows)) return;
            _sort = [.. ResultsGrid.Items.SortDescriptions];
            _sortArrows = ResultsGrid.Columns.ToDictionary(c => c, c => c.SortDirection);
        };
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(MainViewModel.Rows) || _sort.Count == 0) return;
            var (sort, arrows) = (_sort, _sortArrows);
            Dispatcher.BeginInvoke(() =>
            {
                if (ResultsGrid.Items.SortDescriptions.Count > 0) return;
                foreach (var d in sort) ResultsGrid.Items.SortDescriptions.Add(d);
                foreach (var (column, direction) in arrows) column.SortDirection = direction;
            }, System.Windows.Threading.DispatcherPriority.DataBind);
        };

        _vm.PickClientRequests = (name, worked, preselected) =>
        {
            var dialog = new Views.ClientDialog(name, worked, preselected) { Owner = this };
            return dialog.ShowDialog() == true ? dialog.Picked : null;
        };
        _vm.PropertyChanged += (_, e) =>
        {
            // the keyboard follows the page: onto the selected card, or back onto the selected grid row
            if (e.PropertyName is nameof(MainViewModel.ShowShortlist) or nameof(MainViewModel.ShortlistRows)) Dispatcher.BeginInvoke(FocusCurrentPage, System.Windows.Threading.DispatcherPriority.Input);
            if (e.PropertyName == nameof(MainViewModel.TrayMode))
            {
                if (_vm.TrayMode) _tray ??= new UI.TrayIcon(this, _vm);
                else { _tray?.Dispose(); _tray = null; if (!IsVisible) ShowFromTray(); }
            }
        };
        Closing += (_, _) =>
        {
            s.Maximised = (WindowState == WindowState.Minimized ? _restoreTo : WindowState) == WindowState.Maximized; // Exit from the tray menu closes a hidden, "minimised" window
            if (WindowState == WindowState.Normal) { s.Width = Width; s.Height = Height; }
            _vm.Flush(); // closing a window does not make the focused box lose focus, so a note or key being typed is committed by hand
            _vm.SaveSourceSettings();
        };
    }

    private UI.TrayIcon? _tray;
    private WindowState _restoreTo = WindowState.Normal;

    /// <summary>Back from the tray (icon click, menu, balloon, or a second ApiScout being started).</summary>
    public void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = _restoreTo;
        Activate();
        Topmost = true; Topmost = false; // Activate alone may only flash the taskbar button
    }

    private List<System.ComponentModel.SortDescription> _sort = [];
    private Dictionary<DataGridColumn, System.ComponentModel.ListSortDirection?> _sortArrows = [];

    /// <summary>Notes and tags save when their box loses focus; this does it for the box being typed in right now.</summary>
    private static void CommitFocusedTextBox() =>
        (Keyboard.FocusedElement as TextBox)?.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();

    private void SourcesPopup_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        SourcesButton.IsChecked = false; // Esc closes the popup instead of reaching the window, where it would stop a running scan
        SourcesButton.Focus();
        e.Handled = true;
    }

    private void FocusCurrentPage()
    {
        if (SearchBox.IsKeyboardFocusWithin) return; // Ctrl+F from the shortlist page: the search box keeps the keyboard
        if (_vm.ShowShortlist)
        {
            ShortlistBox.UpdateLayout();
            if (ShortlistBox.SelectedItem is { } card && ShortlistBox.ItemContainerGenerator.ContainerFromItem(card) is ListBoxItem item) { item.BringIntoView(); item.Focus(); }
            else ShortlistBox.Focus();
        }
        else if (ResultsGrid.SelectedItem is { } row && ResultsGrid.ItemContainerGenerator.ContainerFromItem(row) is DataGridRow gridRow)
            gridRow.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        else ResultsGrid.Focus();
    }

    // ---- drag a collection card onto another to reorder
    private Point _dragFrom;
    private ApiRow? _dragRow;

    private static ApiRow? CardAt(object source) => (source as FrameworkElement)?.DataContext as ApiRow ?? ((source as FrameworkContentElement)?.Parent as FrameworkElement)?.DataContext as ApiRow;

    private void Shortlist_MouseDown(object sender, MouseButtonEventArgs e)
    {
        // a press on one of the card's buttons is a click, never the start of a drag
        bool onButton = false;
        for (var d = e.OriginalSource as DependencyObject; d is not null and not ListBoxItem; d = d is System.Windows.Media.Visual ? System.Windows.Media.VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d is System.Windows.Controls.Primitives.ButtonBase) { onButton = true; break; }
        _dragRow = _vm.IsCollectionPage && !onButton ? CardAt(e.OriginalSource) : null;
        _dragFrom = e.GetPosition(null);
    }

    private void Shortlist_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragRow is null || e.LeftButton != MouseButtonState.Pressed) return;
        var moved = e.GetPosition(null) - _dragFrom;
        if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var row = _dragRow;
        _dragRow = null;
        DragDrop.DoDragDrop(ShortlistBox, new DataObject(typeof(ApiRow), row), DragDropEffects.Move);
    }

    private void Shortlist_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(ApiRow)) && CardAt(e.OriginalSource) is not null ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void Shortlist_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(ApiRow)) is ApiRow row && CardAt(e.OriginalSource) is { } target) _vm.MoveInCollection(row, target);
        e.Handled = true;
    }

    /// <summary>Single keys on a shortlist card. A focused button inside the card keeps Enter and Space for itself.</summary>
    private void Shortlist_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            // Ctrl+arrow carries the card along (up / down behave like left / right: the order is one line that wraps)
            e.Handled = _vm.MoveSelectedInCollection(e.Key is Key.Left or Key.Up ? -1 : 1) || _vm.IsCollectionPage;
            return;
        }
        // the card the key was pressed on (a button inside another card may have the focus), else the selected one
        if (Keyboard.Modifiers != ModifierKeys.None || ((e.OriginalSource as FrameworkElement)?.DataContext as ApiRow ?? _vm.ShortlistSelected) is not { } row) return;
        if (e.OriginalSource is System.Windows.Controls.Primitives.ButtonBase && e.Key is Key.Enter or Key.Space) return;
        switch (e.Key)
        {
            case Key.Enter: _vm.ShowRowCommand.Execute(row); break;
            case Key.T: _vm.TestRowCommand.Execute(row); break;
            case Key.K: _vm.CopyRowKeyCommand.Execute(row); break;
            case Key.O: _vm.OpenRowCommand.Execute(row); break;
            case Key.U: _vm.ShortlistSelected = row; _vm.CopyCommand.Execute("url"); break;
            case Key.D: _vm.ToggleRowFavouriteCommand.Execute(row); break;
            case Key.R when _vm.IsCollectionPage: _vm.RemoveFromCollectionCommand.Execute(row); break;
            default: return;
        }
        e.Handled = true;
    }

    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        var s = App.Store.Settings;
        s.Dark = !s.Dark;
        App.ApplyTheme(s.Dark);
        App.Store.SaveSettings();
        ResultsGrid.Items.Refresh(); // badge brushes come from converters, so re-evaluate them
        _vm.ThemeChanged();          // ... and the ones in the detail panel and on the shortlist cards
    }

    private void SourcesPopup_Closed(object sender, EventArgs e) => _vm.SaveSourceSettings();

    private void Grid_Copy(object sender, ExecutedRoutedEventArgs e) =>
        _vm.CopyRows([.. ResultsGrid.SelectedItems.OfType<ApiRow>()]);

    private void About_Click(object sender, RoutedEventArgs e) => new Views.AboutWindow(_vm, App.Store) { Owner = this }.ShowDialog();

    // ---- first-run tour

    private sealed record TourStop(Func<FrameworkElement> Target, string Title, string Text);
    private TourStop[] _tour = [];
    private int _tourAt;

    /// <summary>Shown by itself on the very first start (no catalogue yet); About can show it again.</summary>
    public void StartTour()
    {
        _vm.ShowShortlist = false;
        _tour =
        [
            new(() => ScanButton, "Start here: Scan the internet",
                "ApiScout reads the big public API directories, merges them and sorts a few thousand free APIs into categories. It takes about ten seconds (F5). Under Sources you choose where it looks and how often it re-scans by itself."),
            new(() => DashboardPanel, "The dashboard",
                "How many APIs are completely free, have a free tier, or only a trial; what is new this week; what is rate limited. Every tile is a shortcut - click to filter, click again to clear. On the right, an API of the day you can test with one click."),
            new(() => DetailArea, "Keys, and Try it",
                "Select an API and this side shows whether it needs a key, a demo key if the provider publishes one, or how to get your own. \"Try it\" sends a real request and shows the JSON - and can turn what worked into C# classes or a small typed client."),
            new(() => ShortlistButton, "My shortlist and collections",
                "Star an API (Ctrl+D), tag it, or put it in a collection: it appears here as a card with its key and last test result. \"Test all\" checks a whole page at once. That is the tour - F1 lists every shortcut."),
        ];
        _tour = [.. _tour.Where(s => s.Target() is { IsVisible: true, ActualWidth: > 0 })];
        if (_tour.Length == 0) return;
        _tourAt = 0;
        TourLayer.Visibility = Visibility.Visible;
        ShowTourStop();
        TourLayer.Focus();
    }

    private void ShowTourStop()
    {
        if (TourLayer.Visibility != Visibility.Visible || _tour.Length == 0) return;
        var stop = _tour[_tourAt];
        var target = stop.Target();
        var root = (FrameworkElement)Content;
        var box = target.TransformToAncestor(root).TransformBounds(new Rect(0, 0, target.ActualWidth, target.ActualHeight));
        box.Inflate(6, 6);
        box.Intersect(new Rect(2, 2, root.ActualWidth - 4, root.ActualHeight - 4));

        TourDim.Data = new System.Windows.Media.CombinedGeometry(System.Windows.Media.GeometryCombineMode.Exclude,
            new System.Windows.Media.RectangleGeometry(new Rect(0, 0, root.ActualWidth, root.ActualHeight)), new System.Windows.Media.RectangleGeometry(box, 10, 10));
        Canvas.SetLeft(TourRing, box.Left); Canvas.SetTop(TourRing, box.Top);
        TourRing.Width = box.Width; TourRing.Height = box.Height;

        TourStep.Text = $"Tour  ·  {_tourAt + 1} of {_tour.Length}";
        TourTitle.Text = stop.Title;
        TourText.Text = stop.Text;
        TourBack.Visibility = _tourAt == 0 ? Visibility.Collapsed : Visibility.Visible;
        TourNext.Content = _tourAt == _tour.Length - 1 ? "Done" : "Next";

        // below the target if there is room, else above; beside it when the target is as tall as the window
        TourCallout.Measure(new Size(TourCallout.Width, double.PositiveInfinity));
        double h = TourCallout.DesiredSize.Height, w = TourCallout.Width;
        double left = Math.Clamp(box.Left + box.Width / 2 - w / 2, 12, Math.Max(12, root.ActualWidth - w - 12));
        double top = box.Bottom + 12;
        if (top + h > root.ActualHeight - 12) top = box.Top - h - 12;
        if (top < 12) { top = Math.Clamp(box.Top + 40, 12, Math.Max(12, root.ActualHeight - h - 12)); left = box.Left - w - 16 > 12 ? box.Left - w - 16 : Math.Min(box.Right + 16, root.ActualWidth - w - 12); }
        Canvas.SetLeft(TourCallout, left); Canvas.SetTop(TourCallout, top);
    }

    private void EndTour()
    {
        TourLayer.Visibility = Visibility.Collapsed;
        App.Store.Settings.TourSeen = true;
        App.Store.SaveSettings();
        ScanButton.Focus();
    }

    private void TourNext_Click(object sender, RoutedEventArgs e) { if (_tourAt >= _tour.Length - 1) EndTour(); else { _tourAt++; ShowTourStop(); } }
    private void TourBack_Click(object sender, RoutedEventArgs e) { if (_tourAt > 0) { _tourAt--; ShowTourStop(); } }
    private void TourSkip_Click(object sender, RoutedEventArgs e) => EndTour();

    private void Tour_KeyDown(object sender, KeyEventArgs e)
    {
        // the tour owns the keyboard while it is up, so F5 or Ctrl+L cannot act on the dimmed window behind it
        switch (e.Key)
        {
            case Key.Escape: EndTour(); break;
            case Key.Right or Key.Enter or Key.Space when e.OriginalSource is not Button: TourNext_Click(sender, e); break;
            case Key.Left: TourBack_Click(sender, e); break;
            case Key.Tab or Key.Enter or Key.Space: return;
        }
        e.Handled = true;
    }

    private void Insight_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Selected is { } api) new Views.InsightWindow(api, _vm) { Owner = this }.Show();
    }

    private Views.ChangesWindow? _changes;

    private void Changes_Click(object sender, RoutedEventArgs e)
    {
        // one window at a time; a second press brings it forward
        if (_changes is { IsLoaded: true }) { _changes.Activate(); return; }
        _changes = new Views.ChangesWindow(_vm) { Owner = this };
        _changes.Closed += (_, _) => _changes = null;
        _changes.Show();
    }

    private void AddToCollection_Click(object sender, RoutedEventArgs e)
    {
        var rows = ResultsGrid.SelectedItems.OfType<ApiRow>().ToList();
        if (rows.Count == 0) return;
        var dialog = new Views.InputDialog("Add to a collection", $"Collection for the {rows.Count} selected API{(rows.Count == 1 ? "" : "s")} - pick one or type a new name:", _vm.CollectionNames, "Add") { Owner = this };
        if (dialog.ShowDialog() == true) _vm.AddToCollection(rows, dialog.Value);
    }

    private void RenameCollection_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Views.InputDialog("Rename collection", $"New name for \"{_vm.ShortlistTitle}\":", [], "Rename") { Owner = this };
        if (dialog.ShowDialog() == true && !_vm.RenameCollection(dialog.Value)) _vm.Notify("That name is already taken (or too long)");
    }

    private void DeleteCollection_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, $"Delete the collection \"{_vm.ShortlistTitle}\"?\n\nOnly the group goes - the APIs, their keys, notes and tests stay.", "ApiScout", MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK)
            _vm.DeleteCollection();
    }

    private void TagSelected_Click(object sender, RoutedEventArgs e)
    {
        var rows = ResultsGrid.SelectedItems.OfType<ApiRow>().ToList();
        if (rows.Count == 0) return;
        var dialog = new Views.InputDialog("Tag the selected rows", $"Add one tag to the {rows.Count} selected API{(rows.Count == 1 ? "" : "s")}:", _vm.TagFilters.Skip(1)) { Owner = this };
        if (dialog.ShowDialog() == true) _vm.AddTag(rows, dialog.Value);
    }

    private void Compare_Click(object sender, RoutedEventArgs e)
    {
        var rows = ResultsGrid.SelectedItems.OfType<ApiRow>().ToList();
        if (rows.Count is < 2 or > 4) { _vm.Notify("Select 2 to 4 rows first (Ctrl+click), then Compare"); return; }
        new Views.CompareWindow(rows, _vm) { Owner = this }.Show();
    }

    private void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement(ResultsGrid, d) is DataGridRow)
            _vm.OpenCommand.Execute("docs");
    }

    private sealed class ActionCommand(Action run) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => run();
    }
}
