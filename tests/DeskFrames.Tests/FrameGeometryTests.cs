using DeskFrames.Model;

namespace DeskFrames.Tests;

public class FrameGeometryTests
{
    // Bild 300 × 200 (3:2) mit 6 px Rahmen → außen 312 × 212
    private const double Aspect = 1.5;
    private const double Border = 6;
    private static readonly PixelRect Start = new(1000, 500, 312, 212);
    private const double Min = 90, Max = 3000;

    private static double InnerAspect(PixelRect r) => (r.Width - 2 * Border) / (r.Height - 2 * Border);

    private static PixelRect Resize(Grip g, double dx, double dy, double min = Min, double max = Max) =>
        FrameGeometry.Resize(Start, g, dx, dy, Aspect, Border, min, max);

    // ---------- Griffe ----------

    [Theory]
    [InlineData(150, 100, Grip.Move)]
    [InlineData(2, 100, Grip.Left)]
    [InlineData(298, 100, Grip.Right)]
    [InlineData(150, 3, Grip.Top)]
    [InlineData(150, 197, Grip.Bottom)]
    [InlineData(1, 1, Grip.TopLeft)]
    [InlineData(299, 0, Grip.TopRight)]
    [InlineData(0, 199, Grip.BottomLeft)]
    [InlineData(299, 199, Grip.BottomRight)]
    [InlineData(15, 3, Grip.TopLeft)]       // Ecke reicht doppelt so weit am Rand entlang
    [InlineData(25, 3, Grip.Top)]
    [InlineData(15, 15, Grip.Move)]         // innen nahe der Ecke, aber nicht am Rand
    public void HitTest_FindsGrip(double x, double y, Grip expected) =>
        Assert.Equal(expected, FrameGeometry.HitTest(x, y, 300, 200));

    [Fact]
    public void HitTest_SmallFrame_KeepsCenterForMoving()
    {
        Assert.Equal(Grip.Move, FrameGeometry.HitTest(30, 30, 60, 60));
        Assert.Equal(Grip.Left, FrameGeometry.HitTest(1, 30, 60, 60));
    }

    // ---------- Skalieren ----------

    [Theory]
    [InlineData(Grip.Left, -100, 0)]
    [InlineData(Grip.Right, 100, 0)]
    [InlineData(Grip.Top, 0, -60)]
    [InlineData(Grip.Bottom, 0, 60)]
    [InlineData(Grip.TopLeft, -100, -10)]
    [InlineData(Grip.TopRight, 100, -10)]
    [InlineData(Grip.BottomLeft, -10, 60)]
    [InlineData(Grip.BottomRight, 37, 81)]
    [InlineData(Grip.Right, -150, 0)]
    [InlineData(Grip.TopLeft, 50, 40)]
    public void Resize_KeepsAspectRatio(Grip grip, double dx, double dy)
    {
        var r = Resize(grip, dx, dy);
        Assert.NotEqual(Start, r);
        // Höhe ist auf ganze Pixel gerundet: höchstens ein halbes Pixel Abweichung
        double innerW = r.Width - 2 * Border, innerH = r.Height - 2 * Border;
        Assert.InRange(innerW / Aspect - innerH, -0.5, 0.5);
        Assert.InRange(InnerAspect(r), Aspect - 0.01, Aspect + 0.01);
    }

    [Fact]
    public void Right_GrowsWidth_LeftEdgeAndVerticalCenterStay()
    {
        var r = Resize(Grip.Right, 99, 40);   // vertikale Mausbewegung zählt am rechten Rand nicht
        Assert.Equal(Start.Left, r.Left);
        Assert.Equal(399 + 12, r.Width);
        Assert.Equal(266 + 12, r.Height);
        Assert.Equal(Start.Top + Start.Height / 2.0, r.Top + r.Height / 2.0, 0);
    }

    [Fact]
    public void Left_RightEdgeStaysExactly()
    {
        var r = Resize(Grip.Left, -150, 0);
        Assert.Equal(Start.Right, r.Right);
        Assert.Equal(450 + 12, r.Width);
    }

