using System.IO;
using System.Windows;
using DeskFrames.Frames;
using DeskFrames.Model;
using DeskFrames.UI;

namespace DeskFrames;

/// <summary>
/// Einstieg: Einzelinstanz sicherstellen, Rahmen laden und zeigen, Tray-Symbol anlegen.
/// Ein zweiter Start meldet sich bei der laufenden Instanz und beendet sich sofort.
/// </summary>
public static class Program
{
    private const string MutexName = @"Local\DeskFrames.SingleInstance";
    private const string ActivateEventName = @"Local\DeskFrames.Activate";

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);

    /// <summary>Andere frames.json (für Tests und Probeläufe): <c>DeskFrames.exe --config &lt;Datei&gt;</c>.</summary>
    private static string? _configPath;

    [STAThread]
    public static void Main(string[] args)
    {
        int i = Array.IndexOf(args, "--config");
        if (i >= 0 && i + 1 < args.Length) _configPath = Path.GetFullPath(args[i + 1]);

        using var mutex = new Mutex(initiallyOwned: true, MutexName, out bool first);
        if (!first)
        {
            // Laufende Instanz wecken – sie öffnet ihr Verwaltungsfenster
            // Erlaubnis weitergeben, das Fenster nach vorn zu holen (sonst blinkt es nur in der Taskleiste)
            AllowSetForegroundWindow(-1 /* ASFW_ANY */);
            try { using var ev = EventWaitHandle.OpenExisting(ActivateEventName); ev.Set(); }
            catch (WaitHandleCannotBeOpenedException) { }
            return;
        }

        using var activate = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.DispatcherUnhandledException += (_, e) => { ErrorLog.Write(e.Exception); e.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => { if (e.ExceptionObject is Exception ex) ErrorLog.Write(ex); };

        app.Startup += (_, _) => Start(app, activate);
        app.Run();
    }

    private static void Start(Application app, EventWaitHandle activate)
    {
        bool firstRun = !File.Exists(_configPath ?? AppConfig.DefaultPath);
        var manager = new FrameManager(AppConfig.Load(_configPath), _configPath);
        manager.ShowAll();
        var controller = new AppController(app, manager);
        app.Exit += (_, _) => controller.Dispose();   // auch bei Abmelden/Herunterfahren speichern

        // Allererster Start: Verwaltungsfenster zeigen, damit man weiß, wo es losgeht
        if (firstRun) controller.ShowManageWindow();

        // Zweiter Start (Doppelklick auf die EXE): Verwaltungsfenster der laufenden Instanz öffnen
        ThreadPool.RegisterWaitForSingleObject(activate, (_, _) => app.Dispatcher.BeginInvoke(() => controller.ShowManageWindow()),
            null, Timeout.Infinite, executeOnlyOnce: false);
    }
}
