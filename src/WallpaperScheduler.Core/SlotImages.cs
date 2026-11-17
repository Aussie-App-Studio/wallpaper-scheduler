using System.Runtime.InteropServices;

namespace WallpaperScheduler.Core;

/// <summary>Resolves the pictures of a folder slot, in the order they will be shown.</summary>
public static class SlotImages
{
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int StrCmpLogicalW(string a, string b);

    public const int MaxImages = 500;

    /// <summary>Folder pictures: custom order first (those that still exist), then the rest in Explorer name order.</summary>
    public static List<string> GetOrdered(Slot slot)
    {
        if (!slot.IsFolder) return new();

        var files = Directory.EnumerateFiles(slot.Path).Where(WallpaperService.IsImage).ToList();
        files.Sort((a, b) => StrCmpLogicalW(System.IO.Path.GetFileName(a), System.IO.Path.GetFileName(b)));

        var byName = files.ToDictionary(f => System.IO.Path.GetFileName(f), StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var name in slot.Order)
            if (byName.Remove(name, out var path)) result.Add(path);
        result.AddRange(files.Where(f => byName.ContainsKey(System.IO.Path.GetFileName(f))));
        return result.Count > MaxImages ? result.GetRange(0, MaxImages) : result;
    }
}
