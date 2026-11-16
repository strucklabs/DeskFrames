using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DeskFrames.Imaging;

namespace DeskFrames.Tests;

public class ImageLoaderTests
{
    /// <summary>Testbild: linke Hälfte rot, rechte Hälfte blau.</summary>
    private static BitmapSource RedBlue(int width, int height)
    {
        int stride = width * 4;
        var pixels = new byte[stride * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int i = y * stride + x * 4;
                bool red = x < width / 2;
                pixels[i] = (byte)(red ? 0 : 255);       // B
                pixels[i + 2] = (byte)(red ? 255 : 0);   // R
                pixels[i + 3] = 255;
            }
        return BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr32, null, pixels, stride);
    }

    private static string SaveJpeg(TempDir dir, int width, int height, ushort? orientation, string name = "photo.jpg")
    {
        string path = dir.File(name);
        var meta = new BitmapMetadata("jpg");
        if (orientation is { } o) meta.SetQuery("/app1/ifd/{ushort=274}", o);
        var encoder = new JpegBitmapEncoder { QualityLevel = 95 };
        encoder.Frames.Add(BitmapFrame.Create(RedBlue(width, height), null, meta, null));
        using var fs = File.Create(path);
        encoder.Save(fs);
        return path;
    }

    private static string SavePng(TempDir dir, int width, int height, string name = "image.png")
    {
        string path = dir.File(name);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(RedBlue(width, height)));
        using var fs = File.Create(path);
        encoder.Save(fs);
        return path;
    }

    /// <summary>Farbe eines Pixels: true = rot, false = blau.</summary>
    private static bool IsRed(BitmapSource bmp, int x, int y)
    {
        var converted = new FormatConvertedBitmap(bmp, PixelFormats.Bgr32, null, 0);
        var px = new byte[4];
        converted.CopyPixels(new System.Windows.Int32Rect(x, y, 1, 1), px, 4, 0);
        return px[2] > 128 && px[0] < 128;
    }

    [Fact]
    public void Png_AspectFromPixelSize()
    {
        using var dir = new TempDir();
        var img = ImageLoader.Load(SavePng(dir, 300, 200), 4096);
        Assert.Equal(ImageStatus.Ok, img.Status);
        Assert.Equal(1.5, img.AspectRatio, 6);
        Assert.NotNull(img.Bitmap);
        Assert.True(img.Bitmap!.IsFrozen);
    }

    [Theory]
    [InlineData((ushort)1, 1.5)]
    [InlineData((ushort)3, 1.5)]
    [InlineData((ushort)6, 2.0 / 3)]   // Handy hochkant: gespeichert quer, angezeigt hochkant
    [InlineData((ushort)8, 2.0 / 3)]
    [InlineData((ushort)5, 2.0 / 3)]
    [InlineData((ushort)7, 2.0 / 3)]
    public void Jpeg_ExifOrientation_DeterminesAspect(ushort orientation, double expected)
    {
        using var dir = new TempDir();
        var img = ImageLoader.Load(SaveJpeg(dir, 300, 200, orientation), 4096);
        Assert.Equal(ImageStatus.Ok, img.Status);
        Assert.Equal(expected, img.AspectRatio, 6);
        Assert.Equal(expected, (double)img.Bitmap!.PixelWidth / img.Bitmap.PixelHeight, 2);
    }

    [Fact]
    public void Jpeg_WithoutOrientation_IsNormal()
    {
        using var dir = new TempDir();
        var img = ImageLoader.Load(SaveJpeg(dir, 300, 200, null), 4096);
        Assert.Equal(1.5, img.AspectRatio, 6);
    }

    [Fact]
    public void Orientation6_RotatesClockwise()
    {
        // Linke Hälfte (rot) landet nach 90° im Uhrzeigersinn oben
        using var dir = new TempDir();
        var bmp = ImageLoader.Load(SaveJpeg(dir, 80, 40, 6), 4096).Bitmap!;
        Assert.Equal((40, 80), (bmp.PixelWidth, bmp.PixelHeight));
        Assert.True(IsRed(bmp, 20, 10));
        Assert.False(IsRed(bmp, 20, 70));
    }

    [Fact]
    public void Orientation8_RotatesCounterClockwise()
    {
        using var dir = new TempDir();
        var bmp = ImageLoader.Load(SaveJpeg(dir, 80, 40, 8), 4096).Bitmap!;
        Assert.False(IsRed(bmp, 20, 10));
        Assert.True(IsRed(bmp, 20, 70));
    }

    [Fact]
    public void Orientation2_MirrorsHorizontally()
    {
        using var dir = new TempDir();
        var bmp = ImageLoader.Load(SaveJpeg(dir, 80, 40, 2), 4096).Bitmap!;
        Assert.False(IsRed(bmp, 10, 20));
        Assert.True(IsRed(bmp, 70, 20));
    }

    [Fact]
    public void LargeImage_IsDecodedSmaller_AspectFromFullSize()
    {
        using var dir = new TempDir();
        var img = ImageLoader.Load(SavePng(dir, 2000, 1000), 500);
        Assert.Equal(2.0, img.AspectRatio, 6);
        Assert.Equal(500, img.Bitmap!.PixelWidth);
        Assert.Equal(250, img.Bitmap.PixelHeight);
        Assert.Equal(2000, img.FullLongSide);
        Assert.Equal(500, img.DecodedLongSide);
    }

    [Fact]
    public void LargePortraitImage_IsLimitedByHeight()
    {
        using var dir = new TempDir();
        var img = ImageLoader.Load(SavePng(dir, 600, 1800), 900);
        Assert.Equal(900, img.Bitmap!.PixelHeight);
        Assert.Equal(300, img.Bitmap.PixelWidth);
    }

    [Fact]
    public void File_IsNotLocked_AndNotChanged()
    {
        using var dir = new TempDir();
        string path = SavePng(dir, 300, 200);
        byte[] before = File.ReadAllBytes(path);
        var stamp = File.GetLastWriteTimeUtc(path);

        var img = ImageLoader.Load(path, 4096);

        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(path));
        string moved = dir.File("renamed.png");
        File.Move(path, moved);          // würde bei gesperrter Datei scheitern
        File.Delete(moved);
        Assert.Equal(ImageStatus.Ok, img.Status);
    }

    [Fact]
    public void MissingFile_GivesMissing()
    {
        using var dir = new TempDir();
        Assert.Equal(ImageStatus.Missing, ImageLoader.Load(dir.File("weg.jpg"), 1024).Status);
        Assert.Equal(ImageStatus.Missing, ImageLoader.Load(Path.Combine(dir.Path, "kein-ordner", "weg.jpg"), 1024).Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void NoPath_GivesNoPath(string? path) =>
        Assert.Equal(ImageStatus.NoPath, ImageLoader.Load(path, 1024).Status);

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 1, 2, 3, 4, 5 })]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10 })]   // JPEG-Anfang, dann abgeschnitten
    public void DamagedFile_GivesUnreadable_WithoutException(byte[] content)
    {
        using var dir = new TempDir();
        string path = dir.File("kaputt.jpg");
        File.WriteAllBytes(path, content);
        Assert.Equal(ImageStatus.Unreadable, ImageLoader.Load(path, 1024).Status);
    }
}
