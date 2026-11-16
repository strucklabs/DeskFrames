using System.Windows.Threading;
using DeskFrames.Native;

namespace DeskFrames.Desktop;

/// <summary>
/// Hält die Rahmenfenster in der Desktop-Ebene: Sie liegen immer direkt über dem Fenster, das gerade die
/// Desktop-Symbole trägt (<c>SHELLDLL_DefView</c>) – normalerweise <c>Progman</c> ganz unten, bei „Desktop anzeigen“
/// (Win+D) ein <c>WorkerW</c> weit oben. So sind sie hinter allen Fenstern und trotzdem bei Win+D sichtbar.
/// Ermittelt in der Machbarkeitsprobe S0 (Variante C).
/// </summary>
internal sealed class DesktopLayer : IDisposable
{
    private readonly Func<IReadOnlyList<IntPtr>> _frames;
    private readonly Action<IntPtr, IntPtr> _placeAfter;
    private readonly DispatcherTimer _guard = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _settle = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private readonly Win32.WinEventProc _foregroundProc;   // gegen den Garbage Collector sichern
    private readonly IntPtr _hook;

    /// <param name="frames">Aktuelle Rahmenfenster.</param>
    /// <param name="placeAfter">Ordnet ein Rahmenfenster direkt unter dem angegebenen Fenster ein (darf die Z-Reihenfolge ändern).</param>
    public DesktopLayer(Func<IReadOnlyList<IntPtr>> frames, Action<IntPtr, IntPtr> placeAfter)
    {
        _frames = frames;
        _placeAfter = placeAfter;

        _guard.Tick += (_, _) => Update();
        _guard.Start();
        // Explorer räumt nach dem Vordergrund-Wechsel noch um – kurz danach erneut prüfen
        _settle.Tick += (_, _) => { _settle.Stop(); Update(); };

        _foregroundProc = (_, _, hwnd, _, _, _, _) =>
        {
            if (_frames().Contains(hwnd)) return;
            Update();
            _settle.Stop();
            _settle.Start();
        };
        _hook = Win32.SetWinEventHook(Win32.EVENT_SYSTEM_FOREGROUND, Win32.EVENT_SYSTEM_FOREGROUND, IntPtr.Zero,
            _foregroundProc, 0, 0, Win32.WINEVENT_OUTOFCONTEXT);
    }

    /// <summary>Fenster mit den Desktop-Symbolen: WorkerW/Progman, das <c>SHELLDLL_DefView</c> enthält.</summary>
    public static IntPtr DesktopHost()
    {
        IntPtr host = IntPtr.Zero;
        Win32.EnumWindows((h, _) =>
        {
            if (Win32.FindWindowEx(h, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero) { host = h; return false; }
            return true;
        }, IntPtr.Zero);
        return host != IntPtr.Zero ? host : Win32.FindWindow("Progman", null);
    }

    /// <summary>Rahmen direkt über das Desktop-Fenster legen – nur wenn sie dort noch nicht liegen.</summary>
    public void Update()
    {
        var frames = _frames();
        if (frames.Count == 0) return;
        IntPtr host = DesktopHost();
        if (host == IntPtr.Zero) return;
        if (IsPlaced(host, frames)) return;

        IntPtr above = Win32.GetWindow(host, Win32.GW_HWNDPREV);
        while (above != IntPtr.Zero && frames.Contains(above)) above = Win32.GetWindow(above, Win32.GW_HWNDPREV);
        foreach (var f in frames) _placeAfter(f, above == IntPtr.Zero ? Win32.HWND_TOP : above);
    }

    /// <summary>Liegen direkt über dem Desktop-Fenster genau die Rahmen? Unsichtbare Fenster zählen nicht.</summary>
    private static bool IsPlaced(IntPtr host, IReadOnlyList<IntPtr> frames)
    {
        IntPtr h = host;
        for (int i = 0; i < frames.Count; i++)
        {
            do h = Win32.GetWindow(h, Win32.GW_HWNDPREV);
            while (h != IntPtr.Zero && !Win32.IsWindowVisible(h));
            if (!frames.Contains(h)) return false;
        }
        return true;
    }

    public void Dispose()
    {
        _guard.Stop();
        _settle.Stop();
        if (_hook != IntPtr.Zero) Win32.UnhookWinEvent(_hook);
    }
}
