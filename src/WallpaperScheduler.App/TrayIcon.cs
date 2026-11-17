using System.Runtime.InteropServices;

namespace WallpaperScheduler.App;

/// <summary>
/// Shows a system-tray icon only while the Settings window is open.
/// Clicking brings the window to front; right-clicking offers Open or Close.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private const int WmUser = 0x0400;
    private const int WmTrayCallback = WmUser + 42;
    private const int WmLButtonUp = 0x0202;
    private const int WmRButtonUp = 0x0205;
    private const int NimAdd = 0x00000000;
    private const int NimDelete = 0x00000002;
    private const int NifMessage = 0x00000001;
    private const int NifIcon = 0x00000002;
    private const int NifTip = 0x00000004;
    private const uint TpmReturnCmd = 0x0100;
    private const uint TpmRightButton = 0x0002;
    private const int CmdOpen = 1;
    private const int CmdClose = 2;

    private delegate IntPtr SubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, UIntPtr dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, UIntPtr uIdSubclass, UIntPtr dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, UIntPtr uIdSubclass);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(int dwMessage, ref NotifyIconData lpData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImage(IntPtr hInst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, UIntPtr uIDNewItem, string lpNewItem);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenuEx(IntPtr hMenu, uint uFlags, int x, int y, IntPtr hWnd, IntPtr lptpm);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point pt);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X, Y; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public int uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    private readonly IntPtr _hwnd;
    private readonly Action _onOpen;
    private readonly Action _onClose;
    private readonly SubclassProc _proc; // keep delegate alive
    private IntPtr _hIcon;
    private bool _added;

    public TrayIcon(IntPtr hwnd, string tooltip, string iconPath, Action onOpen, Action onClose)
    {
        _hwnd = hwnd;
        _onOpen = onOpen;
        _onClose = onClose;
        _proc = WindowProc;

        if (File.Exists(iconPath))
            _hIcon = LoadImage(IntPtr.Zero, iconPath, 1 /* IMAGE_ICON */, 32, 32, 0x0010 /* LR_LOADFROMFILE */);

        SetWindowSubclass(_hwnd, _proc, (UIntPtr)1, UIntPtr.Zero);

        var data = MakeData(tooltip);
        _added = Shell_NotifyIcon(NimAdd, ref data);
    }

    private NotifyIconData MakeData(string tip = "") => new()
    {
        cbSize = Marshal.SizeOf<NotifyIconData>(),
        hWnd = _hwnd,
        uID = 1,
        uFlags = NifMessage | NifIcon | NifTip,
        uCallbackMessage = WmTrayCallback,
        hIcon = _hIcon,
        szTip = tip,
    };

    private IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, UIntPtr uId, UIntPtr refData)
    {
        if (msg == WmTrayCallback)
        {
            int evt = lParam.ToInt32() & 0xFFFF;
            if (evt == WmLButtonUp)
            {
                _onOpen();
            }
            else if (evt == WmRButtonUp)
            {
                ShowContextMenu();
            }
        }
        return DefSubclassProc(hWnd, msg, wParam, lParam);
    }

    private void ShowContextMenu()
    {
        var menu = CreatePopupMenu();
        AppendMenu(menu, 0, (UIntPtr)CmdOpen, "Open Wallpaper Scheduler");
        AppendMenu(menu, 0, (UIntPtr)CmdClose, "Close window");
        GetCursorPos(out var pt);
        SetForegroundWindow(_hwnd);
        int cmd = TrackPopupMenuEx(menu, TpmReturnCmd | TpmRightButton, pt.X, pt.Y, _hwnd, IntPtr.Zero);
        DestroyMenu(menu);

        if (cmd == CmdOpen) _onOpen();
        else if (cmd == CmdClose) _onClose();
    }

    public void Dispose()
    {
        if (_added)
        {
            var data = MakeData();
            Shell_NotifyIcon(NimDelete, ref data);
            _added = false;
        }
        RemoveWindowSubclass(_hwnd, _proc, (UIntPtr)1);
        if (_hIcon != IntPtr.Zero) { DestroyIcon(_hIcon); _hIcon = IntPtr.Zero; }
    }
}
