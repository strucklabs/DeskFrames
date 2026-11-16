using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace DeskFramesProbe;

/// <summary>
/// Machbarkeitsprobe S0. Aufruf: DeskFramesProbe.exe [A|B|C] (Standard C)
///   C  = Rahmen liegen immer direkt über dem Fenster mit den Desktop-Symbolen (gewählt)
///   A  = Desktop (Progman) als Besitzer der Rahmen
///   B  = Rahmen bei „Desktop anzeigen“ nach oben heben (Vordergrund-Ereignis)
///   AB = beides
/// Protokoll: probe-log.txt neben der EXE.
/// </summary>
public static class Program
{
    private static readonly List<FrameWindow> Frames = new();
    private static string _mode = "A";
    private static StreamWriter? _logFile;
    private static Native.WinEventProc? _fgProc;   // gegen GC sichern

    [STAThread]
    public static void Main(string[] args)
    {
        _mode = args.FirstOrDefault()?.ToUpperInvariant() ?? "C";
        _logFile = new StreamWriter(Path.Combine(AppContext.BaseDirectory, "probe-log.txt"), append: false) { AutoFlush = true };
        Log($"Start, mode {_mode}, {Environment.OSVersion}");

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Startup += (_, _) => Start();
        app.Run();
    }

    private static void Log(string text) => _logFile?.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {text}");

    private static void Start()
    {
        bool owner = _mode.Contains('A');
        string w1 = @"C:\Windows\Web\Wallpaper\Theme1\img1.jpg";
        string w2 = @"C:\Windows\Web\Wallpaper\Theme2\img7.jpg";
        string w3 = @"C:\Windows\Web\Screen\img100.jpg";

        var primary = Forms.Screen.PrimaryScreen!.WorkingArea;
        Frames.Add(new FrameWindow("Frame 1", w1, primary.Left + 300, primary.Top + 200, 360, owner, Log));
        Frames.Add(new FrameWindow("Frame 2", w2, primary.Left + 800, primary.Top + 300, 280, owner, Log));
        foreach (var s in Forms.Screen.AllScreens.Where(s => !s.Primary))
            Frames.Add(new FrameWindow("Frame on " + s.DeviceName, w3, s.WorkingArea.Left + 200, s.WorkingArea.Top + 200, 320, owner, Log));
        foreach (var f in Frames)
        {
            f.FollowDesktop = _mode.Contains('C');
            f.AfterDrag = () => PlaceFrames("after drag");
            f.Show();
        }
        PlaceFrames("start");
        // Sicherheitsnetz: jede Sekunde prüfen (kostet fast nichts, wenn alles stimmt)
        var guard = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        guard.Tick += (_, _) => PlaceFrames("guard");
        guard.Start();

        // Strg+Umschalt gedrückt → Rahmen greifbar
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        timer.Tick += (_, _) =>
        {
            bool on = Native.IsKeyDown(Native.VK_CONTROL) && Native.IsKeyDown(Native.VK_SHIFT);
            foreach (var f in Frames) f.SetDragMode(on);
        };
        timer.Start();

        // Explorer-Neustart: Besitzer neu setzen
        uint taskbarCreated = Native.RegisterWindowMessage("TaskbarCreated");
        ComponentDispatcher.ThreadFilterMessage += (ref MSG msg, ref bool handled) =>
        {
            if (msg.message == (int)taskbarCreated) OnExplorerRestart();
        };
        // Broadcast erreicht nur Top-Level-Fenster; die Rahmen sind welche, ihr Hook leitet weiter
        foreach (var f in Frames)
            HwndSource.FromHwnd(f.Hwnd)!.AddHook((IntPtr h, int m, IntPtr w, IntPtr l, ref bool hd) =>
            {
                if (m == (int)taskbarCreated && h == Frames[0].Hwnd) OnExplorerRestart();
                return IntPtr.Zero;
            });

        _fgProc = OnForeground;
        Native.SetWinEventHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _fgProc, 0, 0, Native.WINEVENT_OUTOFCONTEXT);

