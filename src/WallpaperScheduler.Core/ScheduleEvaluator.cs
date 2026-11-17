namespace WallpaperScheduler.Core;

public sealed record Resolution(Slot Active, DateTime ActiveSince, DateTime NextChange);

public static class ScheduleEvaluator
{
    /// <summary>
    /// Finds the slot in effect at <paramref name="now"/> (local time of the schedule's time zone)
    /// and when the next slot begins. Works across midnight, day filters, and missed transitions.
    /// </summary>
    public static Resolution? Resolve(AppConfig config, DateTime now)
    {
        if (config.Slots.Count == 0) return null;

        var tz = ResolveZone(config.Location);
        var day = DateOnly.FromDateTime(now);
        var starts = new List<(DateTime At, Slot Slot)>();

        for (int offset = -7; offset <= 7; offset++)
        {
            var d = day.AddDays(offset);
            foreach (var slot in config.Slots)
            {
                if (!MatchesDay(slot.Days, d.DayOfWeek)) continue;
                var at = StartOn(slot, d, config.Location, tz);
                if (at != null) starts.Add((at.Value, slot));
            }
        }

        if (starts.Count == 0) return null;
        starts.Sort((a, b) => a.At.CompareTo(b.At));

        int idx = -1;
        for (int i = 0; i < starts.Count; i++)
        {
            if (starts[i].At <= now) idx = i; else break;
        }
        if (idx < 0 || idx + 1 >= starts.Count) return null;

        // Next change = first later start that switches to a different slot.
        var active = starts[idx];
        DateTime? next = null;
        for (int i = idx + 1; i < starts.Count; i++)
        {
            if (!ReferenceEquals(starts[i].Slot, active.Slot)) { next = starts[i].At; break; }
        }
        next ??= starts[idx + 1].At;
        return new Resolution(active.Slot, active.At, next.Value);
    }

    public static bool MatchesDay(DayFilter filter, DayOfWeek day) => filter switch
    {
        DayFilter.Weekdays => day is >= DayOfWeek.Monday and <= DayOfWeek.Friday,
        DayFilter.Weekends => day is DayOfWeek.Saturday or DayOfWeek.Sunday,
        _ => true,
    };

    public static DateTime? StartOn(Slot slot, DateOnly date, GeoLocation? loc, TimeZoneInfo tz)
    {
        switch (slot.Start)
        {
            case StartKind.Time:
                return date.ToDateTime(TimeOnly.MinValue).Add(slot.TimeOfDay);
            case StartKind.Sunrise:
            case StartKind.Sunset:
                if (loc == null) return null;
                var t = slot.Start == StartKind.Sunrise
                    ? SolarCalculator.Sunrise(date, loc.Latitude, loc.Longitude, tz)
                    : SolarCalculator.Sunset(date, loc.Latitude, loc.Longitude, tz);
                return t?.AddMinutes(slot.OffsetMinutes);
            default:
                return null;
        }
    }

    /// <summary>Returns a user-friendly warning if the slot cannot be applied, or null if valid.</summary>
    public static string? ValidateSlot(Slot slot)
    {
        if (string.IsNullOrWhiteSpace(slot.Path))
            return "No picture or folder chosen.";

        var root = System.IO.Path.GetPathRoot(slot.Path);
        if (!string.IsNullOrEmpty(root) && !Directory.Exists(root))
            return $"Drive {root.TrimEnd('\\')} is not connected.";

        if (Directory.Exists(slot.Path))
        {
            return Directory.EnumerateFiles(slot.Path).Any(WallpaperService.IsImage)
                ? null
                : "This folder contains no supported pictures.";
        }

        if (!File.Exists(slot.Path))
            return "Picture file could not be found.";

        return WallpaperService.IsImage(slot.Path)
            ? null
            : "This file is not a supported image format.";
    }

    /// <summary>Returns slots that start at the exact same minute on the same day as another slot.</summary>
    public static HashSet<Slot> FindTimeConflicts(AppConfig config, DateOnly date)
    {
        var tz = ResolveZone(config.Location);
        var conflicts = new HashSet<Slot>();
        var timed = new List<(int Minute, Slot Slot)>();

        foreach (var s in config.Slots)
        {
            var at = StartOn(s, date, config.Location, tz);
            if (at != null) timed.Add(((int)Math.Round((at.Value - date.ToDateTime(TimeOnly.MinValue)).TotalMinutes), s));
        }

        for (int i = 0; i < timed.Count; i++)
        {
            for (int j = i + 1; j < timed.Count; j++)
            {
                if (timed[i].Minute == timed[j].Minute && DayFiltersOverlap(timed[i].Slot.Days, timed[j].Slot.Days))
                {
                    conflicts.Add(timed[i].Slot);
                    conflicts.Add(timed[j].Slot);
                }
            }
        }
        return conflicts;
    }

    private static bool DayFiltersOverlap(DayFilter a, DayFilter b) =>
        a == DayFilter.EveryDay || b == DayFilter.EveryDay || a == b;

    public static TimeZoneInfo ResolveZone(GeoLocation? loc)
    {
        if (loc?.TimeZoneId is { Length: > 0 } id)
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch { }
        }
        return TimeZoneInfo.Local;
    }
}
