namespace DeskFrames.Model;

/// <summary>Rechteck in physischen Pixeln (virtueller Bildschirm, kann negativ sein).</summary>
public readonly record struct PixelRect(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;
    public int Bottom => Top + Height;
    public bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;
}

/// <summary>Ein Monitor mit Gerätename, Fläche in physischen Pixeln und Skalierung (1.0 = 100 %).</summary>
public sealed record MonitorInfo(string DeviceName, PixelRect Bounds, bool IsPrimary, double Scale = 1.0);

/// <summary>
/// Reine Rechnung zwischen gespeicherter Lage (Monitor + Abstand zur Monitorecke) und absoluter Lage auf dem
/// virtuellen Bildschirm. Ohne Fensterzugriffe, damit testbar.
/// </summary>
public static class Placement
{
    /// <summary>
    /// Monitor des Rahmens; fehlt er (abgesteckt), der Hauptmonitor. Der Rahmen bleibt dabei unverändert gespeichert –
    /// steckt der Monitor beim nächsten Start wieder, kommt er dorthin zurück.
    /// </summary>
    public static MonitorInfo ResolveMonitor(string? deviceName, IReadOnlyList<MonitorInfo> monitors)
    {
        if (monitors.Count == 0) throw new ArgumentException("At least one monitor required.", nameof(monitors));
        return monitors.FirstOrDefault(m => string.Equals(m.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
               ?? monitors.FirstOrDefault(m => m.IsPrimary)
               ?? monitors[0];
    }

    /// <summary>
    /// Absolute Lage eines Rahmens der Größe <paramref name="widthPx"/> × <paramref name="heightPx"/> (physische Pixel).
    /// Der Rahmen liegt immer vollständig auf seinem Monitor; ist er größer, schließt er links/oben bündig ab.
    /// </summary>
    public static (MonitorInfo Monitor, int X, int Y) Resolve(FrameConfig frame, int widthPx, int heightPx, IReadOnlyList<MonitorInfo> monitors)
    {
        var m = ResolveMonitor(frame.Monitor, monitors);
        var b = m.Bounds;
        int x = Math.Clamp(b.Left + frame.X, b.Left, Math.Max(b.Left, b.Right - widthPx));
        int y = Math.Clamp(b.Top + frame.Y, b.Top, Math.Max(b.Top, b.Bottom - heightPx));
        return (m, x, y);
    }

    /// <summary>
    /// Umkehrung: Zu welchem Monitor gehört ein Rahmen an absoluter Position (entscheidend ist sein Mittelpunkt),
    /// und welcher Abstand zur Monitorecke wird gespeichert. Liegt der Mittelpunkt außerhalb aller Monitore,
    /// zählt der nächstgelegene.
    /// </summary>
    public static (string DeviceName, int X, int Y) ToStored(int x, int y, int widthPx, int heightPx, IReadOnlyList<MonitorInfo> monitors)
    {
        if (monitors.Count == 0) throw new ArgumentException("At least one monitor required.", nameof(monitors));
        int cx = x + widthPx / 2, cy = y + heightPx / 2;
        var m = monitors.FirstOrDefault(mon => mon.Bounds.Contains(cx, cy))
                ?? monitors.MinBy(mon => DistanceSquared(mon.Bounds, cx, cy))!;
        return (m.DeviceName, x - m.Bounds.Left, y - m.Bounds.Top);
    }

    private static long DistanceSquared(PixelRect r, int x, int y)
    {
        long dx = x < r.Left ? r.Left - x : x >= r.Right ? x - (r.Right - 1) : 0;
        long dy = y < r.Top ? r.Top - y : y >= r.Bottom ? y - (r.Bottom - 1) : 0;
        return dx * dx + dy * dy;
    }

    /// <summary>Neuer Rahmen: mittig auf dem Monitor.</summary>
    public static (int X, int Y) Centered(MonitorInfo monitor, int widthPx, int heightPx) =>
        (Math.Max(0, (monitor.Bounds.Width - widthPx) / 2), Math.Max(0, (monitor.Bounds.Height - heightPx) / 2));
}
