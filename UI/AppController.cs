using System.Windows;
using DeskFrames.Frames;
using DeskFrames.Imaging;
using DeskFrames.Model;
using Forms = System.Windows.Forms;

namespace DeskFrames.UI;

/// <summary>
/// Gemeinsame Befehle für Tray-Menü, Verwaltungsfenster und Kontextmenü am Rahmen –
/// so verhalten sich alle drei Wege gleich (Rückfragen, Fehlermeldungen, Texte).
/// </summary>
internal sealed class AppController : IDisposable
{
    private readonly Application _app;
    private readonly Forms.NotifyIcon _tray;
    private ManageWindow? _manageWindow;

    public FrameManager Frames { get; }
    public Autostart Autostart { get; } = new();

    public const string AppName = "DeskFrames";

    public AppController(Application app, FrameManager frames)
    {
        _app = app;
        Frames = frames;
        Frames.ContextMenuRequested += ShowFrameMenu;

        _tray = new Forms.NotifyIcon
        {
            Icon = AppIcon.Load(),
            Text = AppName,
            Visible = true,
            ContextMenuStrip = BuildTrayMenu(),
        };
        _tray.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) ShowManageWindow(); };
    }

    // ---------- Menüs ----------

    private Forms.ContextMenuStrip BuildTrayMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Add frame…", null, async (_, _) => await AddFrameAsync());
        menu.Items.Add("Manage frames…", null, (_, _) => ShowManageWindow());
        menu.Items.Add(new Forms.ToolStripSeparator());
        var autostart = new Forms.ToolStripMenuItem("Start with Windows");
        autostart.Click += (_, _) => SetAutostart(!Autostart.IsEnabled);
        menu.Items.Add(autostart);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Exit());
        menu.Opening += (_, _) => autostart.Checked = Autostart.IsEnabled;
        return menu;
    }

    /// <summary>Rechtsklick auf einen Rahmen bei gedrückter Strg+Umschalt.</summary>
    private void ShowFrameMenu(FrameWindow frame)
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Change image…", null, async (_, _) => await ChangeImageAsync(frame));

        var colors = new Forms.ToolStripMenuItem("Border color");
        foreach (var (name, color) in StylePresets.Colors)
        {
            var item = new Forms.ToolStripMenuItem(name, SwatchImage(color), (_, _) => Frames.SetBorder(frame, color, frame.Config.BorderWidth))
            {
                Checked = string.Equals(color, frame.Config.BorderColor, StringComparison.OrdinalIgnoreCase),
            };
            colors.DropDownItems.Add(item);
        }
        colors.DropDownItems.Add(new Forms.ToolStripSeparator());
        colors.DropDownItems.Add("Custom…", null, (_, _) =>
        {
            if (PickColor(frame.Config.BorderColor) is { } c) Frames.SetBorder(frame, c, frame.Config.BorderWidth);
        });
        menu.Items.Add(colors);

        var widths = new Forms.ToolStripMenuItem("Border width");
        foreach (int w in StylePresets.Widths)
        {
            widths.DropDownItems.Add(new Forms.ToolStripMenuItem(w == 0 ? "No border" : $"{w} px", null,
                (_, _) => Frames.SetBorder(frame, frame.Config.BorderColor, w)) { Checked = w == frame.Config.BorderWidth });
        }
        menu.Items.Add(widths);

        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Manage frames…", null, (_, _) => ShowManageWindow(frame));
        menu.Items.Add("Remove frame", null, (_, _) => RemoveFrame(frame));
        menu.Closed += (_, _) => _app.Dispatcher.BeginInvoke(menu.Dispose);
        menu.Show(Forms.Cursor.Position);
    }

    private static System.Drawing.Image SwatchImage(string hex)
    {
        var bmp = new System.Drawing.Bitmap(16, 16);
        using var g = System.Drawing.Graphics.FromImage(bmp);
        g.Clear(System.Drawing.ColorTranslator.FromHtml(hex));
        g.DrawRectangle(System.Drawing.Pens.Gray, 0, 0, 15, 15);
        return bmp;
    }

    // ---------- Befehle ----------

    public void ShowManageWindow(FrameWindow? select = null)
    {
        if (_manageWindow == null)
        {
            _manageWindow = new ManageWindow(this);
            _manageWindow.Closed += (_, _) => _manageWindow = null;
            _manageWindow.Show();
        }
        if (_manageWindow.WindowState == WindowState.Minimized) _manageWindow.WindowState = WindowState.Normal;
        _manageWindow.Activate();
        if (select != null) _manageWindow.Select(select);
    }

    private static string? AskForImage(string title, string? current = null)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = title, Filter = ImageLoader.DialogFilter };
        if (!string.IsNullOrEmpty(current))
        {
            try
            {
                string? dir = System.IO.Path.GetDirectoryName(current);
                if (dir != null && System.IO.Directory.Exists(dir)) dialog.InitialDirectory = dir;
            }
            catch (ArgumentException) { }
        }
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    private static void ShowCannotOpen() =>
        MessageBox.Show("This file can't be opened as an image.\nPlease choose a picture file such as JPG or PNG.",
            AppName, MessageBoxButton.OK, MessageBoxImage.Warning);

    public async Task<FrameWindow?> AddFrameAsync()
    {
        if (AskForImage("Choose a picture for the new frame") is not { } path) return null;
        var frame = await Frames.AddFromFileAsync(path);
        if (frame == null) { ShowCannotOpen(); return null; }
        _manageWindow?.Select(frame);
        return frame;
    }

    public async Task ChangeImageAsync(FrameWindow frame)
    {
        if (AskForImage("Choose a new picture for this frame", frame.Config.ImagePath) is not { } path) return;
        if (!await Frames.ChangeImageAsync(frame, path)) ShowCannotOpen();
    }

    public bool RemoveFrame(FrameWindow frame)
    {
        var answer = MessageBox.Show("Remove this frame from the desktop?\nThe picture file itself is not deleted.",
            AppName, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return false;
        Frames.Remove(frame);
        return true;
    }

    /// <summary>Eigene Farbe wählen; null bei Abbruch.</summary>
    public static string? PickColor(string current)
    {
        using var dialog = new Forms.ColorDialog { FullOpen = true, AnyColor = true };
        try { dialog.Color = System.Drawing.ColorTranslator.FromHtml(current); }
        catch (Exception ex) when (ex is ArgumentException or FormatException) { }
        if (dialog.ShowDialog() != Forms.DialogResult.OK) return null;
        var c = dialog.Color;
        return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    }

    public void SetAutostart(bool enabled)
    {
        if (!Autostart.Set(enabled))
            MessageBox.Show("The setting could not be saved.", AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
        _manageWindow?.RefreshAutostart();
    }

    public void Exit()
    {
        _manageWindow?.Close();
        Dispose();
        _app.Shutdown();
    }

    private bool _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _tray.Visible = false;
        _tray.Dispose();
        Frames.Dispose();
    }
}
