using System.IO;
using DeskFrames.Model;

namespace DeskFrames.Tests;

public class ConfigTests
{
    private static FrameConfig Sample(string image, double aspect = 1.5) => new()
    {
        ImagePath = image, AspectRatio = aspect, Monitor = @"\\.\DISPLAY2",
        X = -40, Y = 120, Width = 360.5, BorderColor = "#A0B0C0", BorderWidth = 12,
    };

    [Fact]
    public void SaveThenLoad_GivesSameFrames()
    {
        using var dir = new TempDir();
        string path = dir.File("frames.json");
        var config = new AppConfig { Frames = { Sample(@"C:\Bilder\urlaub.jpg"), Sample(@"D:\Fotos\katze äöü.png", 0.75) } };

        Assert.True(config.Save(path));
        var loaded = AppConfig.Load(path);

        Assert.Equal(2, loaded.Frames.Count);
        for (int i = 0; i < 2; i++)
        {
            var a = config.Frames[i];
            var b = loaded.Frames[i];
            Assert.Equal(a.Id, b.Id);
            Assert.Equal(a.ImagePath, b.ImagePath);
            Assert.Equal(a.AspectRatio, b.AspectRatio);
            Assert.Equal(a.Monitor, b.Monitor);
            Assert.Equal(a.X, b.X);
            Assert.Equal(a.Y, b.Y);
            Assert.Equal(a.Width, b.Width);
            Assert.Equal(a.BorderColor, b.BorderColor);
            Assert.Equal(a.BorderWidth, b.BorderWidth);
        }
    }

