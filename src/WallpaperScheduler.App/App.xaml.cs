using System.Diagnostics;
using Microsoft.UI.Xaml;
using WallpaperScheduler.Core;

namespace WallpaperScheduler.App;

public partial class App : Application
{
    public static MainWindow? MainWindow { get; private set; }

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        AppState.Load();
        AppState.EnsureAgentRunning();
        MainWindow = new MainWindow();
        MainWindow.Activate();
    }
}

/// <summary>Shared config + services for all pages. Saving makes the background agent re-evaluate instantly.</summary>
public static class AppState
{
    public static AppConfig Config { get; private set; } = new();
    public static WallpaperService Wallpaper { get; } = new();

    public static void Load()
    {
        bool firstRun = !File.Exists(ConfigStore.DefaultPath);
        Config = ConfigStore.Load();
        if (firstRun) SettingsPage.EnableStartupByDefault();
    }

    public static void Save() => ConfigStore.Save(Config);

    public static void ReplaceConfig(AppConfig config)
    {
        Config = config;
        Save();
    }

    /// <summary>The agent is single-instance, so launching it when it is already running is harmless.</summary>
    public static void EnsureAgentRunning()
    {
        try
        {
            var exe = Path.Combine(AppContext.BaseDirectory, "WallpaperSchedulerAgent.exe");
            if (File.Exists(exe))
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true });
        }
        catch { /* the UI still works without the agent */ }
    }
}
