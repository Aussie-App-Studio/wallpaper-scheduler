using Microsoft.Win32;
using WallpaperScheduler.Core;

namespace WallpaperScheduler.Agent;

/// <summary>
/// Windowless background process. Applies the active schedule slot, then sleeps until the next
/// transition (or until the config changes / the PC wakes, unlocks or changes displays).
/// </summary>
internal static class Program
{
    private static readonly SemaphoreSlim Wake = new(0, 1);

    private static void Poke()
    {
        try { Wake.Release(); } catch (SemaphoreFullException) { }
    }

    [STAThread]
    private static int Main(string[] args)
    {
        bool once = args.Contains("--apply", StringComparer.OrdinalIgnoreCase);

        using var mutex = new Mutex(true, @"Local\WallpaperSchedulerAgent", out bool first);
        if (!first && !once) return 0;           // already running
        if (!first && once) { return 0; }         // agent is running and will pick up changes itself

        var service = new WallpaperService();
        string? lastKey = null;

        if (once)
        {
            RunOnce(service, ref lastKey, force: true);
            return 0;
        }

        // Re-evaluate on config edits, wake from sleep, unlock and display changes.
        var dir = Path.GetDirectoryName(ConfigStore.DefaultPath)!;
        Directory.CreateDirectory(dir);
        using var watcher = new FileSystemWatcher(dir, "config.json") { EnableRaisingEvents = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size };
        watcher.Changed += (_, _) => Poke();
        watcher.Created += (_, _) => Poke();
        watcher.Renamed += (_, _) => Poke();

        SystemEvents.PowerModeChanged += (_, e) => { if (e.Mode == PowerModes.Resume) Poke(); };
        SystemEvents.SessionSwitch += (_, e) =>
        {
            if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.SessionLogon
                or SessionSwitchReason.RemoteConnect or SessionSwitchReason.ConsoleConnect) Poke();
        };
        SystemEvents.DisplaySettingsChanged += (_, _) => Poke();
        SystemEvents.TimeChanged += (_, _) => Poke();

        while (true)
        {
            var wait = RunOnce(service, ref lastKey, force: false);
            // Cap the sleep so a missed wake event can never leave the wallpaper stale for long.
            var delay = wait > TimeSpan.FromMinutes(30) ? TimeSpan.FromMinutes(30) : wait;
            if (delay < TimeSpan.FromSeconds(5)) delay = TimeSpan.FromSeconds(5);
            Wake.Wait(delay);
            Thread.Sleep(500); // let a burst of file-change events settle
        }
    }

    /// <summary>Evaluates and applies if the active slot changed. Returns how long to sleep.</summary>
    private static TimeSpan RunOnce(WallpaperService service, ref string? lastKey, bool force)
    {
        var fallback = TimeSpan.FromMinutes(30);
        try
        {
            var config = ConfigStore.Load();
            var utcNow = DateTime.UtcNow;
            if (config.IsPausedAt(utcNow))
            {
                lastKey = null;
                if (!config.Paused && config.PausedUntilUtc is DateTime until)
                    return until - utcNow + TimeSpan.FromSeconds(1);
                return fallback;
            }

            var tz = ScheduleEvaluator.ResolveZone(config.Location);
            var now = TimeZoneInfo.ConvertTimeFromUtc(utcNow, tz);
            var res = ScheduleEvaluator.Resolve(config, now);
            if (res == null) return fallback;

            // Key includes slot content so editing the active slot re-applies it.
            var key = $"{res.ActiveSince:o}|{res.Active.Path}|{res.Active.Fit}|{res.Active.Theme}|{res.Active.Days}|{res.Active.Shuffle}|{string.Join(',', res.Active.Order)}|{res.Active.SlideshowIntervalMinutes}|{config.SameOnAllDisplays}|{string.Join(';', res.Active.MonitorPaths.Select(p => p.Key + "=" + p.Value))}";
            if (force || key != lastKey)
            {
                if (service.Apply(res.Active, config.SameOnAllDisplays)) lastKey = key;
            }
            return res.NextChange - now + TimeSpan.FromSeconds(1);
        }
        catch
        {
            return fallback; // never crash the background process
        }
    }
}
