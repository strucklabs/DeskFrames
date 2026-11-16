using System.Windows.Threading;
using DeskFrames.Desktop;
using DeskFrames.Imaging;
using DeskFrames.Model;
using DeskFrames.Native;

namespace DeskFrames.Frames;

/// <summary>
/// Verwaltet alle Rahmenfenster: anlegen, entfernen, Stil, Strg+Umschalt-Modus, Desktop-Ebene und Speichern.
/// Gespeichert wird kurz nach der letzten Änderung (Schieberegler!) und sicher beim Beenden.
/// </summary>
internal sealed class FrameManager : IDisposable
{
    private readonly AppConfig _config;
    private readonly string? _path;
    private readonly List<FrameWindow> _windows = new();
    private readonly DispatcherTimer _modifierTimer = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private DesktopLayer? _layer;
    private List<MonitorInfo> _monitors = Monitors.GetAll();

    /// <summary>Rahmen hinzugefügt/entfernt, Bild geladen, Stil geändert – Verwaltungsfenster aktualisieren.</summary>
    public event Action? FramesChanged;

    /// <summary>Rechtsklick auf einen Rahmen bei gedrückter Strg+Umschalt.</summary>
    public event Action<FrameWindow>? ContextMenuRequested;

    public FrameManager(AppConfig config, string? path = null)
    {
        _config = config;
        _path = path;
        _modifierTimer.Tick += (_, _) =>
        {
            bool on = Win32.IsKeyDown(Win32.VK_CONTROL) && Win32.IsKeyDown(Win32.VK_SHIFT);
            foreach (var w in _windows) w.SetDragMode(on);
        };
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); _config.Save(_path); };
    }

    public IReadOnlyList<FrameWindow> Windows => _windows;

    private IReadOnlyList<MonitorInfo> CurrentMonitors()
    {
        // Monitor-Liste ist billig; bei jedem Zugriff frisch, damit an-/abgesteckte Monitore sofort zählen
        var fresh = Monitors.GetAll();
        if (fresh.Count > 0) _monitors = fresh;
        return _monitors;
    }

    /// <summary>Alle gespeicherten Rahmen zeigen und die Desktop-Ebene starten.</summary>
    public void ShowAll()
    {
        foreach (var cfg in _config.Frames) CreateWindow(cfg);
        // Blinkende Rahmen liegen kurz oben – die Desktop-Ebene lässt sie dabei in Ruhe
        _layer = new DesktopLayer(
            () => _windows.Where(w => w.Hwnd != IntPtr.Zero && !w.IsBlinking).Select(w => w.Hwnd).ToList(),
            (hwnd, after) => _windows.FirstOrDefault(w => w.Hwnd == hwnd)?.PlaceAfter(after));
        _layer.Update();
        _modifierTimer.Start();
    }

    private FrameWindow CreateWindow(FrameConfig cfg)
    {
        var w = new FrameWindow(cfg, CurrentMonitors);
        w.Changed += _ => { SaveSoon(); FramesChanged?.Invoke(); };
        w.ImageStatusChanged += _ => FramesChanged?.Invoke();
        w.ContextMenuRequested += f => ContextMenuRequested?.Invoke(f);
        w.BlinkEnded += _ => _layer?.Update();
        _windows.Add(w);
        w.Show();
        _ = w.LoadImageAsync();
        return w;
    }

    /// <summary>
    /// Neuer Rahmen mit dem Bild <paramref name="path"/>: mittig auf dem Monitor unter dem Mauszeiger,
    /// 320 breit (Hochformat: 320 hoch). Gibt null zurück, wenn die Datei kein lesbares Bild ist.
    /// </summary>
    public async Task<FrameWindow?> AddFromFileAsync(string path)
    {
        var probe = await Task.Run(() => ImageLoader.Load(path, 64));
        if (probe.Status != ImageStatus.Ok) return null;

        double aspect = Math.Clamp(probe.AspectRatio, FrameConfig.MinAspect, FrameConfig.MaxAspect);
        var cfg = new FrameConfig
        {
            ImagePath = path,
            AspectRatio = aspect,
            Width = aspect >= 1 ? FrameConfig.DefaultWidth : FrameConfig.DefaultWidth * aspect,
        };
        cfg.Sanitize();

        var monitors = CurrentMonitors();
        Win32.GetCursorPos(out var cursor);
        var monitor = monitors.FirstOrDefault(m => m.Bounds.Contains(cursor.X, cursor.Y)) ?? Placement.ResolveMonitor(null, monitors);
        var (w, h) = FrameGeometry.OuterSize(cfg.Width * monitor.Scale, aspect, cfg.BorderWidth * monitor.Scale);
        (cfg.X, cfg.Y) = Placement.Centered(monitor, w, h);
        cfg.Monitor = monitor.DeviceName;

        _config.Frames.Add(cfg);
        SaveNow();
        var window = CreateWindow(cfg);
        _layer?.Update();
        FramesChanged?.Invoke();
        return window;
    }

    /// <summary>Anderes Bild für einen Rahmen. Gibt false zurück, wenn die Datei kein lesbares Bild ist.</summary>
    public async Task<bool> ChangeImageAsync(FrameWindow window, string path)
    {
        var probe = await Task.Run(() => ImageLoader.Load(path, 64));
        if (probe.Status != ImageStatus.Ok) return false;
        window.SetImage(path);   // passt das Seitenverhältnis beim Laden an (Fläche und Mitte bleiben)
        SaveSoon();
        FramesChanged?.Invoke();
        return true;
    }

    /// <summary>Rahmen entfernen. Die Bilddatei bleibt unberührt.</summary>
    public void Remove(FrameWindow window)
    {
        if (!_windows.Remove(window)) return;
        _config.Frames.Remove(window.Config);
        window.Close();
        SaveNow();
        FramesChanged?.Invoke();
    }

    public void SetBorder(FrameWindow window, string color, int width)
    {
        window.SetBorder(color, width);
        SaveSoon();
        FramesChanged?.Invoke();
    }

    /// <summary>Farbe und Breite für alle Rahmen übernehmen.</summary>
    public void ApplyBorderToAll(string color, int width)
    {
        foreach (var w in _windows) w.SetBorder(color, width);
        SaveSoon();
        FramesChanged?.Invoke();
    }

    public void Blink(FrameWindow window) => window.Blink();

    public void SaveSoon()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public void SaveNow()
    {
        _saveTimer.Stop();
        _config.Save(_path);
    }

    public void Dispose()
    {
        if (_saveTimer.IsEnabled) SaveNow();
        _modifierTimer.Stop();
        _layer?.Dispose();
        foreach (var w in _windows) w.Close();
        _windows.Clear();
    }
}
