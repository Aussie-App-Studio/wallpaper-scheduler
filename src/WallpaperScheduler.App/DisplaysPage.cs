using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WallpaperScheduler.Core;

namespace WallpaperScheduler.App;

public sealed class DisplaysPage : Page
{
    private readonly StackPanel _stack;

    public DisplaysPage()
    {
        var (scroll, stack) = UiHelpers.PageShell("Displays");
        _stack = stack;
        Content = scroll;
        Loaded += (_, _) => Build();
    }

    private void Build()
    {
        while (_stack.Children.Count > 1) _stack.Children.RemoveAt(1);
        var cfg = AppState.Config;
        IReadOnlyList<MonitorInfo> monitors;
        try { monitors = AppState.Wallpaper.GetMonitors(); } catch { monitors = Array.Empty<MonitorInfo>(); }

        var same = new ToggleSwitch { IsOn = cfg.SameOnAllDisplays, OnContent = "On", OffContent = "Off" };
        same.Toggled += (_, _) => { cfg.SameOnAllDisplays = same.IsOn; AppState.Save(); Build(); };
        _stack.Children.Add(new SettingsCard
        {
            Header = "Use the same wallpaper on all displays",
            Description = monitors.Count > 1 ? $"{monitors.Count} displays detected" : "Only one display detected",
            HeaderIcon = new FontIcon { Glyph = "\uE7F4" },
            Content = same,
        });

        if (cfg.SameOnAllDisplays) return;

        _stack.Children.Add(UiHelpers.SectionHeader("Different picture per display"));
        _stack.Children.Add(new TextBlock
        {
            Text = "Choose a picture or folder for a display. Displays you leave empty use the slot's main picture.",
            Opacity = 0.7, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(1, 0, 0, 8),
        });

        foreach (var slot in cfg.Slots)
        {
            var exp = new SettingsExpander { Header = slot.Name, Description = "Overrides for this slot", HeaderIcon = new FontIcon { Glyph = "\uE823" }, IsExpanded = true };
            int n = 1;
            foreach (var m in monitors)
            {
                slot.MonitorPaths.TryGetValue(m.Id, out var current);
                var label = new TextBlock { Text = string.IsNullOrEmpty(current) ? "Same as slot" : System.IO.Path.GetFileName(current.TrimEnd('\\')), Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center };
                var pic = new Button { Content = "Picture" };
                var fol = new Button { Content = "Folder" };
                var clr = new Button { Content = "Clear" };
                var id = m.Id;
                pic.Click += async (_, _) => { var p = await UiHelpers.PickImageAsync(); if (p != null) { slot.MonitorPaths[id] = p; AppState.Save(); Build(); } };
                fol.Click += async (_, _) => { var p = await UiHelpers.PickFolderAsync(); if (p != null) { slot.MonitorPaths[id] = p; AppState.Save(); Build(); } };
                clr.Click += (_, _) => { slot.MonitorPaths.Remove(id); AppState.Save(); Build(); };
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                row.Children.Add(label); row.Children.Add(pic); row.Children.Add(fol); row.Children.Add(clr);
                exp.Items.Add(new SettingsCard { Header = $"Display {n++}", Description = $"{m.Width} × {m.Height}", Content = row });
            }
            _stack.Children.Add(exp);
        }
    }
}
