using DeskFrames.Model;

namespace DeskFrames.Tests;

public class PlacementTests
{
    // Typischer Aufbau: großer Hauptmonitor, zweiter Monitor links oben mit negativen Koordinaten
    private static readonly MonitorInfo Main = new(@"\\.\DISPLAY6", new PixelRect(0, 0, 3440, 1440), true, 1.0);
    private static readonly MonitorInfo Second = new(@"\\.\DISPLAY7", new PixelRect(-1756, -1080, 1920, 1080), false, 1.25);
    private static readonly IReadOnlyList<MonitorInfo> Both = new[] { Second, Main };

    private static FrameConfig At(string? monitor, int x, int y) => new() { Monitor = monitor, X = x, Y = y };

    [Fact]
    public void KnownMonitor_OffsetFromItsCorner()
    {
        var (m, x, y) = Placement.Resolve(At(@"\\.\DISPLAY7", 200, 100), 300, 200, Both);
        Assert.Same(Second, m);
        Assert.Equal(-1556, x);
        Assert.Equal(-980, y);
    }

    [Fact]
    public void MonitorName_IsCaseInsensitive()
    {
        var (m, _, _) = Placement.Resolve(At(@"\\.\display7", 0, 0), 100, 100, Both);
        Assert.Same(Second, m);
    }

    [Fact]
    public void NoMonitorStored_UsesPrimary()
    {
        var (m, x, y) = Placement.Resolve(At(null, 50, 60), 100, 100, Both);
        Assert.Same(Main, m);
        Assert.Equal((50, 60), (x, y));
    }

    [Fact]
    public void MissingMonitor_FallsBackToPrimary_FullyVisible()
    {
        // Rahmen lag rechts unten auf einem großen, jetzt abgesteckten Monitor
        var (m, x, y) = Placement.Resolve(At(@"\\.\DISPLAY9", 3000, 2000), 400, 300, new[] { Second, Main with { Bounds = new PixelRect(0, 0, 1920, 1080) } });
        Assert.True(m.IsPrimary);
        Assert.Equal(1920 - 400, x);
        Assert.Equal(1080 - 300, y);
    }

    [Fact]
    public void MissingMonitor_DoesNotChangeStoredConfig()
    {
        var frame = At(@"\\.\DISPLAY9", 3000, 2000);
        Placement.Resolve(frame, 400, 300, Both);
        Assert.Equal(@"\\.\DISPLAY9", frame.Monitor);
        Assert.Equal((3000, 2000), (frame.X, frame.Y));
    }

    [Fact]
    public void NoPrimaryFlag_UsesFirstMonitor()
    {
        var m = Placement.ResolveMonitor("unknown", new[] { Second });
        Assert.Same(Second, m);
    }

    [Theory]
    [InlineData(-50, -50, 0, 0)]          // links/oben herausgeschoben
    [InlineData(3400, 1400, 3040, 1140)]  // rechts/unten herausgeschoben (Auflösung verkleinert)
    public void Offscreen_IsPulledBackOntoMonitor(int storedX, int storedY, int expectedX, int expectedY)
    {
        var (_, x, y) = Placement.Resolve(At(@"\\.\DISPLAY6", storedX, storedY), 400, 300, Both);
        Assert.Equal((expectedX, expectedY), (x, y));
    }

    [Fact]
    public void FrameLargerThanMonitor_AlignsTopLeft()
    {
        var (_, x, y) = Placement.Resolve(At(@"\\.\DISPLAY7", 500, 500), 3000, 2000, Both);
        Assert.Equal((-1756, -1080), (x, y));
    }

    [Fact]
    public void ToStored_UsesMonitorUnderCenter()
    {
        // Rahmen ragt über die Grenze, Mittelpunkt liegt auf dem zweiten Monitor
        var (name, x, y) = Placement.ToStored(-300, -400, 400, 300, Both);
        Assert.Equal(@"\\.\DISPLAY7", name);
        Assert.Equal(-300 + 1756, x);
        Assert.Equal(-400 + 1080, y);
    }

    [Fact]
    public void ToStored_CenterOutsideAllMonitors_UsesNearest()
    {
        // Lücke unter dem zweiten Monitor (links vom Hauptmonitor, y > 0): Mittelpunkt (-100, 500)
        // liegt 100 px vom Hauptmonitor und 500 px vom zweiten Monitor entfernt
        var (name, _, _) = Placement.ToStored(-200, 400, 200, 200, Both);
        Assert.Equal(@"\\.\DISPLAY6", name);
        (name, _, _) = Placement.ToStored(-1700, 10, 100, 100, Both);
        Assert.Equal(@"\\.\DISPLAY7", name);
    }

    [Fact]
    public void ToStored_ThenResolve_GivesSamePosition()
    {
        foreach (var (x, y) in new[] { (100, 100), (-1500, -900), (3000, 1100) })
        {
            var (name, sx, sy) = Placement.ToStored(x, y, 300, 200, Both);
            var (_, rx, ry) = Placement.Resolve(At(name, sx, sy), 300, 200, Both);
            Assert.Equal((x, y), (rx, ry));
        }
    }

    [Fact]
    public void Centered_PlacesFrameInMiddle()
    {
        Assert.Equal((1520, 570), Placement.Centered(Main, 400, 300));
        Assert.Equal((0, 0), Placement.Centered(Main, 5000, 3000));
    }
}
