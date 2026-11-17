using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace WallpaperScheduler.Core;

/// <summary>Applies Windows 11 Light/Dark mode via the Personalize registry key and broadcasts WM_SETTINGCHANGE.</summary>
public static class ThemeService
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const int HWND_BROADCAST = 0xFFFF;
    private const uint WM_SETTINGCHANGE = 0x001A;
    private const uint SMTO_ABORTIFHUNG = 0x0002;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd, uint msg, UIntPtr wParam, string lParam,
        uint fuFlags, uint uTimeout, out UIntPtr lpdwResult);

    public static void Apply(WindowsThemeMode mode)
    {
        if (mode == WindowsThemeMode.LeaveAlone) return;

        int light = mode == WindowsThemeMode.Light ? 1 : 0;
        using var key = Registry.CurrentUser.CreateSubKey(PersonalizeKey);
        key?.SetValue("AppsUseLightTheme", light, RegistryValueKind.DWord);
        key?.SetValue("SystemUsesLightTheme", light, RegistryValueKind.DWord);

        SendMessageTimeout(new IntPtr(HWND_BROADCAST), WM_SETTINGCHANGE, UIntPtr.Zero, "ImmersiveColorSet", SMTO_ABORTIFHUNG, 500, out _);
    }
}