        var tray = new Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "DeskFrames probe – mode " + _mode,
            Visible = true,
            ContextMenuStrip = new Forms.ContextMenuStrip(),
        };
        tray.ContextMenuStrip.Items.Add("Mode " + _mode).Enabled = false;
        tray.ContextMenuStrip.Items.Add("Log Z-order", null, (_, _) => LogZOrder("manual"));
        tray.ContextMenuStrip.Items.Add("Exit", null, (_, _) => { tray.Visible = false; Application.Current.Shutdown(); });
        LogZOrder("start");
    }

    /// <summary>Variante C: alle Rahmen direkt über das Fenster mit den Desktop-Symbolen legen, falls nötig.</summary>
    private static void PlaceFrames(string reason)
    {
        if (!_mode.Contains('C')) return;
        IntPtr host = Native.DesktopHost();
        if (host == IntPtr.Zero) return;
        // Schon richtig? Direkt über dem Host liegen genau die Rahmen
        IntPtr h = host;
        bool ok = true;
        // Unsichtbare Fenster (WPF-Hilfsfenster, verstecktes WorkerW) zählen nicht
        for (int i = 0; i < Frames.Count; i++)
        {
            do h = Native.GetWindow(h, Native.GW_HWNDPREV);
            while (h != IntPtr.Zero && !Native.IsWindowVisible(h));
            if (!Frames.Any(f => f.Hwnd == h)) { ok = false; break; }
        }
        if (ok) return;
        if (reason == "guard")
        {
            var chain = new List<string>();
            IntPtr c = host;
            for (int i = 0; i < 6; i++) { c = Native.GetWindow(c, Native.GW_HWNDPREV); chain.Add(c == IntPtr.Zero ? "0" : $"{Native.ClassOf(c)}{(Native.IsWindowVisible(c) ? "" : "(hidden)")}"); }
            Log("Chain above host: " + string.Join(" < ", chain));
        }
        IntPtr above = Native.GetWindow(host, Native.GW_HWNDPREV);
        while (above != IntPtr.Zero && Frames.Any(f => f.Hwnd == above)) above = Native.GetWindow(above, Native.GW_HWNDPREV);
        foreach (var f in Frames) f.PlaceAfter(above == IntPtr.Zero ? Native.HWND_TOP : above);
        Log($"Placed frames above desktop host {Native.ClassOf(host)} ({reason})");
    }

    private static void OnExplorerRestart()
    {
        Log("TaskbarCreated (Explorer restarted)");
        foreach (var f in Frames) { f.AttachToDesktop(); if (!_mode.Contains('C')) f.SendToBottom(); }
        PlaceFrames("explorer restart");
        LogZOrder("after explorer restart");
    }

    private static void OnForeground(IntPtr hook, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time)
    {
        if (Frames.Any(f => f.Hwnd == hwnd)) return;
        string cls = Native.ClassOf(hwnd);
        bool raised = IsDesktopRaised();
        Log($"Foreground: {cls} ({hwnd:X}), desktop raised={raised}");
        if (_mode.Contains('C'))
        {
            PlaceFrames("foreground");
            // Explorer ordnet nach dem Ereignis noch um – kurz danach erneut prüfen
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            t.Tick += (_, _) => { t.Stop(); PlaceFrames("foreground+300ms"); LogZOrder("foreground+300ms"); };
            t.Start();
        }
        if (_mode.Contains('B'))
        {
            foreach (var f in Frames)
            {
                f.AllowRaise = raised;
                if (raised) f.BringToTop(); else f.SendToBottom();
            }
        }
        LogZOrder("after foreground");
    }

    /// <summary>„Desktop anzeigen“ aktiv: Über dem Desktop-Fenster liegt kein normales, sichtbares Programmfenster mehr –
    /// erkannt daran, dass unter ihm noch normale Fenster liegen.</summary>
    private static bool IsDesktopRaised()
    {
        IntPtr host = Native.DesktopHost();
        var z = Native.ZOrder();
        int idx = z.IndexOf(host);
        if (idx < 0) return false;
        foreach (var h in z.Skip(idx + 1))
        {
            if (Frames.Any(f => f.Hwnd == h) || Native.IsIconic(h)) continue;
            string c = Native.ClassOf(h);
            if (c is "Progman" or "WorkerW") continue;
            long ex = (long)Native.GetWindowLongPtr(h, Native.GWL_EXSTYLE);
            if ((ex & Native.WS_EX_TOOLWINDOW) != 0) continue;
            return true;
        }
        return false;
    }

    private static void LogZOrder(string reason)
    {
        var z = Native.ZOrder();
        IntPtr host = Native.DesktopHost();
        var parts = new List<string>();
        for (int i = 0; i < z.Count; i++)
        {
            var f = Frames.FirstOrDefault(x => x.Hwnd == z[i]);
            if (f != null) parts.Add($"#{i}={f.Label}");
            else if (z[i] == host) parts.Add($"#{i}=DESKTOP({Native.ClassOf(z[i])})");
        }
        Log($"Z [{reason}] of {z.Count} visible: {string.Join(", ", parts)}; top8: " +
            string.Join(" | ", z.Take(8).Select(h => Native.ClassOf(h) + (h == host ? "*" : ""))));
    }
}
