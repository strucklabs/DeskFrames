using System.IO;
using System.Windows;
using DeskFrames.Frames;
using DeskFrames.Imaging;
using DeskFrames.Model;
using Forms = System.Windows.Forms;

namespace DeskFrames;

/// <summary>
/// Einstieg: Einzelinstanz sicherstellen, Rahmen laden und zeigen, Tray-Symbol anlegen.
/// Ein zweiter Start meldet sich bei der laufenden Instanz und beendet sich sofort.
/// </summary>
public static class Program
{
    private const string MutexName = @"Local\DeskFrames.SingleInstance";
    private const string ActivateEventName = @"Local\DeskFrames.Activate";

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
        var manager = new FrameManager(AppConfig.Load(_configPath), _configPath);
        manager.ShowAll();

        var tray = new Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "DeskFrames",
            Visible = true,
            ContextMenuStrip = new Forms.ContextMenuStrip(),
        };
        // Vorläufig (S2): nur hinzufügen und beenden – der Rest des Menüs folgt in S3
        tray.ContextMenuStrip.Items.Add("Add frame…", null, async (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Choose an image", Filter = ImageLoader.DialogFilter };
            if (dialog.ShowDialog() != true) return;
            if (!await manager.AddFromFileAsync(dialog.FileName))
                MessageBox.Show("This file can't be opened as an image.", "DeskFrames", MessageBoxButton.OK, MessageBoxImage.Warning);
        });
        tray.ContextMenuStrip.Items.Add(new Forms.ToolStripSeparator());
        tray.ContextMenuStrip.Items.Add("Exit", null, (_, _) =>
        {
            tray.Visible = false;
            tray.Dispose();
            manager.Dispose();
            app.Shutdown();
        });

        // Zweiter Start: Hinweis, dass DeskFrames schon läuft (ab S3: Verwaltungsfenster öffnen)
        ThreadPool.RegisterWaitForSingleObject(activate, (_, _) =>
            app.Dispatcher.BeginInvoke(() => tray.ShowBalloonTip(3000, "DeskFrames", "DeskFrames is already running.", Forms.ToolTipIcon.Info)),
            null, Timeout.Infinite, executeOnlyOnce: false);
    }
}
