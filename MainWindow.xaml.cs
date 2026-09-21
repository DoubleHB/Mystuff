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
        InputBindings.Add(new KeyBinding(new ActionCommand(() => Changes_Click(this, new RoutedEventArgs())), Key.H, ModifierKeys.Control));
        _vm.CommitEdits += CommitFocusedTextBox;

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
        };
        Closing += (_, _) =>
        {
            s.Maximised = WindowState == WindowState.Maximized;
            if (WindowState == WindowState.Normal) { s.Width = Width; s.Height = Height; }
            _vm.Flush(); // closing a window does not make the focused box lose focus, so a note or key being typed is committed by hand
            _vm.SaveSourceSettings();
        };
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

    /// <summary>Single keys on a shortlist card. A focused button inside the card keeps Enter and Space for itself.</summary>
    private void Shortlist_PreviewKeyDown(object sender, KeyEventArgs e)
    {
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

    private Views.ChangesWindow? _changes;

    private void Changes_Click(object sender, RoutedEventArgs e)
    {
        // one window at a time; a second press brings it forward
        if (_changes is { IsLoaded: true }) { _changes.Activate(); return; }
        _changes = new Views.ChangesWindow(_vm) { Owner = this };
        _changes.Closed += (_, _) => _changes = null;
        _changes.Show();
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
