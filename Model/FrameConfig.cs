using System.Text.RegularExpressions;

namespace DeskFrames.Model;

/// <summary>Ein Rahmen, wie er in frames.json steht.</summary>
public sealed class FrameConfig
{
    public const string DefaultBorderColor = "#1C1C1C";
    public const int DefaultBorderWidth = 6;
    public const int MaxBorderWidth = 40;
    public const double DefaultWidth = 320;
    /// <summary>Kürzere Seite mindestens so groß (geräteunabhängige Einheiten).</summary>
    public const double MinShortSide = 60;
    /// <summary>Obergrenze gegen unsinnige Werte aus einer von Hand bearbeiteten Datei.</summary>
    public const double MaxSide = 20000;
    public const double MinAspect = 0.05, MaxAspect = 20;

    public string Id { get; set; } = NewId();

    /// <summary>Verknüpfte Bilddatei; leer = noch kein Bild (Platzhalter).</summary>
    public string ImagePath { get; set; } = "";

    /// <summary>Breite / Höhe des Bildes (nach EXIF-Drehung). Bleibt gespeichert, auch wenn das Bild fehlt.</summary>
    public double AspectRatio { get; set; } = 1.0;

    /// <summary>Gerätename des Monitors, z. B. "\\.\DISPLAY2"; null = Hauptmonitor.</summary>
    public string? Monitor { get; set; }

    /// <summary>Position in physischen Pixeln relativ zur linken oberen Ecke des Monitors.</summary>
    public int X { get; set; }
    public int Y { get; set; }

    /// <summary>
    /// Bildbreite ohne Rahmen in geräteunabhängigen Einheiten (1/96 Zoll). Höhe = Breite / Seitenverhältnis.
    /// Der Rahmen kommt außen dazu – so hat das Bild immer genau sein eigenes Seitenverhältnis.
    /// </summary>
    public double Width { get; set; } = DefaultWidth;

    /// <summary>Farbe als #RRGGBB.</summary>
    public string BorderColor { get; set; } = DefaultBorderColor;

    /// <summary>Rahmenbreite in geräteunabhängigen Einheiten, 0 = ohne Rahmen.</summary>
    public int BorderWidth { get; set; } = DefaultBorderWidth;

    [System.Text.Json.Serialization.JsonIgnore]
    public double Height => Width / AspectRatio;

    public static string NewId() => Guid.NewGuid().ToString("N");

    private static readonly Regex ColorPattern = new("^#[0-9A-Fa-f]{6}$", RegexOptions.CultureInvariant);

    public static bool IsValidColor(string? color) => color != null && ColorPattern.IsMatch(color);

    /// <summary>Kleinste Breite, bei der die kürzere Seite <see cref="MinShortSide"/> erreicht.</summary>
    public static double MinWidthFor(double aspect) => MinShortSide * Math.Max(1.0, aspect);

    /// <summary>Größte Breite, bei der die längere Seite <see cref="MaxSide"/> nicht überschreitet.</summary>
    public static double MaxWidthFor(double aspect) => MaxSide * Math.Min(1.0, aspect);

    /// <summary>Repariert unsinnige Werte (von Hand bearbeitete oder alte Datei), statt den Rahmen zu verwerfen.</summary>
    public void Sanitize()
    {
        if (string.IsNullOrWhiteSpace(Id)) Id = NewId();
        ImagePath ??= "";
        if (!double.IsFinite(AspectRatio) || AspectRatio <= 0) AspectRatio = 1.0;
        AspectRatio = Math.Clamp(AspectRatio, MinAspect, MaxAspect);
        if (!double.IsFinite(Width)) Width = DefaultWidth;
        Width = Math.Clamp(Width, MinWidthFor(AspectRatio), MaxWidthFor(AspectRatio));
        if (!IsValidColor(BorderColor)) BorderColor = DefaultBorderColor;
        BorderColor = BorderColor.ToUpperInvariant();
        BorderWidth = Math.Clamp(BorderWidth, 0, MaxBorderWidth);
        if (string.IsNullOrWhiteSpace(Monitor)) Monitor = null;
    }

    public FrameConfig Clone() => (FrameConfig)MemberwiseClone();
}