    [Fact]
    public void Save_CreatesFolder_AndLeavesNoTempFile()
    {
        using var dir = new TempDir();
        string path = Path.Combine(dir.Path, "sub", "frames.json");
        Assert.True(new AppConfig { Frames = { Sample("a.jpg") } }.Save(path));
        Assert.True(File.Exists(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void Save_OverwritesExistingFile()
    {
        using var dir = new TempDir();
        string path = dir.File("frames.json");
        new AppConfig { Frames = { Sample("a.jpg"), Sample("b.jpg") } }.Save(path);
        new AppConfig { Frames = { Sample("c.jpg") } }.Save(path);
        var loaded = AppConfig.Load(path);
        Assert.Equal("c.jpg", Assert.Single(loaded.Frames).ImagePath);
    }

    [Fact]
    public void Save_DoesNotWriteComputedHeight()
    {
        using var dir = new TempDir();
        string path = dir.File("frames.json");
        new AppConfig { Frames = { Sample("a.jpg") } }.Save(path);
        Assert.DoesNotContain("\"Height\"", File.ReadAllText(path));
    }

    [Fact]
    public void MissingFile_GivesEmptyConfig()
    {
        using var dir = new TempDir();
        Assert.Empty(AppConfig.Load(dir.File("missing.json")).Frames);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \r\n")]
    [InlineData("null")]
    public void EmptyFile_GivesEmptyConfig(string content)
    {
        using var dir = new TempDir();
        string path = dir.File("frames.json");
        File.WriteAllText(path, content);
        Assert.Empty(AppConfig.Load(path).Frames);
    }

    [Theory]
    [InlineData("{ \"Frames\": [ { \"ImagePath\": ")]
    [InlineData("das ist kein JSON")]
    [InlineData("{ \"Frames\": \"falsch\" }")]
    [InlineData("{ \"Frames\": [ { \"Width\": \"breit\" } ] }")]
    public void DamagedFile_GivesEmptyConfig_AndKeepsBackup(string content)
    {
        using var dir = new TempDir();
        string path = dir.File("frames.json");
        File.WriteAllText(path, content);

        var config = AppConfig.Load(path);

        Assert.Empty(config.Frames);
        Assert.Equal(content, File.ReadAllText(path + ".bad"));
    }

    [Fact]
    public void LockedFile_GivesEmptyConfig_WithoutException()
    {
        using var dir = new TempDir();
        string path = dir.File("frames.json");
        new AppConfig { Frames = { Sample("a.jpg") } }.Save(path);
        using var lockStream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.Empty(AppConfig.Load(path).Frames);
    }

    [Fact]
    public void UnknownFields_AreIgnored_CommentsAndTrailingCommasAllowed()
    {
        using var dir = new TempDir();
        string path = dir.File("frames.json");
        File.WriteAllText(path, """
            {
              // von Hand bearbeitet
              "Version": 7,
              "SomethingNew": { "x": 1 },
              "Frames": [
                { "Id": "abc", "ImagePath": "a.jpg", "AspectRatio": 2, "Width": 400, "Shadow": true, },
              ],
            }
            """);

        var f = Assert.Single(AppConfig.Load(path).Frames);
        Assert.Equal("abc", f.Id);
        Assert.Equal("a.jpg", f.ImagePath);
        Assert.Equal(2, f.AspectRatio);
        Assert.Equal(400, f.Width);
    }

    [Fact]
    public void MissingFields_GetDefaults()
    {
        using var dir = new TempDir();
        string path = dir.File("frames.json");
        File.WriteAllText(path, """{ "Frames": [ { "ImagePath": "a.jpg" } ] }""");

        var f = Assert.Single(AppConfig.Load(path).Frames);
        Assert.False(string.IsNullOrEmpty(f.Id));
        Assert.Equal(1.0, f.AspectRatio);
        Assert.Equal(FrameConfig.DefaultWidth, f.Width);
        Assert.Equal("#1C1C1C", f.BorderColor);
        Assert.Equal(6, f.BorderWidth);
        Assert.Null(f.Monitor);
    }

    [Fact]
    public void InvalidValues_AreRepaired()
    {
        using var dir = new TempDir();
        string path = dir.File("frames.json");
        File.WriteAllText(path, """
            { "Frames": [
              null,
              { "Id": "", "ImagePath": null, "AspectRatio": -3, "Width": 5, "BorderColor": "red", "BorderWidth": 99, "Monitor": " " },
              { "AspectRatio": 1000, "Width": 9999999, "BorderColor": "#abcdef", "BorderWidth": -4 }
            ] }
            """);

        var frames = AppConfig.Load(path).Frames;
        Assert.Equal(2, frames.Count);

        var a = frames[0];
        Assert.False(string.IsNullOrEmpty(a.Id));
        Assert.Equal("", a.ImagePath);
        Assert.Equal(1.0, a.AspectRatio);
        Assert.Equal(FrameConfig.MinShortSide, a.Width);
        Assert.Equal(FrameConfig.DefaultBorderColor, a.BorderColor);
        Assert.Equal(FrameConfig.MaxBorderWidth, a.BorderWidth);
        Assert.Null(a.Monitor);

        var b = frames[1];
        Assert.Equal(FrameConfig.MaxAspect, b.AspectRatio);
        Assert.Equal(FrameConfig.MaxSide, b.Width);
        Assert.Equal("#ABCDEF", b.BorderColor);
        Assert.Equal(0, b.BorderWidth);
    }

    [Theory]
    [InlineData(2.0, 100, 120)]    // Querformat: Höhe = 50 → Breite muss 120 sein, damit die Höhe 60 erreicht
    [InlineData(0.5, 30, 60)]      // Hochformat: Breite ist die kürzere Seite
    [InlineData(1.0, 59.9, 60)]
    public void TooSmallFrames_AreEnlargedToMinimumShortSide(double aspect, double width, double expected)
    {
        var f = new FrameConfig { AspectRatio = aspect, Width = width };
        f.Sanitize();
        Assert.Equal(expected, f.Width, 6);
        Assert.True(Math.Min(f.Width, f.Height) >= FrameConfig.MinShortSide - 1e-9);
    }

    [Fact]
    public void DuplicateIds_GetNewIds()
    {
        using var dir = new TempDir();
        string path = dir.File("frames.json");
        File.WriteAllText(path, """{ "Frames": [ { "Id": "x" }, { "Id": "x" } ] }""");
        var frames = AppConfig.Load(path).Frames;
        Assert.Equal("x", frames[0].Id);
        Assert.NotEqual("x", frames[1].Id);
    }

    [Fact]
    public void FileWrittenByHand_WithLowercaseNames_IsRead()
    {
        using var dir = new TempDir();
        string path = dir.File("frames.json");
        File.WriteAllText(path, """{ "frames": [ { "imagePath": "a.jpg", "width": 200 } ] }""");
        var f = Assert.Single(AppConfig.Load(path).Frames);
        Assert.Equal("a.jpg", f.ImagePath);
        Assert.Equal(200, f.Width);
    }
}
