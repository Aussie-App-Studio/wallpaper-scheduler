using System.Runtime.InteropServices;
using WallpaperScheduler.Core.Interop;

namespace WallpaperScheduler.Core;

public sealed record MonitorInfo(string Id, int Width, int Height, string CurrentWallpaper);

/// <summary>Thin wrapper over the Windows IDesktopWallpaper COM API.</summary>
public sealed class WallpaperService
{
    private static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff", ".webp", ".heic", ".jxr" };

    private static IDesktopWallpaper Create() =>
        (IDesktopWallpaper)Activator.CreateInstance(Type.GetTypeFromCLSID(Native.CLSID_DesktopWallpaper)!)!;

    public IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var dw = Create();
        try
        {
            var list = new List<MonitorInfo>();
            uint n = dw.GetMonitorDevicePathCount();
            for (uint i = 0; i < n; i++)
            {
                var id = dw.GetMonitorDevicePathAt(i);
                if (string.IsNullOrEmpty(id)) continue; // disconnected / mirrored
                var r = dw.GetMonitorRECT(id);
                list.Add(new MonitorInfo(id, r.Right - r.Left, r.Bottom - r.Top, dw.GetWallpaper(id)));
            }
            return list;
        }
        finally { Marshal.FinalReleaseComObject(dw); }
    }

    public static bool IsImage(string file) =>
        ImageExtensions.Contains(System.IO.Path.GetExtension(file), StringComparer.OrdinalIgnoreCase);

    public static bool SlotIsUsable(Slot slot) =>
        slot.IsFolder
            ? Directory.EnumerateFiles(slot.Path).Any(IsImage)
            : File.Exists(slot.Path) && IsImage(slot.Path);

    /// <summary>Applies a slot. Returns false (and changes nothing) if its picture/folder is missing.</summary>
    public bool Apply(Slot slot, bool sameOnAllDisplays = true)
    {
        if (!SlotIsUsable(slot)) return false;

        var dw = Create();
        try
        {
            dw.SetPosition(ToPosition(slot.Fit));

            if (slot.IsFolder)
            {
                var array = slot.Shuffle ? FolderArray(slot.Path) : OrderedArray(SlotImages.GetOrdered(slot));
                dw.SetSlideshow(array);
                dw.SetSlideshowOptions(slot.Shuffle ? 1u : 0u, (uint)Math.Max(1, slot.SlideshowIntervalMinutes) * 60_000u);
                Marshal.FinalReleaseComObject(array);
            }
            else
            {
                dw.SetWallpaper(null, slot.Path);
            }

            if (!sameOnAllDisplays)
            {
                foreach (var (monitorId, path) in slot.MonitorPaths)
                {
                    var file = PickImage(path);
                    if (file != null) dw.SetWallpaper(monitorId, file);
                }
            }
            ThemeService.Apply(slot.Theme);
            return true;
        }
        finally { Marshal.FinalReleaseComObject(dw); }
    }

    /// <summary>Advances the active Windows wallpaper slideshow to the next picture.</summary>
    public void AdvanceSlideshow()
    {
        var dw = Create();
        try { dw.AdvanceSlideshow(null, 0); }
        finally { Marshal.FinalReleaseComObject(dw); }
    }

    /// <summary>The whole folder as the slideshow source (Windows shuffles it and sees new files).</summary>
    private static IShellItemArray FolderArray(string folder)
    {
        var iid = Native.IID_IShellItem;
        Native.SHCreateItemFromParsingName(folder, IntPtr.Zero, ref iid, out var item);
        var aiid = Native.IID_IShellItemArray;
        Native.SHCreateShellItemArrayFromShellItem(item, ref aiid, out var array);
        Marshal.FinalReleaseComObject(item);
        return array;
    }

    /// <summary>An explicit list of pictures, played in exactly this order.</summary>
    private static IShellItemArray OrderedArray(IReadOnlyList<string> files)
    {
        var pidls = new List<IntPtr>();
        try
        {
            foreach (var f in files)
            {
                Native.SHParseDisplayName(f, IntPtr.Zero, out var pidl, 0, out _);
                pidls.Add(pidl);
            }
            Native.SHCreateShellItemArrayFromIDLists((uint)pidls.Count, pidls.ToArray(), out var array);
            return array;
        }
        finally { foreach (var p in pidls) Marshal.FreeCoTaskMem(p); }
    }

    private static string? PickImage(string path)
    {
        if (File.Exists(path)) return path;
        if (!Directory.Exists(path)) return null;
        var files = Directory.EnumerateFiles(path).Where(IsImage).ToArray();
        return files.Length == 0 ? null : files[Random.Shared.Next(files.Length)];
    }

    private static DesktopWallpaperPosition ToPosition(FitMode fit) => fit switch
    {
        FitMode.Fill => DesktopWallpaperPosition.Fill,
        FitMode.Fit => DesktopWallpaperPosition.Fit,
        FitMode.Stretch => DesktopWallpaperPosition.Stretch,
        FitMode.Tile => DesktopWallpaperPosition.Tile,
        FitMode.Center => DesktopWallpaperPosition.Center,
        FitMode.Span => DesktopWallpaperPosition.Span,
        _ => DesktopWallpaperPosition.Fill,
    };
}
