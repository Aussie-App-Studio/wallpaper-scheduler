using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Win32;
using WallpaperScheduler.Core;
using Windows.ApplicationModel;

namespace WallpaperScheduler.App;

public sealed class SettingsPage : Page
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "WallpaperSchedulerAgent";
    private const string StartupTaskId = "WallpaperSchedulerStartup";

    public SettingsPage()
    {
        var (scroll, stack) = UiHelpers.PageShell("Settings");
        Content = scroll;
        var cfg = AppState.Config;

        // Pause
        var pause = new ToggleSwitch { IsOn = cfg.Paused, OnContent = "Paused", OffContent = "Running" }.WithA11y("Pause schedule");
        pause.Toggled += (_, _) =>
        {
            cfg.Paused = pause.IsOn;
            if (!cfg.Paused) cfg.PausedUntilUtc = null;
            AppState.Save();
        };
        stack.Children.Add(new SettingsCard
        {
            Header = "Pause Wallpaper Scheduler",
            Description = "Temporarily disable wallpaper and theme switching while keeping your schedule saved.",
            HeaderIcon = new FontIcon { Glyph = "\uE769" },
            Content = pause,
        });

        // Startup (StartupTask when MSIX-packaged, Registry fallback when running unpackaged)
        var startup = new ToggleSwitch { OnContent = "On", OffContent = "Off" }.WithA11y("Start with Windows");
        var startupCard = new SettingsCard
        {
            Header = "Start with Windows",
            Description = "On by default so your schedule keeps working after a restart. Use Pause above to disable the app temporarily.",
            HeaderIcon = new FontIcon { Glyph = "\uE7E8" },
            Content = startup,
        };
        stack.Children.Add(startupCard);
        InitStartupToggle(startup, startupCard);

        // App theme (only affects this app window)
        var appTheme = new ComboBox
        {
            ItemsSource = new[] { "Use system setting", "Light", "Dark" },
            SelectedIndex = (int)cfg.AppTheme,
            MinWidth = 180,
        }.WithA11y("App theme");
        appTheme.SelectionChanged += (_, _) =>
        {
            if (appTheme.SelectedIndex < 0) return;
            cfg.AppTheme = (AppThemeMode)appTheme.SelectedIndex;
            AppState.Save();
            App.MainWindow?.ApplyAppTheme(cfg.AppTheme);
        };
        stack.Children.Add(new SettingsCard
        {
            Header = "App theme",
            Description = "Choose Light or Dark for this app only (does not change Windows).",
            HeaderIcon = new FontIcon { Glyph = "\uE790" },
            Content = appTheme,
        });

        // Import / Export
        stack.Children.Add(UiHelpers.SectionHeader("Backup & transfer"));
        var backupStatus = new TextBlock { Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center };
        var exportBtn = new Button { Content = "Export settings" }.WithA11y("Export schedule settings to a file");
        var importBtn = new Button { Content = "Import settings" }.WithA11y("Import schedule settings from a file");
        exportBtn.Click += async (_, _) =>
        {
            var path = await UiHelpers.PickJsonSaveAsync();
            if (path == null) return;
            ConfigStore.Save(AppState.Config, path);
            backupStatus.Text = "Exported";
        };
        importBtn.Click += async (_, _) =>
        {
            var path = await UiHelpers.PickJsonOpenAsync();
            if (path == null) return;
            var loaded = ConfigStore.Load(path);
            AppState.ReplaceConfig(loaded);
            backupStatus.Text = $"Imported {loaded.Slots.Count} slots";
        };
        var backupRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        backupRow.Children.Add(backupStatus);
        backupRow.Children.Add(exportBtn);
        backupRow.Children.Add(importBtn);
        stack.Children.Add(new SettingsCard
        {
            Header = "Import or export settings",
            Description = "Save your schedule to a JSON file to copy it to another PC.",
            HeaderIcon = new FontIcon { Glyph = "\uE8C8" },
            Content = backupRow,
        });

        // Shortcuts (Desktop + Windows Search)
        var shortcutStatus = new TextBlock { Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center };
        var desktopBtn = new Button { Content = "Add to Desktop" }.WithA11y("Create a desktop shortcut");
        desktopBtn.Click += (_, _) =>
        {
            CreateShortcut(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
            shortcutStatus.Text = "Added to Desktop";
        };
        var startBtn = new Button { Content = "Add to Start / Search" }.WithA11y("Add to Start menu and Windows Search");
        startBtn.Click += (_, _) =>
        {
            CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs)));
            shortcutStatus.Text = "Added to Start";
        };
        var shortcutRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        shortcutRow.Children.Add(shortcutStatus);
        shortcutRow.Children.Add(desktopBtn);
        if (!IsPackaged()) shortcutRow.Children.Add(startBtn);
        stack.Children.Add(new SettingsCard
        {
            Header = "Shortcuts",
            Description = IsPackaged()
                ? "Available in Windows Search and Start automatically. Optionally add a Desktop icon."
                : "Create an optional Desktop icon or add to Start / Windows Search.",
            HeaderIcon = new FontIcon { Glyph = "\uE8A9" },
            Content = shortcutRow,
        });

        // About & Privacy
        stack.Children.Add(UiHelpers.SectionHeader("About"));
        var about = new SettingsExpander
        {
            Header = "Wallpaper Scheduler: Time of Day",
            Description = "Version 1.0.0",
            HeaderIcon = new FontIcon { Glyph = "\uE946" },
            IsExpanded = true,
        };
        about.Items.Add(new SettingsCard
        {
            Header = "No ads. No accounts. No tracking.",
            Description = "Everything runs on this PC. The app has no internet capability and never collects, stores externally, or transmits any data.",
        });
        stack.Children.Add(about);

        // About the developer
        stack.Children.Add(UiHelpers.SectionHeader("About the developer"));
        var devNote = new StackPanel { Spacing = 10, HorizontalAlignment = HorizontalAlignment.Stretch };
        devNote.Children.Add(new TextBlock
        {
            Text = "Hey! My name is Jesse, and I built this because I wanted it for my own PC. I'm not monetizing it, so if you want to do me a favour, check out this video where a Bible Expert takes on hard questions from the guys on the Flagrant Podcast.",
            TextWrapping = TextWrapping.Wrap,
        });
        devNote.Children.Add(new HyperlinkButton
        {
            Content = "Watch on YouTube (youtube.com/watch?v=p58vknxGR4I)",
            NavigateUri = new Uri("https://www.youtube.com/watch?v=p58vknxGR4I"),
            Padding = new Thickness(0),
        }.WithA11y("Watch Bible Expert on Flagrant Podcast on YouTube"));
        devNote.Children.Add(new TextBlock
        {
            Text = "I don't get anything... but as a Christian, I want as many people as possible to hear how reasonable and compelling faith in Jesus is — so that as many people as possible will be in paradise after the judgment at the end of this age. No stress if you aren't keen, you are welcome to just use the app. :)",
            TextWrapping = TextWrapping.Wrap,
        });
        devNote.Children.Add(new TextBlock
        {
            Text = "If you are already a Christian, you will probably like this extension I built. It's called \"Bible Hover\" and makes it so that whenever you hover your mouse over a Bible reference on the web a tiny box appears with the relevant verse(s).\n\nI also have a ton of (totally free) Android apps, some of which are just generically useful, but which are especially designed to be useful to Christians. Search for \"Aussie App Studio\" or \"Church Book Library\" on the Android Play Store.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.85,
        });
        stack.Children.Add(new SettingsCard
        {
            ContentAlignment = ContentAlignment.Vertical,
            Content = devNote,
        });
    }

    public static async void EnableStartupByDefault()
    {
        if (IsPackaged())
        {
            try
            {
                var task = await StartupTask.GetAsync(StartupTaskId);
                if (task.State == StartupTaskState.Disabled) await task.RequestEnableAsync();
            }
            catch { }
        }
        else if (!IsRegistryStartupEnabled())
        {
            SetRegistryStartup(true);
        }
    }

    private static bool IsPackaged()
    {
        try { _ = Package.Current; return true; }
        catch { return false; }
    }

    private static async void InitStartupToggle(ToggleSwitch toggle, SettingsCard card)
    {
        bool suppress = true;
        if (IsPackaged())
        {
            try
            {
                var task = await StartupTask.GetAsync(StartupTaskId);
                toggle.IsOn = task.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
                if (task.State == StartupTaskState.DisabledByUser)
                    card.Description = "Disabled in Windows Settings → Apps → Startup. Re-enable it there.";
                toggle.IsEnabled = task.State is not (StartupTaskState.DisabledByPolicy or StartupTaskState.DisabledByUser or StartupTaskState.EnabledByPolicy);
            }
            catch { toggle.IsOn = false; }
        }
        else
        {
            toggle.IsOn = IsRegistryStartupEnabled();
        }
        suppress = false;

        toggle.Toggled += async (_, _) =>
        {
            if (suppress) return;
            if (IsPackaged())
            {
                try
                {
                    var task = await StartupTask.GetAsync(StartupTaskId);
                    if (toggle.IsOn)
                    {
                        var state = await task.RequestEnableAsync();
                        suppress = true;
                        toggle.IsOn = state is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
                        suppress = false;
                    }
                    else
                    {
                        task.Disable();
                    }
                }
                catch { }
            }
            else
            {
                SetRegistryStartup(toggle.IsOn);
            }
        };
    }

    private static bool IsRegistryStartupEnabled()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey);
        return k?.GetValue(RunValue) != null;
    }

    private static void SetRegistryStartup(bool on)
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (k == null) return;
        if (on)
        {
            var exe = Path.Combine(AppContext.BaseDirectory, "WallpaperSchedulerAgent.exe");
            if (File.Exists(exe)) k.SetValue(RunValue, $"\"{exe}\"");
        }
        else k.DeleteValue(RunValue, throwOnMissingValue: false);
    }

    private static void CreateShortcut(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            var lnkPath = Path.Combine(folder, "Wallpaper Scheduler.lnk");
            var target = IsPackaged()
                ? $@"shell:AppsFolder\{Package.Current.Id.FamilyName}!App"
                : Path.Combine(AppContext.BaseDirectory, "WallpaperScheduler.exe");
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");

            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return;
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic sc = shell.CreateShortcut(lnkPath);
            sc.TargetPath = target;
            if (!IsPackaged()) sc.WorkingDirectory = AppContext.BaseDirectory;
            if (File.Exists(iconPath)) sc.IconLocation = iconPath;
            sc.Save();
        }
        catch { }
    }
}
