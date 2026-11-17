using System.Text.Json;
using System.Text.Json.Serialization;

namespace WallpaperScheduler.Core;

public static class ConfigStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string DefaultPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WallpaperScheduler", "config.json");

    public static AppConfig Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path), Options) ?? new AppConfig();
        }
        catch (Exception ex) when (ex is JsonException or IOException) { }
        return new AppConfig();
    }

    public static void Save(AppConfig config, string? path = null)
    {
        path ??= DefaultPath;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(config, Options));
        File.Move(tmp, path, overwrite: true); // atomic-ish replace so the agent never reads a partial file
    }
}
