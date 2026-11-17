using CommunityToolkit.WinUI.Controls;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using WallpaperScheduler.Core;

namespace WallpaperScheduler.App;

public sealed class SchedulePage : Page
{
    private readonly StackPanel _root;
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly StackPanel _welcomeHost = new();
    private readonly StackPanel _scheduleHost = new() { Spacing = 4 };
    private readonly StackPanel _nowHost = new();
    private readonly StackPanel _timelineHost = new();
    private readonly StackPanel _slotsHost = new() { Spacing = 4 };
    private readonly StackPanel _locationHost = new();
    private readonly HashSet<Slot> _open = new();
    private bool _building;

    private static readonly string[] Presets = { "Day & Night", "Four times of day", "Custom" };

    public SchedulePage()
    {
        var (scroll, stack) = UiHelpers.PageShell("Schedule");
        _root = stack;
        Content = scroll;

        _root.Children.Add(_welcomeHost);
        _root.Children.Add(_scheduleHost);

        _scheduleHost.Children.Add(_nowHost);
        _scheduleHost.Children.Add(_timelineHost);
        _scheduleHost.Children.Add(UiHelpers.SectionHeader("Wallpapers"));
        _scheduleHost.Children.Add(BuildPresetCard());
        _scheduleHost.Children.Add(_slotsHost);
        _scheduleHost.Children.Add(_locationHost);

        Loaded += (_, _) => { Rebuild(); _tick.Tick += (_, _) => RefreshNow(); _tick.Start(); };
        Unloaded += (_, _) => _tick.Stop();
    }

    // ---------- First-run welcome ----------
    private void BuildWelcome()
    {
        _welcomeHost.Children.Clear();
        _welcomeHost.Children.Add(new InfoBar
        {
            Title = "Welcome to Wallpaper Scheduler",
            Message = "Pick a starting layout below, then choose your pictures or folders. Everything runs locally on your PC — no ads, no accounts, no tracking.",
            Severity = InfoBarSeverity.Informational,
            IsOpen = true,
            IsClosable = false,
            Margin = new Thickness(0, 0, 0, 12),
        });

        SettingsCard Choice(string title, string desc, string glyph, string preset)
        {
            var btn = new Button { Content = "Get started", Style = (Style)Application.Current.Resources["AccentButtonStyle"] }
                .WithA11y($"Start with {title}");
            btn.Click += (_, _) => ApplyPreset(preset);
            return new SettingsCard
            {
                Header = title,
                Description = desc,
                HeaderIcon = new FontIcon { Glyph = glyph },
                Content = btn,
            }.WithA11y(title, desc);
        }

        _welcomeHost.Children.Add(Choice("Day & Night", "Two slots starting at sunrise and sunset. Great for light and dark folders.", "\uE706", "Day & Night"));
        _welcomeHost.Children.Add(Choice("Four times of day", "Morning (6 AM), Afternoon (12 PM), Evening (6 PM), and Night (9 PM).", "\uE823", "Four times of day"));
        _welcomeHost.Children.Add(Choice("Start from scratch", "Create your own custom time slots.", "\uE710", "Custom"));
    }

    // ---------- Presets ----------
    private ComboBox? _presetBox;

    private SettingsCard BuildPresetCard()
    {
        _presetBox = new ComboBox { MinWidth = 200, ItemsSource = Presets }.WithA11y("Schedule type");
        _presetBox.SelectionChanged += (_, _) =>
        {
            if (_building || _presetBox.SelectedItem is not string p) return;
            ApplyPreset(p);
        };
        return new SettingsCard
        {
            Header = "Schedule type",
            Description = "Start from a preset, then choose your pictures or folders.",
            HeaderIcon = new FontIcon { Glyph = "\uE81C" },
            Content = _presetBox,
        };
    }

