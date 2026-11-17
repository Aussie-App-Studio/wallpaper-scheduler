using System.Text.Json.Serialization;

namespace WallpaperScheduler.Core;

public enum StartKind { Time, Sunrise, Sunset }

public enum FitMode { Fill, Fit, Stretch, Tile, Center, Span }

public enum WindowsThemeMode { LeaveAlone, Light, Dark }

public enum AppThemeMode { System, Light, Dark }

public enum DayFilter { EveryDay, Weekdays, Weekends }

/// <summary>One entry of the schedule: when it starts and what it shows.</summary>
public sealed class Slot
{
    public string Name { get; set; } = "";
    public StartKind Start { get; set; } = StartKind.Time;

    /// <summary>Clock time (used when Start == Time).</summary>
    public TimeSpan TimeOfDay { get; set; } = TimeSpan.FromHours(8);

    /// <summary>Minutes added to sunrise/sunset (negative = before).</summary>
    public int OffsetMinutes { get; set; }

    /// <summary>Path to a picture file or a folder (slideshow).</summary>
    public string Path { get; set; } = "";

    public bool Shuffle { get; set; } = false;
    public int SlideshowIntervalMinutes { get; set; } = 30;
    public FitMode Fit { get; set; } = FitMode.Fill;
    public WindowsThemeMode Theme { get; set; } = WindowsThemeMode.LeaveAlone;
    public DayFilter Days { get; set; } = DayFilter.EveryDay;

    /// <summary>Optional per-monitor overrides, keyed by monitor device path.</summary>
    public Dictionary<string, string> MonitorPaths { get; set; } = new();

    /// <summary>Custom play order for a folder (file names). Files not listed follow in name order.</summary>
    public List<string> Order { get; set; } = new();

    [JsonIgnore]
    public bool IsFolder => !string.IsNullOrEmpty(Path) && Directory.Exists(Path);
}

public sealed class GeoLocation
{
    public string Name { get; set; } = "";
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    /// <summary>Null means the machine's local time zone.</summary>
    public string? TimeZoneId { get; set; }
}

public sealed class AppConfig
{
    public int Version { get; set; } = 1;
    public bool Paused { get; set; }
    public AppThemeMode AppTheme { get; set; } = AppThemeMode.System;
    /// <summary>If set, schedule stays paused until this UTC timestamp (e.g. next slot boundary).</summary>
    public DateTime? PausedUntilUtc { get; set; }
    public bool SameOnAllDisplays { get; set; } = true;
    public GeoLocation? Location { get; set; }
    public List<Slot> Slots { get; set; } = new();

    [JsonIgnore]
    public bool UsesSun => Slots.Any(s => s.Start != StartKind.Time);

    public bool IsPausedAt(DateTime utcNow) =>
        Paused || (PausedUntilUtc is DateTime until && utcNow < until);
}
