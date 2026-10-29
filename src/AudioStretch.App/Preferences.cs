using Jupiter.UI;
using System.Text.Json;

namespace AudioStretch;
public sealed class Preferences : IThemePreferences, IAccentPreferences
{
    public static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Jupiter", "AudioStretch");
    private static string FilePath => Path.Combine(DirectoryPath, "settings.json");
    public string? Theme { get; set; }
    public bool UseCustomAccent { get; set; }
    public string? CustomAccent { get; set; }
    public double TempoChange { get; set; } = 5;
    public string? LastTransformJson { get; set; }
    public string Format { get; set; } = "wav";
    public string Encoding { get; set; } = "float";
    public bool CentreFocus { get; set; }
    public bool CheckUpdatesAutomatically { get; set; }
    public DateTimeOffset? LastUpdateCheck { get; set; }
    public string? LastFolder { get; set; }
    public int X { get; set; } = int.MinValue;
    public int Y { get; set; } = int.MinValue;
    public int Width { get; set; } = 800;
    public int Height { get; set; } = 600;
    public bool Maximized { get; set; }
    private bool _unreadable;
    public static Preferences Load()
    {
        try { return File.Exists(FilePath) ? JsonSerializer.Deserialize<Preferences>(File.ReadAllText(FilePath)) ?? new() : new(); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { App.LogError(e); return new() { _unreadable = true }; }
    }
    public void Save()
    {
        if (_unreadable) throw new IOException("Existing settings could not be read and have been preserved. Close AudioStretch and check settings.json.");
        Directory.CreateDirectory(DirectoryPath);
        File.WriteAllText(FilePath + ".tmp", JsonSerializer.Serialize(this));
        File.Move(FilePath + ".tmp", FilePath, true);
    }
}
