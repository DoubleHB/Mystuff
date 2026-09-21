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

        _vm.ShowTestCard += () => Dispatcher.BeginInvoke(() => TestCard.BringIntoView(), System.Windows.Threading.DispatcherPriority.Background);
        InputBindings.Add(new KeyBinding(new ActionCommand(() => { SearchBox.Focus(); SearchBox.SelectAll(); }), Key.F, ModifierKeys.Control));
        Closing += (_, _) =>
        {
            s.Maximised = WindowState == WindowState.Maximized;
            if (WindowState == WindowState.Normal) { s.Width = Width; s.Height = Height; }
            _vm.SaveSourceSettings();
        };
    }

    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        var s = App.Store.Settings;
        s.Dark = !s.Dark;
        App.ApplyTheme(s.Dark);
        App.Store.SaveSettings();
        ResultsGrid.Items.Refresh(); // badge brushes come from converters, so re-evaluate them
    }

    private void SourcesPopup_Closed(object sender, EventArgs e) => _vm.SaveSourceSettings();

    private void Grid_Copy(object sender, ExecutedRoutedEventArgs e) =>
        _vm.CopyRows([.. ResultsGrid.SelectedItems.OfType<ApiRow>()]);

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
