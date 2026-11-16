using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DeskFramesProbe;

/// <summary>Ein Rahmen: randloses, transparentes Fenster ganz unten in der Z-Reihenfolge.</summary>
internal sealed class FrameWindow : Window
{
    private static readonly Brush NormalBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1C)));
    private static readonly Brush DragBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80)));

    private readonly Border _border;
    private readonly int _startX, _startY;
    private readonly bool _ownerProgman;
    private readonly Action<string> _log;
    private bool _dragMode;

    public IntPtr Hwnd { get; private set; }
    public string Label { get; }

    /// <summary>Variante B: Solange der Desktop „angezeigt“ ist (Win+D), darf der Rahmen nach oben.</summary>
    public bool AllowRaise { get; set; }

    public Action? AfterDrag { get; set; }

    public FrameWindow(string label, string imagePath, int physX, int physY, double widthDip, bool ownerProgman, Action<string> log)
    {
        Label = label;
        _startX = physX; _startY = physY;
        _ownerProgman = ownerProgman;
        _log = log;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Title = "DeskFrames probe " + label;

        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.UriSource = new Uri(imagePath);
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.DecodePixelWidth = 900;
        bmp.EndInit();
        bmp.Freeze();

        _border = new Border
        {
            BorderBrush = NormalBrush,
            BorderThickness = new Thickness(6),
            Background = Brushes.Black,
            Child = new Image { Source = bmp, Stretch = Stretch.Uniform },
        };
        Content = _border;
        Width = widthDip;
        Height = widthDip / ((double)bmp.PixelWidth / bmp.PixelHeight) ;

        SourceInitialized += (_, _) => Setup();
        MouseLeftButtonDown += OnMouseDown;
    }

    private void Setup()
    {
        Hwnd = new WindowInteropHelper(this).Handle;
        long ex = (long)Native.GetWindowLongPtr(Hwnd, Native.GWL_EXSTYLE);
        ex |= Native.WS_EX_TRANSPARENT | Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
        Native.SetWindowLongPtr(Hwnd, Native.GWL_EXSTYLE, (IntPtr)ex);
        HwndSource.FromHwnd(Hwnd)!.AddHook(WndProc);
        AttachToDesktop();
        Native.SetWindowPos(Hwnd, Native.HWND_BOTTOM, _startX, _startY, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        _log($"{Label}: hwnd={Hwnd:X} owner={(_ownerProgman ? "Progman" : "none")}");
    }

    /// <summary>Variante A: Desktop-Fenster als Besitzer. Auch nach Explorer-Neustart erneut aufrufen.</summary>
    public void AttachToDesktop()
    {
        if (!_ownerProgman || Hwnd == IntPtr.Zero) return;
        IntPtr progman = Native.FindWindow("Progman", null);
        Native.SetWindowLongPtr(Hwnd, Native.GWLP_HWNDPARENT, progman);
        _log($"{Label}: owner set to Progman {progman:X}");
    }

    /// <summary>Variante C: Rahmen folgt dem Fenster mit den Desktop-Symbolen.</summary>
    public bool FollowDesktop { get; set; }
    private bool Placing { get; set; }

    /// <summary>Direkt unter <paramref name="insertAfter"/> einordnen (also direkt über dem Desktop-Fenster).</summary>
    public void PlaceAfter(IntPtr insertAfter)
    {
        Placing = true;
        Native.SetWindowPos(Hwnd, insertAfter, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        Placing = false;
    }

    public void SendToBottom() =>
        Native.SetWindowPos(Hwnd, Native.HWND_BOTTOM, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);

    public void BringToTop() =>
        Native.SetWindowPos(Hwnd, Native.HWND_TOP, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_WINDOWPOSCHANGING && FollowDesktop && !Placing)
        {
            // Variante C: Z-Reihenfolge bestimmt nur PlaceAbove – alle anderen Versuche werden ignoriert
            var pos = System.Runtime.InteropServices.Marshal.PtrToStructure<Native.WINDOWPOS>(lParam);
            if ((pos.flags & Native.SWP_NOZORDER) == 0)
            {
                pos.flags |= Native.SWP_NOZORDER;
                System.Runtime.InteropServices.Marshal.StructureToPtr(pos, lParam, false);
            }
        }
        else if (msg == Native.WM_WINDOWPOSCHANGING && !FollowDesktop && !AllowRaise && !_dragMode)
        {
            var pos = System.Runtime.InteropServices.Marshal.PtrToStructure<Native.WINDOWPOS>(lParam);
            if ((pos.flags & Native.SWP_NOZORDER) == 0 && pos.hwndInsertAfter != Native.HWND_BOTTOM)
            {
                pos.hwndInsertAfter = Native.HWND_BOTTOM;
                System.Runtime.InteropServices.Marshal.StructureToPtr(pos, lParam, false);
            }
        }
        else if (msg == Native.WM_MOUSEACTIVATE)
        {
            handled = true;
            return (IntPtr)Native.MA_NOACTIVATE;
        }
        return IntPtr.Zero;
    }

    public void SetDragMode(bool on)
    {
        if (on == _dragMode || Hwnd == IntPtr.Zero) return;
        _dragMode = on;
        long ex = (long)Native.GetWindowLongPtr(Hwnd, Native.GWL_EXSTYLE);
        ex = on ? ex & ~(long)Native.WS_EX_TRANSPARENT : ex | Native.WS_EX_TRANSPARENT;
        Native.SetWindowLongPtr(Hwnd, Native.GWL_EXSTYLE, (IntPtr)ex);
        _border.BorderBrush = on ? DragBrush : NormalBrush;
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_dragMode) return;
        DragMove();
        Native.GetWindowRect(Hwnd, out var r);
        _log($"{Label}: moved to {r.Left},{r.Top} size {r.Right - r.Left}x{r.Bottom - r.Top}");
        AfterDrag?.Invoke();
        if (!AllowRaise && !FollowDesktop) SendToBottom();
    }

    private static Brush Freeze(Brush b) { b.Freeze(); return b; }
}
