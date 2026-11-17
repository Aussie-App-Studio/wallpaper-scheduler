using WallpaperScheduler.Core;
using Xunit;

namespace WallpaperScheduler.Core.Tests;

public class SolarTests
{
    [Fact]
    public void Sydney_Summer_Solstice_Times_Are_Plausible()
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById("AUS Eastern Standard Time");
        var rise = SolarCalculator.Sunrise(new DateOnly(2026, 12, 21), -33.8688, 151.2093, tz)!.Value;
        var set = SolarCalculator.Sunset(new DateOnly(2026, 12, 21), -33.8688, 151.2093, tz)!.Value;
        // Real values: ~05:41 and ~20:05 (AEDT).
        Assert.InRange(rise.TimeOfDay.TotalMinutes, 5 * 60 + 35, 5 * 60 + 47);
        Assert.InRange(set.TimeOfDay.TotalMinutes, 20 * 60, 20 * 60 + 10);
    }

    [Fact]
    public void London_Equinox_Is_About_Six_And_Six()
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time");
        var rise = SolarCalculator.Sunrise(new DateOnly(2026, 3, 20), 51.5074, -0.1278, tz)!.Value;
        Assert.InRange(rise.TimeOfDay.TotalMinutes, 5 * 60 + 55, 6 * 60 + 10);
    }

    [Fact]
    public void Polar_Night_Returns_Null()
    {
        var tz = TimeZoneInfo.Utc;
        Assert.Null(SolarCalculator.Sunrise(new DateOnly(2026, 12, 21), 78.2, 15.6, tz));
    }
}

public class EvaluatorTests
{
    private static AppConfig FourSlots() => new()
    {
        Slots =
        {
            new Slot { Name = "Morning", TimeOfDay = TimeSpan.FromHours(6) },
            new Slot { Name = "Afternoon", TimeOfDay = TimeSpan.FromHours(12) },
            new Slot { Name = "Evening", TimeOfDay = TimeSpan.FromHours(18) },
            new Slot { Name = "Night", TimeOfDay = TimeSpan.FromHours(21) },
        }
    };

    [Theory]
    [InlineData(7, "Morning")]
    [InlineData(12, "Afternoon")]
    [InlineData(20, "Evening")]
    [InlineData(23, "Night")]
    [InlineData(3, "Night")] // wraps past midnight
    public void Picks_Active_Slot(int hour, string expected)
    {
        var r = ScheduleEvaluator.Resolve(FourSlots(), new DateTime(2026, 6, 10, hour, 0, 0))!;
        Assert.Equal(expected, r.Active.Name);
    }

    [Fact]
    public void Next_Change_Wraps_To_Tomorrow()
    {
        var r = ScheduleEvaluator.Resolve(FourSlots(), new DateTime(2026, 6, 10, 22, 0, 0))!;
        Assert.Equal(new DateTime(2026, 6, 11, 6, 0, 0), r.NextChange);
    }

    [Fact]
    public void Single_Slot_Is_Always_Active()
    {
        var cfg = new AppConfig { Slots = { new Slot { Name = "Only", TimeOfDay = TimeSpan.FromHours(8) } } };
        var r = ScheduleEvaluator.Resolve(cfg, new DateTime(2026, 6, 10, 3, 0, 0))!;
        Assert.Equal("Only", r.Active.Name);
    }

    [Fact]
    public void Empty_Schedule_Returns_Null() =>
        Assert.Null(ScheduleEvaluator.Resolve(new AppConfig(), DateTime.Now));

    [Fact]
    public void Sun_Slots_Without_Location_Are_Skipped()
    {
        var cfg = new AppConfig { Slots = { new Slot { Name = "Day", Start = StartKind.Sunrise } } };
        Assert.Null(ScheduleEvaluator.Resolve(cfg, DateTime.Now));
    }

