using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinRT.Interop;

namespace WallpaperScheduler.App;

public sealed partial class MainWindow : Window
{
    public IntPtr Hwnd => WindowNative.GetWindowHandle(this);
    private readonly TrayIcon _tray;

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    public MainWindow()
    {
        InitializeComponent();
        Title = "Wallpaper Scheduler: Time of Day";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (File.Exists(iconPath)) AppWindow.SetIcon(iconPath);

        SizeAndCenterWindow();

        _tray = new TrayIcon(
            Hwnd,
            "Wallpaper Scheduler: Time of Day",
            iconPath,
            onOpen: () => { AppWindow.Show(); SetForegroundWindow(Hwnd); },
            onClose: Close);
        Closed += (_, _) => _tray.Dispose();

        ApplyAppTheme(AppState.Config.AppTheme);
        Nav.SelectedItem = Nav.MenuItems[0];
    }

    public void ApplyAppTheme(Core.AppThemeMode mode)
    {
        if (Content is FrameworkElement root)
        {
            root.RequestedTheme = mode switch
            {
                Core.AppThemeMode.Light => ElementTheme.Light,
                Core.AppThemeMode.Dark => ElementTheme.Dark,
                _ => ElementTheme.Default,
            };
        }
    }

    private void SizeAndCenterWindow()
    {
        if (AppWindow.Presenter is OverlappedPresenter p)
        {
            p.IsResizable = true;
            p.IsMaximizable = true;
            p.IsMinimizable = true;
        }

        double scale = Math.Max(1.0, GetDpiForWindow(Hwnd) / 96.0);
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;

        int w = Math.Min((int)(1100 * scale), area.Width - (int)(64 * scale));
        int h = Math.Min((int)(780 * scale), area.Height - (int)(64 * scale));
        w = Math.Max(w, Math.Min(900, area.Width));
        h = Math.Max(h, Math.Min(640, area.Height));

        int x = area.X + (area.Width - w) / 2;
        int y = area.Y + (area.Height - h) / 2;
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(x, y, w, h));
    }

    // Pages are built in code (no XAML), so they are created directly instead of via Frame.Navigate.
    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item) return;
        ContentFrame.Content = (item.Tag as string) switch
        {
            "displays" => new DisplaysPage(),
            "tips" => new TipsPage(),
            "settings" => new SettingsPage(),
            _ => new SchedulePage(),
        };
    }
}