    [Fact]
    public void Top_BottomEdgeStays_HorizontalCenterStays()
    {
        var r = Resize(Grip.Top, 33, -100);
        Assert.Equal(Start.Bottom, r.Bottom);
        Assert.Equal(300 + 12, r.Height);
        Assert.Equal(450 + 12, r.Width);
        Assert.Equal(Start.Left + Start.Width / 2.0, r.Left + r.Width / 2.0, 0);
    }

    [Fact]
    public void Bottom_TopEdgeStays()
    {
        var r = Resize(Grip.Bottom, 0, 50);
        Assert.Equal(Start.Top, r.Top);
        Assert.Equal(250 + 12, r.Height);
    }

    [Theory]
    [InlineData(Grip.TopLeft)]
    [InlineData(Grip.TopRight)]
    [InlineData(Grip.BottomLeft)]
    [InlineData(Grip.BottomRight)]
    public void Corner_OppositeCornerStays(Grip grip)
    {
        double sx = grip is Grip.TopLeft or Grip.BottomLeft ? -1 : 1;
        double sy = grip is Grip.TopLeft or Grip.TopRight ? -1 : 1;
        var r = Resize(grip, 80 * sx, 20 * sy);
        if (sx < 0) Assert.Equal(Start.Right, r.Right); else Assert.Equal(Start.Left, r.Left);
        if (sy < 0) Assert.Equal(Start.Bottom, r.Bottom); else Assert.Equal(Start.Top, r.Top);
    }

    [Fact]
    public void Corner_LargerMovementWins()
    {
        // Waagerecht +30 → Breite 330; senkrecht +40 → Höhe 240 → Breite 360: die senkrechte Bewegung ist größer
        Assert.Equal(360 + 12, Resize(Grip.BottomRight, 30, 40).Width);
        // Waagerecht +90 → 390; senkrecht +40 → 360: jetzt gewinnt die waagerechte
        Assert.Equal(390 + 12, Resize(Grip.BottomRight, 90, 40).Width);
        // Verkleinern zählt genauso: −60 waagerecht schlägt +10 senkrecht (→ 315)
        Assert.Equal(240 + 12, Resize(Grip.BottomRight, -60, 10).Width);
    }

    [Fact]
    public void Resize_RespectsMinimum()
    {
        var r = Resize(Grip.BottomRight, -1000, -1000);
        Assert.Equal(Min + 12, r.Width);
        Assert.Equal(Start.Left, r.Left);
    }

    [Fact]
    public void Resize_RespectsMaximum()
    {
        var r = Resize(Grip.Left, -5000, 0, max: 600);
        Assert.Equal(600 + 12, r.Width);
        Assert.Equal(Start.Right, r.Right);
    }

    [Fact]
    public void Resize_MinimumWinsOverTooSmallMaximum()
    {
        var r = Resize(Grip.Right, 100, 0, min: 200, max: 150);
        Assert.Equal(200 + 12, r.Width);
    }

    [Fact]
    public void ManySmallSteps_NoDrift()
    {
        // Jede Mausbewegung rechnet vom Startrechteck aus: hin und zurück ergibt exakt den Anfang
        foreach (var grip in Enum.GetValues<Grip>())
        {
            for (int d = 1; d <= 400; d++) Resize(grip, d * 0.7, d * 0.3);
            for (int d = 400; d >= -40; d--) Resize(grip, d * 0.7, d * 0.3);
            Assert.Equal(Start, Resize(grip, 0, 0));
        }
    }

    [Fact]
    public void ManySizes_InnerAspectStaysWithinHalfPixel()
    {
        foreach (double aspect in new[] { 0.5625, 0.75, 1.0, 1.333333, 1.5, 1.777778, 2.39 })
        {
            var start = new PixelRect(0, 0, 212, (int)Math.Round(200 / aspect + 12));
            for (int dx = -100; dx <= 900; dx += 7)
            {
                var r = FrameGeometry.Resize(start, Grip.Right, dx, 0, aspect, Border, 60, 5000);
                double innerW = r.Width - 2 * Border, innerH = r.Height - 2 * Border;
                Assert.InRange(innerW / aspect - innerH, -0.5, 0.5);
            }
        }
    }

