using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace DeskFrames.UI;

/// <summary>
/// Programmsymbol, zur Laufzeit gezeichnet: ein dunkler Bilderrahmen mit Landschaft (Berg und Sonne).
/// So braucht es keine Bilddatei; das .ico für die EXE wird in S4 aus derselben Zeichnung erzeugt.
/// </summary>
internal static class AppIcon
{
    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    public static Bitmap Draw(int size)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);
        float s = size / 32f;

        // Rahmen
        using (var frame = new SolidBrush(Color.FromArgb(0x2B, 0x2B, 0x2E)))
            g.FillRectangle(frame, 2 * s, 4 * s, 28 * s, 24 * s);
        // Bildfläche: Himmel
        var inner = new RectangleF(5 * s, 7 * s, 22 * s, 18 * s);
        using (var sky = new LinearGradientBrush(inner, Color.FromArgb(0x7C, 0xC4, 0xF0), Color.FromArgb(0xD8, 0xEE, 0xFA), 90f))
            g.FillRectangle(sky, inner);
        // Sonne
        using (var sun = new SolidBrush(Color.FromArgb(0xF6, 0xC3, 0x45)))
            g.FillEllipse(sun, 19 * s, 9 * s, 5 * s, 5 * s);
        // Berge
        using (var hill = new SolidBrush(Color.FromArgb(0x3F, 0x8F, 0x5A)))
            g.FillPolygon(hill, new[] { new PointF(5 * s, 25 * s), new PointF(13 * s, 14 * s), new PointF(19 * s, 21 * s), new PointF(22 * s, 18 * s), new PointF(27 * s, 25 * s) });
        return bmp;
    }

    /// <summary>Symbol für Infobereich und Fenster (32 × 32, Windows verkleinert für den Infobereich).</summary>
    public static Icon Load()
    {
        using var bmp = Draw(32);
        IntPtr h = bmp.GetHicon();
        try { return (Icon)Icon.FromHandle(h).Clone(); }
        finally { DestroyIcon(h); }
    }

    /// <summary>Dasselbe Symbol als WPF-Bild für die Titelleiste.</summary>
    public static System.Windows.Media.ImageSource LoadWpf()
    {
        using var icon = Load();
        var src = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(icon.Handle, System.Windows.Int32Rect.Empty,
            System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
        src.Freeze();
        return src;
    }
}
