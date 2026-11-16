using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using DeskFrames.Imaging;
using DeskFrames.Model;
using DeskFrames.Native;

namespace DeskFrames.Frames;

/// <summary>
/// Ein Rahmen auf dem Desktop: randloses, transparentes Fenster ohne Taskleisten-Eintrag, das nie den Fokus nimmt
/// und Klicks durchlässt. Solange Strg+Umschalt gedrückt ist (<see cref="SetDragMode"/>), lässt es sich verschieben
/// und an Rändern/Ecken mit festem Seitenverhältnis skalieren.
/// </summary>
internal sealed class FrameWindow : Window
{
    private static readonly Brush MarkerBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80)));
    private static readonly Brush PlaceholderBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x3A, 0x3C, 0x40)));
    private static readonly Brush PlaceholderTextBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xC8, 0xCA, 0xCE)));

    private readonly Func<IReadOnlyList<MonitorInfo>> _monitors;
    private readonly Border _frame;
    private readonly Image _image;
    private readonly Border _placeholder;
    private readonly TextBlock _placeholderTitle;
    private readonly TextBlock _placeholderFile;
    private readonly Rectangle _marker;

    private bool _dragMode;
    private bool _moving;
    private bool _resizing;
    private bool _placing;
    private Grip _grip;
    private PixelRect _startRect;
    private Win32.POINT _startCursor;
    private double _startScale, _minInner, _maxInner;
    private readonly System.Windows.Threading.DispatcherTimer _resizeTimer;
    private int _loadToken;
    private LoadedImage? _loaded;

    public FrameConfig Config { get; }
    public IntPtr Hwnd { get; private set; }

    /// <summary>Position, Größe oder Seitenverhältnis wurden geändert – speichern.</summary>
    public event Action<FrameWindow>? Changed;

    public ImageStatus ImageStatus => _loaded?.Status ?? ImageStatus.NoPath;

    public FrameWindow(FrameConfig config, Func<IReadOnlyList<MonitorInfo>> monitors)
    {
        Config = config;
        _monitors = monitors;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        UseLayoutRounding = true;
        Title = "DeskFrames frame";

        _image = new Image { Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);

        _placeholderTitle = new TextBlock
        {
            Foreground = PlaceholderTextBrush, FontSize = 13, FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap,
        };
        _placeholderFile = new TextBlock
        {
            Foreground = PlaceholderTextBrush, FontSize = 11, Opacity = 0.8, Margin = new Thickness(0, 4, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
        };
        _placeholder = new Border
        {
            Background = PlaceholderBrush,
            Visibility = Visibility.Collapsed,
            Child = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8),
                Children = { _placeholderTitle, _placeholderFile },
            },
        };

        _frame = new Border { Child = new Grid { Children = { _placeholder, _image } } };
        _marker = new Rectangle { Stroke = MarkerBrush, StrokeThickness = 2, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
        // Fast unsichtbarer Hintergrund, damit im Verschiebemodus auch ein leerer Rahmen die Maus annimmt
        Content = new Grid { Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)), Children = { _frame, _marker } };

        ApplyStyle();
        ApplySizeDip();

        SourceInitialized += (_, _) => Setup();
        DpiChanged += (_, _) =>
        {
            // Beim ersten Platzieren auf einem Monitor mit anderer Skalierung skaliert WPF das Fenster neu –
            // danach Position und Größe noch einmal exakt setzen. Beim Ziehen übernimmt das CommitPosition.
            if (!_moving && !_resizing) Dispatcher.BeginInvoke(PlaceFromConfig);
        };
        MouseMove += OnMouseMove;
        MouseLeftButtonDown += OnMouseDown;
        _resizeTimer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(10),
        };
        _resizeTimer.Tick += OnResizeTick;
    }

    private void Setup()
    {
        Hwnd = new WindowInteropHelper(this).Handle;
        long ex = (long)Win32.GetWindowLongPtr(Hwnd, Win32.GWL_EXSTYLE);
        ex |= Win32.WS_EX_TRANSPARENT | Win32.WS_EX_LAYERED | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE;
        Win32.SetWindowLongPtr(Hwnd, Win32.GWL_EXSTYLE, (IntPtr)ex);
        HwndSource.FromHwnd(Hwnd)!.AddHook(WndProc);
        PlaceFromConfig();
    }

    // ---------- Stil und Größe ----------

    public void ApplyStyle()
    {
        _frame.BorderBrush = Frozen(new SolidColorBrush((Color)ColorConverter.ConvertFromString(Config.BorderColor)));
        _frame.BorderThickness = new Thickness(Config.BorderWidth);
    }

    private void ApplySizeDip()
    {
        Width = Config.Width + 2 * Config.BorderWidth;
        Height = Config.Height + 2 * Config.BorderWidth;
    }

    private double CurrentScale => VisualTreeHelper.GetDpi(this).DpiScaleX;

    /// <summary>Position und Größe aus <see cref="Config"/> setzen (physische Pixel, über Win32).</summary>
    public void PlaceFromConfig()
    {
        if (Hwnd == IntPtr.Zero) return;
        var monitors = _monitors();
        if (monitors.Count == 0) return;
        double scale = Placement.ResolveMonitor(Config.Monitor, monitors).Scale;
        var (w, h) = FrameGeometry.OuterSize(Config.Width * scale, Config.AspectRatio, Config.BorderWidth * scale);
        var (_, x, y) = Placement.Resolve(Config, w, h, monitors);
        SetBounds(new PixelRect(x, y, w, h));
    }

    private void SetBounds(PixelRect r) =>
        Win32.SetWindowPos(Hwnd, IntPtr.Zero, r.Left, r.Top, r.Width, r.Height, Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);

    private PixelRect GetBounds()
    {
        Win32.GetWindowRect(Hwnd, out var r);
        return new PixelRect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
    }

    /// <summary>Aktuelle Lage in <see cref="Config"/> übernehmen; die Bildbreite nur nach Skalieren.</summary>
    private void CommitPosition(bool sizeChanged)
    {
        var r = GetBounds();
        var (name, x, y) = Placement.ToStored(r.Left, r.Top, r.Width, r.Height, _monitors());
        Config.Monitor = name;
        Config.X = x;
        Config.Y = y;
        if (sizeChanged)
        {
            double scale = CurrentScale;
            Config.Width = Math.Max(1, r.Width - 2 * Config.BorderWidth * scale) / scale;
        }
        Changed?.Invoke(this);
    }

    // ---------- Desktop-Ebene ----------

    /// <summary>Von <see cref="Desktop.DesktopLayer"/>: direkt unter <paramref name="insertAfter"/> einordnen.</summary>
    public void PlaceAfter(IntPtr insertAfter)
    {
        _placing = true;
        Win32.SetWindowPos(Hwnd, insertAfter, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
        _placing = false;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Win32.WM_WINDOWPOSCHANGING && !_placing)
        {
            // Die Z-Reihenfolge bestimmt allein die DesktopLayer – alle anderen Versuche (Klick, Verschieben) ignorieren
            var pos = Marshal.PtrToStructure<Win32.WINDOWPOS>(lParam);
            if ((pos.flags & Win32.SWP_NOZORDER) == 0)
            {
                pos.flags |= Win32.SWP_NOZORDER;
                Marshal.StructureToPtr(pos, lParam, false);
            }
        }
        else if (msg == Win32.WM_MOUSEACTIVATE)
        {
            handled = true;
            return (IntPtr)Win32.MA_NOACTIVATE;
        }
        return IntPtr.Zero;
    }

    // ---------- Verschieben und Skalieren ----------

    /// <summary>Strg+Umschalt gedrückt: Klicks annehmen und grün markieren; sonst durchklickbar.</summary>
    public void SetDragMode(bool on)
    {
        if (Hwnd == IntPtr.Zero || on == _dragMode) return;
        if (!on && (_resizing || _moving)) return;   // erst nach dem Loslassen der Maus
        _dragMode = on;
        long ex = (long)Win32.GetWindowLongPtr(Hwnd, Win32.GWL_EXSTYLE);
        ex = on ? ex & ~(long)Win32.WS_EX_TRANSPARENT : ex | Win32.WS_EX_TRANSPARENT;
        Win32.SetWindowLongPtr(Hwnd, Win32.GWL_EXSTYLE, (IntPtr)ex);
        _marker.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (!on) Cursor = null;
    }

    private static Cursor CursorFor(Grip grip) => grip switch
    {
        Grip.Left or Grip.Right => Cursors.SizeWE,
        Grip.Top or Grip.Bottom => Cursors.SizeNS,
        Grip.TopLeft or Grip.BottomRight => Cursors.SizeNWSE,
        Grip.TopRight or Grip.BottomLeft => Cursors.SizeNESW,
        _ => Cursors.SizeAll,
    };

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragMode) return;
        if (_resizing) return;
        var p = e.GetPosition(this);
        Cursor = CursorFor(FrameGeometry.HitTest(p.X, p.Y, ActualWidth, ActualHeight));
    }

    /// <summary>
    /// Skalieren ohne Mausfang (Capture): Die Rahmen werden nie aktiv, dadurch geht der Fang sofort verloren.
    /// Stattdessen fragt ein schneller Timer Mausposition und Taste direkt ab – wie die Verschiebe-Schleife von Windows.
    /// </summary>
    private void OnResizeTick(object? sender, EventArgs e)
    {
        if (!Win32.IsKeyDown(Win32.PrimaryButton))
        {
            EndResize();
            return;
        }
        Win32.GetCursorPos(out var c);
        var r = FrameGeometry.Resize(_startRect, _grip, c.X - _startCursor.X, c.Y - _startCursor.Y,
            Config.AspectRatio, Config.BorderWidth * _startScale, _minInner, _maxInner);
        if (r != GetBounds()) SetBounds(r);
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_dragMode) return;
        var p = e.GetPosition(this);
        _grip = FrameGeometry.HitTest(p.X, p.Y, ActualWidth, ActualHeight);
        e.Handled = true;

        if (_grip == Grip.Move)
        {
            _moving = true;
            try { DragMove(); }   // kehrt zurück, sobald die Maustaste losgelassen wird
            catch (InvalidOperationException) { }
            _moving = false;
            CommitPosition(sizeChanged: false);
            return;
        }

        _startRect = GetBounds();
        Win32.GetCursorPos(out _startCursor);
        _startScale = CurrentScale;
        double border = Config.BorderWidth * _startScale;
        var monitors = _monitors();
        var (name, _, _) = Placement.ToStored(_startRect.Left, _startRect.Top, _startRect.Width, _startRect.Height, monitors);
        var monitor = Placement.ResolveMonitor(name, monitors);
        _minInner = FrameConfig.MinWidthFor(Config.AspectRatio) * _startScale;
        _maxInner = Math.Min(FrameConfig.MaxWidthFor(Config.AspectRatio) * _startScale,
            FrameGeometry.MaxInnerWidthOnMonitor(monitor.Bounds, Config.AspectRatio, border));
        _resizing = true;
        _resizeTimer.Start();
    }

    private void EndResize()
    {
        if (!_resizing) return;
        _resizing = false;
        _resizeTimer.Stop();
        CommitPosition(sizeChanged: true);
        ReloadIfTooSmall();
    }

    // ---------- Bild ----------

    /// <summary>Neues Bild verknüpfen; ein anderes Seitenverhältnis übernimmt der Rahmen (Fläche und Mitte bleiben).</summary>
    public void SetImage(string path)
    {
        Config.ImagePath = path;
        _ = LoadImageAsync();
    }

    /// <summary>Lädt das Bild im Hintergrund. Fehlt es, erscheint der Platzhalter.</summary>
    public async Task LoadImageAsync()
    {
        int token = ++_loadToken;
        string path = Config.ImagePath;
        int target = DecodeSizeFor(NeededLongSide());
        var result = await Task.Run(() => ImageLoader.Load(path, target));
        if (token != _loadToken) return;   // inzwischen ein anderes Bild gewählt
        _loaded = result;

        if (result.Status == ImageStatus.Ok)
        {
            _image.Source = result.Bitmap;
            _image.Visibility = Visibility.Visible;
            _placeholder.Visibility = Visibility.Collapsed;
            // Anderes Seitenverhältnis (neues Bild oder Datei ersetzt): Rahmen passt sich an
            if (Math.Abs(result.AspectRatio - Config.AspectRatio) / Config.AspectRatio > 0.005)
                AdoptAspect(result.AspectRatio);
        }
        else
        {
            _image.Source = null;
            _image.Visibility = Visibility.Collapsed;
            _placeholderTitle.Text = result.Status switch
            {
                ImageStatus.Missing => "Image not found",
                ImageStatus.Unreadable => "Image can't be opened",
                _ => "No image",
            };
            _placeholderFile.Text = string.IsNullOrEmpty(path) ? "" : System.IO.Path.GetFileName(path);
            _placeholder.Visibility = Visibility.Visible;
        }
    }

    private void AdoptAspect(double aspect)
    {
        if (Hwnd == IntPtr.Zero) return;
        double scale = CurrentScale;
        double border = Config.BorderWidth * scale;
        var start = GetBounds();
        var monitors = _monitors();
        var (name, _, _) = Placement.ToStored(start.Left, start.Top, start.Width, start.Height, monitors);
        var monitor = Placement.ResolveMonitor(name, monitors);
        double min = FrameConfig.MinWidthFor(aspect) * scale;
        double max = Math.Min(FrameConfig.MaxWidthFor(aspect) * scale, FrameGeometry.MaxInnerWidthOnMonitor(monitor.Bounds, aspect, border));
        var r = FrameGeometry.ChangeAspect(start, border, aspect, min, max);

        // Vollständig auf dem Monitor halten
        var b = monitor.Bounds;
        r = r with
        {
            Left = Math.Clamp(r.Left, b.Left, Math.Max(b.Left, b.Right - r.Width)),
            Top = Math.Clamp(r.Top, b.Top, Math.Max(b.Top, b.Bottom - r.Height)),
        };

        Config.AspectRatio = aspect;
        SetBounds(r);
        CommitPosition(sizeChanged: true);
    }

    private int NeededLongSide()
    {
        if (Hwnd == IntPtr.Zero) return 1024;
        var r = GetBounds();
        double border = 2 * Config.BorderWidth * CurrentScale;
        return (int)Math.Ceiling(Math.Max(r.Width - border, r.Height - border));
    }

    /// <summary>Etwas Reserve und in Stufen, damit nicht jede kleine Größenänderung neu dekodiert.</summary>
    private static int DecodeSizeFor(int needed) =>
        Math.Clamp((int)Math.Ceiling(needed * 1.25 / 256.0) * 256, 512, 8192);

    /// <summary>Nach dem Vergrößern schärfer nachladen, falls das Bild bisher kleiner dekodiert war.</summary>
    private void ReloadIfTooSmall()
    {
        if (_loaded is not { Status: ImageStatus.Ok } img) return;
        if (img.DecodedLongSide >= img.FullLongSide) return;
        if (NeededLongSide() > img.DecodedLongSide) _ = LoadImageAsync();
    }

    private static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }
}
