using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.UI.Xaml;

namespace DiscBg.Gui;

public enum Backdrop { Transparent, Checkerboard, Black, White, Red, Yellow, Green, Cyan, Blue, Magenta, Plasma }

public sealed class Settings
{
    public ElementTheme Theme { get; set; } = ElementTheme.Default;
    public Backdrop Backdrop { get; set; } = Backdrop.Transparent;
    public string SaveSuffix { get; set; } = "_nobg";
    public string BatchFolder { get; set; } = "";
    public bool BatchTwoPass { get; set; }

    public static Settings Current { get; } = Load();

    public static event Action? Changed;

    static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DiscBg", "settings.json");

    static Settings Load()
    {
        try { return JsonSerializer.Deserialize(File.ReadAllText(FilePath), SettingsJson.Default.Settings) ?? new(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, SettingsJson.Default.Settings));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        Changed?.Invoke();
    }
}

// Source-generated, so trimmed builds need no reflection
[JsonSerializable(typeof(Settings))]
[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
sealed partial class SettingsJson : JsonSerializerContext;