    private static string DetectPreset(AppConfig c)
    {
        if (c.Slots.Count == 2 && c.Slots[0].Start == StartKind.Sunrise && c.Slots[1].Start == StartKind.Sunset) return Presets[0];
        if (c.Slots.Count == 4 && c.Slots.All(s => s.Start == StartKind.Time)
            && c.Slots.Select(s => s.TimeOfDay.TotalHours).SequenceEqual(new double[] { 6, 12, 18, 21 })) return Presets[1];
        return Presets[2];
    }

    private void ApplyPreset(string preset)
    {
        var old = AppState.Config.Slots.Select(s => s.Path).ToList();
        List<Slot> slots = preset switch
        {
            "Day & Night" => new()
            {
                new Slot { Name = "Day", Start = StartKind.Sunrise, Theme = WindowsThemeMode.Light },
                new Slot { Name = "Night", Start = StartKind.Sunset, Theme = WindowsThemeMode.Dark },
            },
            "Four times of day" => new()
            {
                new Slot { Name = "Morning", TimeOfDay = TimeSpan.FromHours(6) },
                new Slot { Name = "Afternoon", TimeOfDay = TimeSpan.FromHours(12) },
                new Slot { Name = "Evening", TimeOfDay = TimeSpan.FromHours(18) },
                new Slot { Name = "Night", TimeOfDay = TimeSpan.FromHours(21) },
            },
            _ => AppState.Config.Slots,
        };
        if (preset != "Custom")
        {
            for (int i = 0; i < slots.Count && i < old.Count; i++) slots[i].Path = old[i];
            AppState.Config.Slots = slots;
        }
        else if (AppState.Config.Slots.Count == 0)
        {
            var first = new Slot { Name = "Slot 1", TimeOfDay = TimeSpan.FromHours(8) };
            AppState.Config.Slots.Add(first);
            _open.Add(first);
        }
        AppState.Save();
        Rebuild();
    }

    // ---------- Build ----------
    private void Rebuild()
    {
        bool empty = AppState.Config.Slots.Count == 0;
        _welcomeHost.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        _scheduleHost.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        if (empty) { BuildWelcome(); return; }

        _building = true;
        _presetBox!.SelectedItem = DetectPreset(AppState.Config);
        _building = false;

        EnsureLocation();
        BuildSlots();
        BuildLocation();
        RefreshNow();
    }

    private void EnsureLocation()
    {
        var c = AppState.Config;
        if (c.UsesSun && c.Location == null)
        {
            var d = Cities.Default();
            c.Location = new GeoLocation { Name = d.Name, Latitude = d.Lat, Longitude = d.Lon, TimeZoneId = d.TimeZoneId };
            AppState.Save();
        }
    }

