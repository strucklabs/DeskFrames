using System.IO;
using System.Text.Json;

namespace DeskFrames.Model;

/// <summary>
/// Inhalt von %APPDATA%\DeskFrames\frames.json. Der Autostart steht nicht hier, sondern nur in der Registry
/// (eine Quelle der Wahrheit). Fehlt die Datei oder ist sie beschädigt, startet das Programm leer – nie mit Fehler.
/// </summary>
public sealed class AppConfig
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public List<FrameConfig> Frames { get; set; } = new();

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeskFrames", "frames.json");

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        // Von Hand bearbeitete Dateien sollen nicht an Kleinigkeiten scheitern
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
    };

    public static AppConfig Load(string? path = null)
    {
        path ??= DefaultPath;
        string text;
        try
        {
            if (!File.Exists(path)) return new AppConfig();
            text = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorLog.Write($"frames.json not readable, starting empty: {ex.Message}");
            return new AppConfig();
        }

        if (string.IsNullOrWhiteSpace(text)) return new AppConfig();

        try
        {
            var config = JsonSerializer.Deserialize<AppConfig>(text, Json) ?? new AppConfig();
            config.Sanitize();
            return config;
        }
        catch (JsonException ex)
        {
            // Beschädigte Datei beiseitelegen, damit der nächste Speichervorgang sie nicht überschreibt
            ErrorLog.Write($"frames.json damaged, moved to frames.json.bad: {ex.Message}");
            try { File.Copy(path, path + ".bad", overwrite: true); }
            catch (Exception copyEx) when (copyEx is IOException or UnauthorizedAccessException) { }
            return new AppConfig();
        }
    }

    public void Sanitize()
    {
        Frames ??= new();
        Frames.RemoveAll(f => f == null);
        var seen = new HashSet<string>();
        foreach (var f in Frames)
        {
            f.Sanitize();
            if (!seen.Add(f.Id)) { f.Id = FrameConfig.NewId(); seen.Add(f.Id); }
        }
    }

    /// <summary>Schreibt über eine temporäre Datei, damit ein Absturz mittendrin keine halbe Datei hinterlässt.</summary>
    public bool Save(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this, Json));
            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorLog.Write($"frames.json not saved: {ex.Message}");
            return false;
        }
    }
}
