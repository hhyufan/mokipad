using System.Text.Json;

namespace MokiPad.Services;

public sealed class Preferences
{
    public bool Dark { get; set; }
    public bool Background { get; set; } = true;
    public string? CustomBackground { get; set; }
    public double BackgroundTransparency { get; init; } = 85;
    public string Language { get; set; } = "zh";
    public double FontSize { get; set; } = 16;

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private static string SettingsFile =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MokiPad",
            "preferences.json");

    public static Preferences Load()
    {
        try
        {
            return JsonSerializer.Deserialize<Preferences>(File.ReadAllText(SettingsFile)) ?? new Preferences();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new Preferences();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
        var temp = SettingsFile + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, SerializerOptions));
        File.Move(temp, SettingsFile, true);
    }
}