    private void BuildSlots()
    {
        _slotsHost.Children.Clear();
        var slots = AppState.Config.Slots;
        var tz = ScheduleEvaluator.ResolveZone(AppState.Config.Location);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz));
        var conflicts = ScheduleEvaluator.FindTimeConflicts(AppState.Config, today);

        for (int i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            var expander = BuildSlotExpander(slot, conflicts.Contains(slot));
            var accent = new Border
            {
                BorderThickness = new Thickness(3, 0, 0, 0),
                BorderBrush = new SolidColorBrush(SlotColor(i)),
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(0, 0, 0, 10),
                Child = expander,
            };
            _slotsHost.Children.Add(accent);
        }

        var add = new Button { Content = "Add a time slot", Margin = new Thickness(0, 8, 0, 0) }
            .WithA11y("Add a time slot");
        add.Click += (_, _) =>
        {
            var s = new Slot { Name = $"Slot {slots.Count + 1}", TimeOfDay = TimeSpan.FromHours(12) };
            slots.Add(s);
            _open.Add(s);
            AppState.Save();
            Rebuild();
        };
        _slotsHost.Children.Add(add);
    }

    private SettingsExpander BuildSlotExpander(Slot slot, bool hasTimeConflict)
    {
        string? issue = ScheduleEvaluator.ValidateSlot(slot);
        if (issue == null && hasTimeConflict)
            issue = "Another slot starts at the same time.";

        var expander = new SettingsExpander
        {
            Header = slot.Name,
            Description = issue != null ? $"Warning: {issue}" : UiHelpers.Describe(slot),
            HeaderIcon = new FontIcon
            {
                Glyph = issue != null ? "\uE7BA" : slot.Start switch { StartKind.Sunrise => "\uE706", StartKind.Sunset => "\uE708", _ => "\uE823" },
            },
        }.WithA11y($"Time slot {slot.Name}", UiHelpers.Describe(slot));

        void Changed()
        {
            AppState.Save();
            var newIssue = ScheduleEvaluator.ValidateSlot(slot);
            expander.Description = newIssue != null ? $"Warning: {newIssue}" : UiHelpers.Describe(slot);
            RefreshNow();
        }

        // Name + warning badge + remove
        var name = new TextBox { Text = slot.Name, MinWidth = 150 }.WithA11y("Slot name");
        name.TextChanged += (_, _) => { slot.Name = name.Text; expander.Header = name.Text; AppState.Save(); };
        var remove = new Button { Content = new FontIcon { Glyph = "\uE74D", FontSize = 14 } }.WithA11y($"Remove {slot.Name}");
        ToolTipService.SetToolTip(remove, "Remove this slot");
        remove.Click += (_, _) => { AppState.Config.Slots.Remove(slot); AppState.Save(); Rebuild(); };
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        if (issue != null)
        {
            var warn = new FontIcon { Glyph = "\uE7BA", Foreground = new SolidColorBrush(Colors.Orange), VerticalAlignment = VerticalAlignment.Center };
            ToolTipService.SetToolTip(warn, issue);
            head.Children.Add(warn);
        }
        head.Children.Add(BuildMiniPreview(slot));
        head.Children.Add(name);
        head.Children.Add(remove);
        expander.Content = head;

        expander.IsExpanded = _open.Contains(slot);
        expander.Expanded += (_, _) => _open.Add(slot);
        expander.Collapsed += (_, _) => _open.Remove(slot);

        if (issue != null)
        {
            expander.Items.Add(new SettingsCard
            {
                Header = issue,
                HeaderIcon = new FontIcon { Glyph = "\uE7BA", Foreground = new SolidColorBrush(Colors.Orange) },
                Description = hasTimeConflict ? "Change one of the start times so they do not overlap." : "Choose a valid picture or folder below.",
            });
        }

        // Starts at
        var kind = new ComboBox { ItemsSource = new[] { "Specific time", "Sunrise", "Sunset" }, MinWidth = 150, SelectedIndex = (int)slot.Start }.WithA11y("Start trigger");
        var time = new TimePicker { Time = slot.TimeOfDay, MinuteIncrement = 5, Visibility = slot.Start == StartKind.Time ? Visibility.Visible : Visibility.Collapsed }.WithA11y("Start time");
        var offset = new NumberBox
        {
            Value = slot.OffsetMinutes, Minimum = -240, Maximum = 240, SmallChange = 5, Width = 150,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
            PlaceholderText = "Offset (min)",
            Visibility = slot.Start == StartKind.Time ? Visibility.Collapsed : Visibility.Visible,
        }.WithA11y("Offset in minutes");
        ToolTipService.SetToolTip(offset, "Minutes after (+) or before (−) the sun event");
        kind.SelectionChanged += (_, _) =>
        {
            if (kind.SelectedIndex < 0) return;
            slot.Start = (StartKind)kind.SelectedIndex;
            AppState.Save();
            Rebuild();
        };
        time.TimeChanged += (_, e) => { slot.TimeOfDay = e.NewTime; AppState.Save(); Rebuild(); };
        offset.ValueChanged += (_, e) => { if (!double.IsNaN(e.NewValue)) { slot.OffsetMinutes = (int)e.NewValue; Changed(); } };
        var when = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        when.Children.Add(kind); when.Children.Add(time); when.Children.Add(offset);
        expander.Items.Add(new SettingsCard { Header = "Starts at", Content = when });

        // Days filter
        var days = new ComboBox { ItemsSource = new[] { "Every day", "Weekdays", "Weekends" }, SelectedIndex = (int)slot.Days, MinWidth = 150 }.WithA11y("Active days");
        days.SelectionChanged += (_, _) => { if (days.SelectedIndex >= 0) { slot.Days = (DayFilter)days.SelectedIndex; AppState.Save(); Rebuild(); } };
        expander.Items.Add(new SettingsCard { Header = "Days", Description = "Run on every day, weekdays only, or weekends only", Content = days });

        // Shows
        var pathText = new TextBlock { Text = string.IsNullOrEmpty(slot.Path) ? "Nothing chosen" : slot.Path, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 320, Opacity = 0.7 };
        var pic = new Button { Content = "Choose picture" }.WithA11y($"Choose picture for {slot.Name}");
        var fol = new Button { Content = "Choose folder" }.WithA11y($"Choose folder for {slot.Name}");
        pic.Click += async (_, _) => { var p = await UiHelpers.PickImageAsync(); if (p != null) { slot.Path = p; AppState.Save(); Rebuild(); } };
        fol.Click += async (_, _) => { var p = await UiHelpers.PickFolderAsync(); if (p != null) { slot.Path = p; AppState.Save(); Rebuild(); } };
        var showRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        showRow.Children.Add(pathText); showRow.Children.Add(pic); showRow.Children.Add(fol);
        expander.Items.Add(new SettingsCard { Header = "Shows", Description = "A single picture, or a folder to cycle through", Content = showRow });

        // Preview / playlist
        if (slot.IsFolder) expander.Items.Add(BuildPlaylist(slot));
        else if (File.Exists(slot.Path))
            expander.Items.Add(new SettingsCard { Header = "Preview", Content = Thumb(slot.Path, 192, 108).WithA11y($"Preview of {System.IO.Path.GetFileName(slot.Path)}") });

        // Slideshow options (folders only)
        if (slot.IsFolder)
        {
            var shuffle = new ToggleSwitch { IsOn = slot.Shuffle, OnContent = "Shuffle", OffContent = "In order" }.WithA11y("Shuffle folder pictures");
            shuffle.Toggled += (_, _) => { slot.Shuffle = shuffle.IsOn; AppState.Save(); Rebuild(); };
            var every = new ComboBox { ItemsSource = new[] { "1 minute", "10 minutes", "30 minutes", "1 hour", "6 hours", "1 day" } }.WithA11y("Slideshow interval");
            int[] mins = { 1, 10, 30, 60, 360, 1440 };
            int idx = Array.IndexOf(mins, slot.SlideshowIntervalMinutes);
            every.SelectedIndex = idx >= 0 ? idx : 2;
            every.SelectionChanged += (_, _) => { if (every.SelectedIndex >= 0) { slot.SlideshowIntervalMinutes = mins[every.SelectedIndex]; Changed(); } };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
            row.Children.Add(shuffle); row.Children.Add(every);
            expander.Items.Add(new SettingsCard { Header = "Slideshow", Description = "Change picture every", Content = row });
        }

        // Fit
        var fit = new ComboBox { ItemsSource = Enum.GetNames<FitMode>(), SelectedItem = slot.Fit.ToString(), MinWidth = 150 }.WithA11y("Picture fit");
        fit.SelectionChanged += (_, _) => { if (fit.SelectedItem is string s) { slot.Fit = Enum.Parse<FitMode>(s); Changed(); } };
        expander.Items.Add(new SettingsCard { Header = "Choose a fit for your picture", Content = fit });

        // Windows Light/Dark theme
        var theme = new ComboBox { ItemsSource = new[] { "Leave alone", "Light", "Dark" }, SelectedIndex = (int)slot.Theme, MinWidth = 150 }.WithA11y("Windows theme");
        theme.SelectionChanged += (_, _) => { if (theme.SelectedIndex >= 0) { slot.Theme = (WindowsThemeMode)theme.SelectedIndex; Changed(); } };
        expander.Items.Add(new SettingsCard { Header = "Windows theme", Description = "Optionally switch Windows Light or Dark mode when this slot starts", Content = theme });

        return expander;
    }

    // ---------- Location (city search + custom coordinates) ----------
    private bool _customCoords;

    private void BuildLocation()
    {
        _locationHost.Children.Clear();
        var c = AppState.Config;
        if (!c.UsesSun || c.Location == null) return;

        _locationHost.Children.Add(UiHelpers.SectionHeader("Location"));

        var search = new AutoSuggestBox
        {
            PlaceholderText = "Search a city...",
            Text = c.Location.Name,
            MinWidth = 260,
            QueryIcon = new SymbolIcon(Symbol.Find),
        }.WithA11y("Search city for sunrise and sunset");

        search.TextChanged += (_, args) =>
        {
            if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
            var q = search.Text.Trim();
            search.ItemsSource = string.IsNullOrEmpty(q)
                ? Cities.All.Select(x => x.Name).ToList()
                : Cities.All.Where(x => x.Name.Contains(q, StringComparison.OrdinalIgnoreCase)).Select(x => x.Name).ToList();
        };
        search.SuggestionChosen += (_, args) =>
        {
            var city = Cities.All.FirstOrDefault(x => x.Name == args.SelectedItem as string);
            if (city == null) return;
            c.Location = new GeoLocation { Name = city.Name, Latitude = city.Lat, Longitude = city.Lon, TimeZoneId = city.TimeZoneId };
            _customCoords = false;
            AppState.Save();
            Rebuild();
        };

        var exp = new SettingsExpander
        {
            Header = "Nearest city",
            Description = $"{c.Location.Name} ({c.Location.Latitude:F2}, {c.Location.Longitude:F2}) · Calculated offline.",
            HeaderIcon = new FontIcon { Glyph = "\uE707" },
            Content = search,
            IsExpanded = _customCoords,
        };
        exp.Expanded += (_, _) => _customCoords = true;
        exp.Collapsed += (_, _) => _customCoords = false;

        var latBox = new NumberBox { Header = "Latitude", Value = c.Location.Latitude, Minimum = -89.9, Maximum = 89.9, SmallChange = 0.1, Width = 140 }.WithA11y("Latitude");
        var lonBox = new NumberBox { Header = "Longitude", Value = c.Location.Longitude, Minimum = -180, Maximum = 180, SmallChange = 0.1, Width = 140 }.WithA11y("Longitude");
        var zones = TimeZoneInfo.GetSystemTimeZones().Select(z => z.Id).ToList();
        var tzBox = new ComboBox { Header = "Time zone", ItemsSource = zones, SelectedItem = c.Location.TimeZoneId ?? TimeZoneInfo.Local.Id, MaxWidth = 260 }.WithA11y("Time zone");
        var applyLoc = new Button { Content = "Use coordinates", VerticalAlignment = VerticalAlignment.Bottom }.WithA11y("Apply custom coordinates");
        applyLoc.Click += (_, _) =>
        {
            if (double.IsNaN(latBox.Value) || double.IsNaN(lonBox.Value)) return;
            c.Location = new GeoLocation
            {
                Name = $"Custom ({latBox.Value:F2}, {lonBox.Value:F2})",
                Latitude = latBox.Value,
                Longitude = lonBox.Value,
                TimeZoneId = tzBox.SelectedItem as string ?? TimeZoneInfo.Local.Id,
            };
            AppState.Save();
            Rebuild();
        };

        var coordRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        coordRow.Children.Add(latBox); coordRow.Children.Add(lonBox); coordRow.Children.Add(tzBox); coordRow.Children.Add(applyLoc);
        exp.Items.Add(new SettingsCard { Header = "Custom coordinates", Description = "Enter your exact latitude and longitude if your city is not listed", Content = coordRow });

        _locationHost.Children.Add(exp);
    }

    // ---------- Now showing + timeline ----------
    private void RefreshNow()
    {
        _nowHost.Children.Clear();
        _timelineHost.Children.Clear();
        var cfg = AppState.Config;
        var tz = ScheduleEvaluator.ResolveZone(cfg.Location);
        var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        var res = ScheduleEvaluator.Resolve(cfg, now);

        string header, desc;
        string? activeIssue = res != null ? ScheduleEvaluator.ValidateSlot(res.Active) : null;
        bool pausedUntilNext = !cfg.Paused && cfg.PausedUntilUtc is DateTime u && DateTime.UtcNow < u;

        if (res == null)
        {
            header = "Waiting for a valid slot";
            desc = "Add a slot, or choose a location for sunrise/sunset times.";
        }
        else if (activeIssue != null)
        {
            header = $"Now showing: {res.Active.Name} (skipped)";
            desc = $"{activeIssue} Next change at {UiHelpers.FormatTime(res.NextChange)}.";
        }
        else
        {
            header = $"Now showing: {res.Active.Name}";
            string pauseNote = cfg.Paused ? " (schedule paused)"
                : pausedUntilNext ? $" (paused until {UiHelpers.FormatTime(res.NextChange)})" : "";
            desc = $"Next change at {UiHelpers.FormatTime(res.NextChange)}{pauseNote}";
        }

        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        if (res != null && File.Exists(res.Active.Path))
        {
            try
            {
                right.Children.Add(new Image
                {
                    Width = 88, Height = 50, Stretch = Stretch.UniformToFill,
                    Source = new BitmapImage(new Uri(res.Active.Path)) { DecodePixelWidth = 176 },
                }.WithA11y($"Current wallpaper for {res.Active.Name}"));
            }
            catch { }
        }

        if (res != null && res.Active.IsFolder)
        {
            var skip = new Button { Content = "Next picture" }.WithA11y("Skip to next picture");
            skip.Click += (_, _) => { try { AppState.Wallpaper.AdvanceSlideshow(); } catch { } };
            right.Children.Add(skip);
        }

        if (res != null && !cfg.Paused)
        {
            var pauseBtn = new Button { Content = pausedUntilNext ? "Resume" : "Pause until next" }
                .WithA11y(pausedUntilNext ? "Resume schedule" : "Pause until next slot");
            pauseBtn.Click += (_, _) =>
            {
                cfg.PausedUntilUtc = pausedUntilNext ? null : TimeZoneInfo.ConvertTimeToUtc(res.NextChange, tz);
                AppState.Save();
                RefreshNow();
            };
            right.Children.Add(pauseBtn);
        }

        var switchNow = new Button { Content = "Apply now", IsEnabled = res != null && activeIssue == null }
            .WithA11y("Apply current slot wallpaper now");
        switchNow.Click += (_, _) =>
        {
            if (res != null && !AppState.Wallpaper.Apply(res.Active, cfg.SameOnAllDisplays))
                switchNow.Content = "Choose a picture first";
        };
        right.Children.Add(switchNow);

        _nowHost.Children.Add(new SettingsCard
        {
            Header = header,
            Description = desc,
            HeaderIcon = new FontIcon { Glyph = activeIssue != null ? "\uE7BA" : "\uE91B" },
            Content = right,
        });

        BuildTimeline(cfg, tz, now);
    }

    private void BuildTimeline(AppConfig cfg, TimeZoneInfo tz, DateTime now)
    {
        var day = DateOnly.FromDateTime(now);
        var starts = new List<(double Min, Slot Slot)>();
        foreach (var s in cfg.Slots)
        {
            if (!ScheduleEvaluator.MatchesDay(s.Days, day.DayOfWeek)) continue;
            var at = ScheduleEvaluator.StartOn(s, day, cfg.Location, tz);
            if (at != null) starts.Add((Math.Clamp((at.Value - day.ToDateTime(TimeOnly.MinValue)).TotalMinutes, 0, 1439), s));
        }
        if (starts.Count < 2) return;
        starts.Sort((a, b) => a.Min.CompareTo(b.Min));

        var segs = new List<(double Len, Slot Slot)> { (starts[0].Min, starts[^1].Slot) };
        for (int i = 0; i < starts.Count; i++)
            segs.Add(((i + 1 < starts.Count ? starts[i + 1].Min : 1440) - starts[i].Min, starts[i].Slot));

        var grid = new Grid { Height = 28, CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 10, 0, 0) }
            .WithA11y("24-hour schedule timeline");
        int col = 0;
        foreach (var (len, slot) in segs)
        {
            if (len <= 0) continue;
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(len, GridUnitType.Star) });
            int pi = cfg.Slots.IndexOf(slot);
            var b = new Border { Background = new SolidColorBrush(SlotColor(pi)), Opacity = 0.85 }
                .WithA11y(slot.Name);
            ToolTipService.SetToolTip(b, slot.Name);
            if (len > 90)
                b.Child = new TextBlock { Text = slot.Name, Foreground = new SolidColorBrush(Colors.White), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetColumn(b, col++);
            grid.Children.Add(b);
        }
        var marker = new Border { Width = 2, Background = new SolidColorBrush(Colors.White), HorizontalAlignment = HorizontalAlignment.Left };
        double frac = now.TimeOfDay.TotalMinutes / 1440.0;
        _timelineHost.SizeChanged += (_, e) => marker.Margin = new Thickness(e.NewSize.Width * frac, 0, 0, 0);

        var host = new Grid();
        host.Children.Add(grid);
        host.Children.Add(marker);
        _timelineHost.Children.Add(new TextBlock { Text = "Today", Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"], Opacity = 0.7, Margin = new Thickness(1, 14, 0, 0) });
        _timelineHost.Children.Add(host);
        var labels = new Grid { Margin = new Thickness(0, 2, 0, 0) };
        labels.ColumnDefinitions.Add(new ColumnDefinition()); labels.ColumnDefinitions.Add(new ColumnDefinition()); labels.ColumnDefinitions.Add(new ColumnDefinition());
        void Lbl(string t, int c, HorizontalAlignment a) { var tb = new TextBlock { Text = t, FontSize = 11, Opacity = 0.6, HorizontalAlignment = a }; Grid.SetColumn(tb, c); labels.Children.Add(tb); }
        Lbl("12 AM", 0, HorizontalAlignment.Left); Lbl("12 PM", 1, HorizontalAlignment.Center); Lbl("12 AM", 2, HorizontalAlignment.Right);
        _timelineHost.Children.Add(labels);
    }

    // ---------- Previews ----------
    private static Image Thumb(string path, int w, int h)
    {
        var img = new Image { Width = w, Height = h, Stretch = Stretch.UniformToFill };
        try { img.Source = new BitmapImage { DecodePixelWidth = w * 2, UriSource = new Uri(path) }; } catch { }
        return img;
    }

    private static string EveryText(int minutes) => minutes switch
    {
        1 => "minute", 60 => "hour", 1440 => "day",
        _ when minutes % 60 == 0 => $"{minutes / 60} hours",
        _ => $"{minutes} minutes",
    };

    private static UIElement BuildMiniPreview(Slot slot)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        void Add(string path) =>
            row.Children.Add(new Border { CornerRadius = new CornerRadius(4), Child = Thumb(path, 64, 36) });

        if (slot.IsFolder)
        {
            var files = SlotImages.GetOrdered(slot);
            foreach (var f in files.Take(3)) Add(f);
            if (files.Count > 3)
                row.Children.Add(new TextBlock { Text = $"+{files.Count - 3}", VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 });
            if (files.Count == 0)
                row.Children.Add(new TextBlock { Text = "No pictures", VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 });
        }
        else if (File.Exists(slot.Path)) Add(slot.Path);
        else row.Children.Add(new TextBlock { Text = "Nothing chosen", VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 });
        return row;
    }

    private SettingsCard BuildPlaylist(Slot slot)
    {
        var files = SlotImages.GetOrdered(slot);
        bool canReorder = !slot.Shuffle;

        var grid = new GridView
        {
            SelectionMode = ListViewSelectionMode.None,
            CanReorderItems = canReorder, CanDragItems = canReorder, AllowDrop = canReorder,
            MaxHeight = 360,
            IsItemClickEnabled = false,
        }.WithA11y($"Pictures in {slot.Name}", canReorder ? "Drag items to reorder" : "Shuffled order");
        ScrollViewer.SetVerticalScrollBarVisibility(grid, ScrollBarVisibility.Auto);

        int n = 1;
        foreach (var file in files)
        {
            var fileName = System.IO.Path.GetFileName(file);
            var cell = new Grid { Width = 144, Height = 81, Tag = file }.WithA11y($"Picture {n}: {fileName}");
            cell.Children.Add(new Border { CornerRadius = new CornerRadius(6), Child = Thumb(file, 144, 81) });
            ToolTipService.SetToolTip(cell, fileName);
            var badge = new Border
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(190, 0, 0, 0)),
                CornerRadius = new CornerRadius(10), Padding = new Thickness(7, 1, 7, 1),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(6),
                Child = new TextBlock { Text = (n++).ToString(), FontSize = 11, Foreground = new SolidColorBrush(Colors.White) },
            };
            cell.Children.Add(badge);
            grid.Items.Add(cell);
        }

        void Renumber()
        {
            int i = 1;
            foreach (var item in grid.Items.Cast<Grid>())
                ((TextBlock)((Border)item.Children[1]).Child).Text = (i++).ToString();
        }

        grid.DragItemsCompleted += (_, _) =>
        {
            slot.Order = grid.Items.Cast<Grid>().Select(g => System.IO.Path.GetFileName((string)g.Tag)).ToList();
            Renumber();
            AppState.Save();
        };

        string caption = files.Count == 0 ? "This folder has no pictures."
            : slot.Shuffle
                ? $"{files.Count} pictures · Windows shows them in random order, changing every {EveryText(slot.SlideshowIntervalMinutes)}. Turn off shuffle to choose the order."
                : $"{files.Count} pictures · shown in this order, changing every {EveryText(slot.SlideshowIntervalMinutes)}. Drag to reorder.";

        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock { Text = caption, TextWrapping = TextWrapping.Wrap, Opacity = 0.75 });
        panel.Children.Add(grid);
        if (canReorder && slot.Order.Count > 0)
        {
            var reset = new HyperlinkButton { Content = "Sort by name" }.WithA11y("Reset picture order to sort by name");
            reset.Click += (_, _) => { slot.Order.Clear(); AppState.Save(); Rebuild(); };
            panel.Children.Add(reset);
        }

        return new SettingsCard { Header = "Pictures", ContentAlignment = ContentAlignment.Vertical, Content = panel };
    }

    private static readonly string[] SlotPalette = { "#FFB900", "#0078D4", "#E3008C", "#5C2E91", "#107C10", "#CA5010" };

    private static Windows.UI.Color SlotColor(int index) =>
        ParseColor(SlotPalette[Math.Abs(index) % SlotPalette.Length]);

    private static Windows.UI.Color ParseColor(string hex) =>
        Windows.UI.Color.FromArgb(255, Convert.ToByte(hex.Substring(1, 2), 16), Convert.ToByte(hex.Substring(3, 2), 16), Convert.ToByte(hex.Substring(5, 2), 16));
}
