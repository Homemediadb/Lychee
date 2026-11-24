using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.UI.Xaml;

namespace Lychee.Gui;

public enum Backdrop { Transparent, Checkerboard, Black, White, Red, Yellow, Green, Cyan, Blue, Magenta, Plasma }

public sealed class Settings
{
    public ElementTheme Theme { get; set; } = ElementTheme.Default;
    public Backdrop Backdrop { get; set; } = Backdrop.Transparent;
    public ElementTheme BackdropTheme { get; set; } = ElementTheme.Dark;
    public bool HideScanTips { get; set; }
    public string SaveSuffix { get; set; } = "_nobg";
    public string BatchFolder { get; set; } = "";
    public bool BatchTwoPass { get; set; }

    public static Settings Current { get; } = Load();

    public void FitBackdropTo(ElementTheme theme)
    {
        if (theme == BackdropTheme) return;
        BackdropTheme = theme;
        Backdrop = (theme, Backdrop) switch
        {
            (ElementTheme.Light, Backdrop.Transparent) => Backdrop.Checkerboard,
            (ElementTheme.Dark, Backdrop.Checkerboard) => Backdrop.Transparent,
            (ElementTheme.Light, Backdrop.White) => Backdrop.Black,
            (ElementTheme.Dark, Backdrop.Black) => Backdrop.White,
            var (_, kept) => kept,
        };
        Save();
    }

    public static event Action? Changed;

    static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lychee", "settings.json");

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
