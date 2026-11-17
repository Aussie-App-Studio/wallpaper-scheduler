using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WallpaperScheduler.App;

public sealed class TipsPage : Page
{
    public TipsPage()
    {
        var (scroll, stack) = UiHelpers.PageShell("Tips & Tricks");
        Content = scroll;

        stack.Children.Add(new TextBlock
        {
            Text = "A quick guide to how Wallpaper Scheduler works and how to get the most out of it.",
            Opacity = 0.75,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(1, 0, 0, 8),
        });

        stack.Children.Add(UiHelpers.SectionHeader("How it works"));

        stack.Children.Add(TipCard(
            "Your day as a loop of slots",
            "Each slot has a start time (like 6:00 AM or Sunset) and stays active until the next slot begins — even past midnight.",
            "\uE823"));

        stack.Children.Add(TipCard(
            "Single picture or folder slideshow",
            "Choose one picture for a steady wallpaper, or choose a folder to cycle through multiple pictures automatically. Turn off Shuffle inside a folder slot to drag pictures into your exact play order.",
            "\uEB9F"));

        stack.Children.Add(TipCard(
            "Match Windows Light & Dark mode",
            "Inside any slot, set Windows theme to Light or Dark to switch your taskbar and apps along with your wallpaper.",
            "\uE793"));

        stack.Children.Add(TipCard(
            "Sunrise & sunset without internet",
            "When a slot uses Sunrise or Sunset, choose your nearest city (or enter your coordinates). Times are calculated offline on your PC — nothing is sent anywhere.",
            "\uE706"));

        stack.Children.Add(TipCard(
            "Runs silently with no tray icon",
            "Closing this window does not stop your schedule. A tiny background process wakes only when a wallpaper needs to change. Enable 'Start with Windows' in Settings so it runs after a reboot.",
            "\uE7E8"));

        stack.Children.Add(UiHelpers.SectionHeader("Good to know"));

        stack.Children.Add(TipCard(
            "Waking from sleep",
            "If your PC was asleep when a slot was supposed to start, the right wallpaper for the current time is applied as soon as you wake or unlock it.",
            "\uE708"));

        stack.Children.Add(TipCard(
            "Weekdays, weekends & multiple monitors",
            "Set any slot to run Every day, Weekdays only, or Weekends only. Use the Displays page if you want different wallpapers on each monitor.",
            "\uE7F4"));

        stack.Children.Add(TipCard(
            "Moving to a new PC",
            "Use Export in Settings to save your schedule to a JSON file, then Import it on your new PC to restore everything in seconds.",
            "\uE8C8"));

        stack.Children.Add(TipCard(
            "Spotting problems",
            "If a picture is moved, a drive is unplugged, or two slots start at the exact same time, you will see a warning icon on the slot card explaining what to fix.",
            "\uE7BA"));
    }

    private static SettingsCard TipCard(string title, string description, string glyph) => new()
    {
        Header = title,
        Description = description,
        HeaderIcon = new FontIcon { Glyph = glyph },
    };
}
