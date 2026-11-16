using System.Windows;
using System.Windows.Threading;
using DeskFrames.Model;
using Forms = System.Windows.Forms;

namespace DeskFrames;

/// <summary>
/// Einstieg: Einzelinstanz sicherstellen, Rahmen laden, Tray-Symbol anlegen.
/// Ein zweiter Start meldet sich bei der laufenden Instanz und beendet sich sofort.
/// </summary>
public static class Program
{
    private const string MutexName = @"Local\DeskFrames.SingleInstance";
    private const string ActivateEventName = @"Local\DeskFrames.Activate";

    [STAThread]
    public static void Main()
    {
        using var mutex = new Mutex(initiallyOwned: true, MutexName, out bool first);
        if (!first)
        {
            // Laufende Instanz wecken (öffnet ab S3 das Verwaltungsfenster)
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
        var config = AppConfig.Load();

        var tray = new Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "DeskFrames",
            Visible = true,
            ContextMenuStrip = new Forms.ContextMenuStrip(),
        };
        tray.ContextMenuStrip.Items.Add("Exit", null, (_, _) =>
        {
            tray.Visible = false;
            tray.Dispose();
            app.Shutdown();
        });

        // Zweiter Start: Hinweis, dass DeskFrames schon läuft (ab S3: Verwaltungsfenster öffnen)
        ThreadPool.RegisterWaitForSingleObject(activate, (_, _) =>
            app.Dispatcher.BeginInvoke(() => tray.ShowBalloonTip(3000, "DeskFrames", "DeskFrames is already running.", Forms.ToolTipIcon.Info)),
            null, Timeout.Infinite, executeOnlyOnce: false);

        // Rahmenfenster folgen in S2
        _ = config;
    }
}