    [Fact]
    public void Move_ShiftsWithoutResizing()
    {
        var r = Resize(Grip.Move, -1300.4, 20.6);
        Assert.Equal(new PixelRect(-300, 521, 312, 212), r);
    }

    [Fact]
    public void WithoutBorder_SameRules()
    {
        var r = FrameGeometry.Resize(new PixelRect(0, 0, 300, 200), Grip.Right, 100, 0, 1.5, 0, 60, 5000);
        // Höhe 200 → 267, senkrechte Mitte (100) bleibt: Oberkante 100 − 133,5 → −34
        Assert.Equal(new PixelRect(0, -34, 400, 267), r);
    }

    // ---------- Bild wechseln ----------

    [Theory]
    [InlineData(0.75)]   // Querformat → Hochformat
    [InlineData(1.0)]
    [InlineData(2.0)]
    [InlineData(1.5)]
    public void ChangeAspect_KeepsAreaAndCenter(double newAspect)
    {
        var r = FrameGeometry.ChangeAspect(Start, Border, newAspect, 60, 5000);
        double innerW = r.Width - 2 * Border, innerH = r.Height - 2 * Border;

        Assert.InRange(innerW / innerH, newAspect * 0.99, newAspect * 1.01);
        Assert.InRange(innerW * innerH, 300 * 200 * 0.98, 300 * 200 * 1.02);
        Assert.InRange(r.Left + r.Width / 2.0 - (Start.Left + Start.Width / 2.0), -1, 1);
        Assert.InRange(r.Top + r.Height / 2.0 - (Start.Top + Start.Height / 2.0), -1, 1);
    }

    [Fact]
    public void ChangeAspect_RespectsLimits()
    {
        var r = FrameGeometry.ChangeAspect(Start, Border, 20, 60, 400);
        Assert.Equal(400 + 12, r.Width);
    }

    // ---------- Rahmenbreite ändern ----------

    [Theory]
    [InlineData(6, 20)]
    [InlineData(6, 0)]
    [InlineData(0, 40)]
    [InlineData(6, 6)]
    public void ChangeBorder_ImageStaysSameSizeAndPlace(double oldBorder, double newBorder)
    {
        var start = new PixelRect(1000, 500, (int)(300 + 2 * oldBorder), (int)(200 + 2 * oldBorder));
        var r = FrameGeometry.ChangeBorder(start, oldBorder, newBorder, 1.5);

        // Bildfläche gleich groß …
        Assert.Equal(300, r.Width - 2 * newBorder, 0);
        Assert.Equal(200, r.Height - 2 * newBorder, 0);
        // … und am selben Ort: linke obere Bildecke unverändert
        Assert.Equal(start.Left + oldBorder, r.Left + newBorder, 0);
        Assert.Equal(start.Top + oldBorder, r.Top + newBorder, 0);
    }

    [Fact]
    public void ChangeBorder_Back_GivesStartAgain()
    {
        var r = FrameGeometry.ChangeBorder(Start, Border, 17, Aspect);
        Assert.Equal(Start, FrameGeometry.ChangeBorder(r, 17, Border, Aspect));
    }

    [Fact]
    public void ChangeBorder_FractionalScale_StaysConsistentWithOuterSize()
    {
        // 125 %: 6 → 7,5 px, 10 → 12,5 px
        var start = new PixelRect(0, 0, (int)Math.Round(400 + 15.0), (int)Math.Round(400 / 1.5 + 15.0));
        var r = FrameGeometry.ChangeBorder(start, 7.5, 12.5, 1.5);
        Assert.Equal(FrameGeometry.OuterSize(400, 1.5, 12.5), (r.Width, r.Height));
    }

    [Fact]
    public void MaxInnerWidthOnMonitor_FitsBothDirections()
    {
        var monitor = new PixelRect(0, 0, 1920, 1080);
        Assert.Equal(1080 - 12, FrameGeometry.MaxInnerWidthOnMonitor(monitor, 1.0, 6), 6);   // Höhe begrenzt
        Assert.Equal(1920 - 12, FrameGeometry.MaxInnerWidthOnMonitor(monitor, 4.0, 6), 6);   // Breite begrenzt
    }
}
