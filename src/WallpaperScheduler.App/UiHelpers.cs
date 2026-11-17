using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace WallpaperScheduler.App;

internal static class UiHelpers
{
    public static (ScrollViewer Scroll, StackPanel Stack) PageShell(string title)
    {
        var container = new Grid { Padding = new Thickness(36, 8, 36, 36), HorizontalAlignment = HorizontalAlignment.Stretch };
        container.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = 1000 });
        var stack = new StackPanel { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Stretch };
        stack.Children.Add(new TextBlock
        {
            Text = title,
            Style = (Style)Application.Current.Resources["TitleTextBlockStyle"],
            Margin = new Thickness(0, 0, 0, 16),
        });
        container.Children.Add(stack);
        return (new ScrollViewer { Content = container, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, stack);
    }

    public static TextBlock SectionHeader(string text) => new()
    {
        Text = text,
        Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
        Margin = new Thickness(1, 20, 0, 6),
    };

    public static T WithA11y<T>(this T element, string name, string? help = null) where T : DependencyObject
    {
        AutomationProperties.SetName(element, name);
        if (!string.IsNullOrEmpty(help)) AutomationProperties.SetHelpText(element, help);
        return element;
    }

    public static async Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, App.MainWindow!.Hwnd);
        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

    public static async Task<string?> PickImageAsync()
    {
        var picker = new FileOpenPicker();
        foreach (var ext in new[] { ".jpg", ".jpeg", ".png", ".bmp", ".webp", ".heic", ".tif", ".tiff", ".gif" })
            picker.FileTypeFilter.Add(ext);
        InitializeWithWindow.Initialize(picker, App.MainWindow!.Hwnd);
        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    public static async Task<string?> PickJsonOpenAsync()
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".json");
        InitializeWithWindow.Initialize(picker, App.MainWindow!.Hwnd);
        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    public static async Task<string?> PickJsonSaveAsync()
    {
        var picker = new FileSavePicker { SuggestedFileName = "wallpaper-scheduler-settings" };
        picker.FileTypeChoices.Add("JSON settings", new[] { ".json" });
        InitializeWithWindow.Initialize(picker, App.MainWindow!.Hwnd);
        var file = await picker.PickSaveFileAsync();
        return file?.Path;
    }

    public static string FormatTime(DateTime t) => t.ToString("t");

    public static string Describe(Core.Slot s)
    {
        string when = s.Start switch
        {
            Core.StartKind.Time => DateTime.Today.Add(s.TimeOfDay).ToString("t"),
            Core.StartKind.Sunrise => "Sunrise" + Offset(s.OffsetMinutes),
            _ => "Sunset" + Offset(s.OffsetMinutes),
        };
        string what = string.IsNullOrEmpty(s.Path) ? "No picture chosen"
            : (s.IsFolder ? "Folder: " : "Picture: ") + System.IO.Path.GetFileName(s.Path.TrimEnd('\\', '/'));
        string days = s.Days switch
        {
            Core.DayFilter.Weekdays => " · Weekdays",
            Core.DayFilter.Weekends => " · Weekends",
            _ => "",
        };
        string theme = s.Theme switch
        {
            Core.WindowsThemeMode.Light => " · Light theme",
            Core.WindowsThemeMode.Dark => " · Dark theme",
            _ => "",
        };
        return $"{when}{days} · {what}{theme}";
    }

    private static string Offset(int m) => m == 0 ? "" : (m > 0 ? $" + {m} min" : $" − {-m} min");
}
