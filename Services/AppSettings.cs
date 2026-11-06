using System.IO;
using System.Text.Json;

namespace BiliRemote.Services;

public sealed class AppSettings
{
    public int SeekSeconds { get; set; } = 10;
    public bool GlobalHotkeys { get; set; } = true;
    public bool AlwaysOnTop { get; set; }
    public bool ToolbarsHidden { get; set; }
    public HotkeyConfiguration Hotkeys { get; set; } = new();
    public static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BiliRemote");
    private static string SettingsPath => Path.Combine(DataDirectory, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            return FromJson(File.ReadAllText(SettingsPath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new();
        }
    }

    public static AppSettings FromJson(string json)
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new();
        if (settings.SeekSeconds is not (5 or 10 or 30)) settings.SeekSeconds = 10;
        if (settings.Hotkeys is null || settings.Hotkeys.Validate() is not null) settings.Hotkeys = new();
        return settings;
    }

    public void Save()
    {
        Directory.CreateDirectory(DataDirectory);
        var temporary = SettingsPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, SettingsPath, overwrite: true);
    }
}
