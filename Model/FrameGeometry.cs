namespace DeskFrames.Model;

/// <summary>Wo die Maus einen Rahmen greift.</summary>
public enum Grip { Move, Left, Right, Top, Bottom, TopLeft, TopRight, BottomLeft, BottomRight }

/// <summary>
/// Reine Rechnungen für Verschieben/Skalieren mit festem Seitenverhältnis, in physischen Pixeln.
/// Das Seitenverhältnis gilt für das Bild innerhalb des Rahmens; der Rahmen (<c>border</c>) liegt außen herum.
/// </summary>
public static class FrameGeometry
{
    /// <summary>Griffbreite an den Rändern (geräteunabhängige Einheiten).</summary>
    public const double GripSize = 10;

    /// <summary>
    /// Welcher Griff liegt unter dem Punkt (<paramref name="x"/>, <paramref name="y"/>) eines Rahmens der Größe
    /// <paramref name="width"/> × <paramref name="height"/>? Ecken sind doppelt so lang wie die Griffbreite,
    /// damit man sie leicht trifft; bei kleinen Rahmen schrumpfen die Griffe, die Mitte bleibt zum Verschieben frei.
    /// </summary>
    public static Grip HitTest(double x, double y, double width, double height, double grip = GripSize)
    {
        double g = Math.Min(grip, Math.Min(width, height) / 4);
        double corner = 2 * g;
        bool left = x < g, right = x >= width - g, top = y < g, bottom = y >= height - g;
        bool nearEdge = left || right || top || bottom;
        if (nearEdge)
        {
            bool cl = x < corner, cr = x >= width - corner, ct = y < corner, cb = y >= height - corner;
            if (ct && cl) return Grip.TopLeft;
            if (ct && cr) return Grip.TopRight;
            if (cb && cl) return Grip.BottomLeft;
            if (cb && cr) return Grip.BottomRight;
        }
        if (left) return Grip.Left;
        if (right) return Grip.Right;
        if (top) return Grip.Top;
        if (bottom) return Grip.Bottom;
        return Grip.Move;
    }

    /// <summary>
    /// Neue Außenmaße beim Ziehen an <paramref name="grip"/> um (<paramref name="dx"/>, <paramref name="dy"/>) seit
    /// Beginn des Ziehens. Gerechnet wird immer vom Startrechteck aus – so entsteht auch bei vielen kleinen
    /// Mausbewegungen keine Rundungsdrift.
    /// <list type="bullet">
    /// <item>Ränder links/rechts bestimmen die Breite, oben/unten die Höhe, an Ecken zählt die größere Änderung.</item>
    /// <item>Die gegenüberliegende Kante bzw. Ecke bleibt stehen; bei Rändern bleibt die Mitte der anderen Achse.</item>
    /// <item>Bildbreite wird auf [<paramref name="minInnerWidth"/>, <paramref name="maxInnerWidth"/>] begrenzt.</item>
    /// </list>
    /// </summary>
    public static PixelRect Resize(PixelRect start, Grip grip, double dx, double dy, double aspect, double border,
        double minInnerWidth, double maxInnerWidth)
    {
        if (grip == Grip.Move) return start with { Left = start.Left + (int)Math.Round(dx), Top = start.Top + (int)Math.Round(dy) };

        double w0 = start.Width - 2 * border;
        double h0 = start.Height - 2 * border;

        double fromX = grip is Grip.Left or Grip.TopLeft or Grip.BottomLeft ? w0 - dx : w0 + dx;
        double fromY = (grip is Grip.Top or Grip.TopLeft or Grip.TopRight ? h0 - dy : h0 + dy) * aspect;

        double w = grip switch
        {
            Grip.Left or Grip.Right => fromX,
            Grip.Top or Grip.Bottom => fromY,
            _ => Math.Abs(fromX - w0) >= Math.Abs(fromY - w0) ? fromX : fromY,
        };
        w = ClampWidth(w, minInnerWidth, maxInnerWidth);

        var (outerW, outerH) = OuterSize(w, aspect, border);

        // Feste Bezugspunkte in ganzen Pixeln, damit die feste Kante exakt stehen bleibt
        int left = start.Left, top = start.Top, right = start.Right, bottom = start.Bottom;
        double cx = start.Left + start.Width / 2.0, cy = start.Top + start.Height / 2.0;

        int x = grip switch
        {
            Grip.Left or Grip.TopLeft or Grip.BottomLeft => right - outerW,
            Grip.Top or Grip.Bottom => (int)Math.Round(cx - outerW / 2.0),
            _ => left,
        };
        int y = grip switch
        {
            Grip.Top or Grip.TopLeft or Grip.TopRight => bottom - outerH,
            Grip.Left or Grip.Right => (int)Math.Round(cy - outerH / 2.0),
            _ => top,
        };
        return new PixelRect(x, y, outerW, outerH);
    }

    /// <summary>
    /// Neues Bild mit anderem Seitenverhältnis: Bildfläche und Mittelpunkt bleiben (etwa) gleich.
    /// </summary>
    public static PixelRect ChangeAspect(PixelRect outer, double border, double newAspect, double minInnerWidth, double maxInnerWidth)
    {
        double w0 = Math.Max(1, outer.Width - 2 * border);
        double h0 = Math.Max(1, outer.Height - 2 * border);
        double w = ClampWidth(Math.Sqrt(w0 * h0 * newAspect), minInnerWidth, maxInnerWidth);
        var (outerW, outerH) = OuterSize(w, newAspect, border);
        double cx = outer.Left + outer.Width / 2.0, cy = outer.Top + outer.Height / 2.0;
        return new PixelRect((int)Math.Round(cx - outerW / 2.0), (int)Math.Round(cy - outerH / 2.0), outerW, outerH);
    }

    /// <summary>
    /// Andere Rahmenbreite: Das Bild bleibt gleich groß und am selben Ort, der Rahmen wächst nach außen
    /// (bzw. schrumpft nach innen). Die Außenmaße folgen aus <see cref="OuterSize"/>, damit sie zur Anzeige passen.
    /// </summary>
    public static PixelRect ChangeBorder(PixelRect outer, double oldBorder, double newBorder, double aspect)
    {
        double innerW = Math.Max(1, outer.Width - 2 * oldBorder);
        var (w, h) = OuterSize(innerW, aspect, newBorder);
        double cx = outer.Left + outer.Width / 2.0, cy = outer.Top + outer.Height / 2.0;
        return new PixelRect((int)Math.Round(cx - w / 2.0), (int)Math.Round(cy - h / 2.0), w, h);
    }

    /// <summary>Außenmaße in ganzen Pixeln für eine Bildbreite; die Höhe folgt aus dem Seitenverhältnis.</summary>
    public static (int Width, int Height) OuterSize(double innerWidth, double aspect, double border) =>
        ((int)Math.Round(innerWidth + 2 * border), (int)Math.Round(innerWidth / aspect + 2 * border));

    /// <summary>Größte Bildbreite, mit der der Rahmen samt Rand noch auf einen Monitor passt.</summary>
    public static double MaxInnerWidthOnMonitor(PixelRect monitor, double aspect, double border) =>
        Math.Max(1, Math.Min(monitor.Width - 2 * border, (monitor.Height - 2 * border) * aspect));

    private static double ClampWidth(double w, double min, double max) =>
        min >= max ? min : Math.Clamp(w, min, max);
}
