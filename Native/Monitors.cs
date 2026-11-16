using System.Runtime.InteropServices;
using DeskFrames.Model;

namespace DeskFrames.Native;

/// <summary>Liste der Monitore in physischen Pixeln samt Skalierung (Prozess ist PerMonitorV2).</summary>
internal static class Monitors
{
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor, rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, IntPtr rect, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFOEX info);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

    private const uint MONITORINFOF_PRIMARY = 1;

    public static List<MonitorInfo> GetAll()
    {
        var list = new List<MonitorInfo>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (h, _, _, _) =>
        {
            var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            if (GetMonitorInfo(h, ref info))
            {
                double scale = GetDpiForMonitor(h, 0 /* MDT_EFFECTIVE_DPI */, out uint dpi, out _) == 0 && dpi > 0 ? dpi / 96.0 : 1.0;
                var r = info.rcMonitor;
                list.Add(new MonitorInfo(info.szDevice, new PixelRect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top),
                    (info.dwFlags & MONITORINFOF_PRIMARY) != 0, scale));
            }
            return true;
        }, IntPtr.Zero);
        return list;
    }
}
