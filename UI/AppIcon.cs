using System.Drawing;
using System.IO;

namespace DeskFrames.UI;

/// <summary>
/// Programmsymbol (dunkler Bilderrahmen mit Landschaft) aus der eingebetteten Assets\DeskFrames.ico –
/// dieselbe Datei, die auch die EXE trägt. Erzeugt mit Assets\make-icon.ps1.
/// </summary>
internal static class AppIcon
{
    private static Stream Open() =>
        typeof(AppIcon).Assembly.GetManifestResourceStream("DeskFrames.ico")
        ?? throw new InvalidOperationException("Embedded icon missing.");

    /// <summary>Symbol für den Infobereich in der passenden Größe (16 px bei 100 %, größer bei höherer Skalierung).</summary>
    public static Icon Load()
    {
        using var stream = Open();
        var size = System.Windows.Forms.SystemInformation.SmallIconSize;
        return new Icon(stream, size);
    }

    /// <summary>Dasselbe Symbol als WPF-Bild für die Titelleiste (alle Größen, Windows wählt passend).</summary>
    public static System.Windows.Media.ImageSource LoadWpf()
    {
        using var stream = Open();
        var decoder = new System.Windows.Media.Imaging.IconBitmapDecoder(stream,
            System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
        var frame = decoder.Frames.OrderBy(f => Math.Abs(f.PixelWidth - 32)).First();
        frame.Freeze();
        return frame;
    }
}