    [Fact]
    public void Day_Night_With_Sun_Resolves()
    {
        var cfg = new AppConfig
        {
            Location = new GeoLocation { Latitude = -33.8688, Longitude = 151.2093, TimeZoneId = "AUS Eastern Standard Time" },
            Slots =
            {
                new Slot { Name = "Day", Start = StartKind.Sunrise },
                new Slot { Name = "Night", Start = StartKind.Sunset },
            }
        };
        Assert.Equal("Day", ScheduleEvaluator.Resolve(cfg, new DateTime(2026, 12, 21, 12, 0, 0))!.Active.Name);
        Assert.Equal("Night", ScheduleEvaluator.Resolve(cfg, new DateTime(2026, 12, 21, 22, 0, 0))!.Active.Name);
        Assert.Equal("Night", ScheduleEvaluator.Resolve(cfg, new DateTime(2026, 12, 21, 3, 0, 0))!.Active.Name);
    }
}

public class ConfigTests
{
    [Fact]
    public void Round_Trips()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        var cfg = new AppConfig { Paused = true, Slots = { new Slot { Name = "A", Start = StartKind.Sunset, OffsetMinutes = -30 } } };
        ConfigStore.Save(cfg, path);
        var back = ConfigStore.Load(path);
        File.Delete(path);
        Assert.True(back.Paused);
        Assert.Equal(StartKind.Sunset, back.Slots[0].Start);
        Assert.Equal(-30, back.Slots[0].OffsetMinutes);
    }

    [Fact]
    public void Missing_File_Gives_Defaults() =>
        Assert.Empty(ConfigStore.Load(Path.Combine(Path.GetTempPath(), "nope-" + Guid.NewGuid() + ".json")).Slots);

    [Fact]
    public void Weekday_And_Weekend_Slots_Resolve_Across_Friday_To_Saturday()
    {
        var cfg = new AppConfig
        {
            Slots =
            {
                new Slot { Name = "Work", TimeOfDay = TimeSpan.FromHours(9), Days = DayFilter.Weekdays },
                new Slot { Name = "Weekend", TimeOfDay = TimeSpan.FromHours(10), Days = DayFilter.Weekends },
            }
        };
        // Saturday 08:00 -> still on Friday's Work slot until Weekend starts at 10:00
        var satMorning = ScheduleEvaluator.Resolve(cfg, new DateTime(2026, 11, 14, 8, 0, 0))!;
        Assert.Equal("Work", satMorning.Active.Name);
        Assert.Equal(new DateTime(2026, 11, 14, 10, 0, 0), satMorning.NextChange);

        // Saturday 11:00 -> Weekend slot until Monday 09:00
        var satNoon = ScheduleEvaluator.Resolve(cfg, new DateTime(2026, 11, 14, 11, 0, 0))!;
        Assert.Equal("Weekend", satNoon.Active.Name);
        Assert.Equal(new DateTime(2026, 11, 16, 9, 0, 0), satNoon.NextChange);
    }

    [Fact]
    public void Detects_Missing_Files_And_Time_Conflicts()
    {
        var cfg = new AppConfig
        {
            Slots =
            {
                new Slot { Name = "A", TimeOfDay = TimeSpan.FromHours(8), Path = "" },
                new Slot { Name = "B", TimeOfDay = TimeSpan.FromHours(8), Path = @"C:\nonexistent-wallpaper-xyz.jpg" },
            }
        };
        Assert.NotNull(ScheduleEvaluator.ValidateSlot(cfg.Slots[0]));
        Assert.NotNull(ScheduleEvaluator.ValidateSlot(cfg.Slots[1]));
        var conflicts = ScheduleEvaluator.FindTimeConflicts(cfg, new DateOnly(2026, 11, 17));
        Assert.Equal(2, conflicts.Count);
    }

    [Fact]
    public void PausedUntil_Expires_At_Target_Time()
    {
        var t0 = new DateTime(2026, 11, 17, 10, 0, 0, DateTimeKind.Utc);
        var cfg = new AppConfig { PausedUntilUtc = t0.AddHours(1) };
        Assert.True(cfg.IsPausedAt(t0.AddMinutes(30)));
        Assert.False(cfg.IsPausedAt(t0.AddHours(1)));
    }
}

public class WallpaperServiceSmokeTests
{
    [Fact]
    public void Lists_Monitors_Read_Only()
    {
        var monitors = new WallpaperService().GetMonitors();
        Assert.NotEmpty(monitors);
        foreach (var m in monitors) Console.WriteLine($"{m.Id} {m.Width}x{m.Height} {m.CurrentWallpaper}");
    }
}
