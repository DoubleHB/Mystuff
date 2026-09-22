using System.Windows;
using ApiScout.ViewModels;
using Forms = System.Windows.Forms;

namespace ApiScout.UI;

/// <summary>
/// Tray mode: the notification-area icon with its menu (Open, Scan now, API of the day, My shortlist, Exit) and the
/// "new APIs found" balloon from the running app. Minimising then hides the window instead of leaving it on the taskbar.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ToolStripMenuItem _apiOfTheDay = new("API of the day");
    private readonly MainWindow _window;
    private readonly MainViewModel _vm;
    private Action? _onBalloonClick;

    public TrayIcon(MainWindow window, MainViewModel vm)
    {
        _window = window;
        _vm = vm;
        var open = new Forms.ToolStripMenuItem("Open ApiScout", null, (_, _) => _window.ShowFromTray());
        open.Font = new System.Drawing.Font(open.Font, System.Drawing.FontStyle.Bold);
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(open);
        menu.Items.Add(new Forms.ToolStripMenuItem("Scan now", null, (_, _) => Scan()));
        _apiOfTheDay.Click += (_, _) => { _window.ShowFromTray(); _vm.ShowApiOfTheDayCommand.Execute(null); _vm.TestApiOfTheDayCommand.Execute(null); };
        menu.Items.Add(_apiOfTheDay);
        menu.Items.Add(new Forms.ToolStripMenuItem("My shortlist", null, (_, _) => { _window.ShowFromTray(); _vm.ShowShortlist = true; }));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(new Forms.ToolStripMenuItem("Exit", null, (_, _) => _window.Close()));
        menu.Opening += (_, _) =>
        {
            _apiOfTheDay.Text = _vm.ApiOfTheDay is { } api ? $"API of the day: {api.Name}" : "API of the day (scan first)";
            _apiOfTheDay.Enabled = _vm.ApiOfTheDay is not null;
        };

        _icon = new Forms.NotifyIcon
        {
            Icon = Environment.ProcessPath is { } exe ? System.Drawing.Icon.ExtractAssociatedIcon(exe) : System.Drawing.SystemIcons.Application,
            Text = "ApiScout - free API finder",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) _window.ShowFromTray(); };
        _icon.BalloonTipClicked += (_, _) => { var act = _onBalloonClick; _onBalloonClick = null; _window.ShowFromTray(); act?.Invoke(); };
    }

    private void Scan()
    {
        if (_vm.IsBusy) { Notify("ApiScout is already busy", "A scan or link check is running.", null); return; }
        _vm.ScanCommand.Execute(null); // the result arrives as a balloon (new APIs) or stays quiet (nothing new)
    }

    public void Notify(string title, string text, Action? onClick)
    {
        _onBalloonClick = onClick;
        _icon.ShowBalloonTip(10000, title, text, Forms.ToolTipIcon.Info);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
    }
}
