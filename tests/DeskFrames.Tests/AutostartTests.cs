using Microsoft.Win32;

namespace DeskFrames.Tests;

/// <summary>Arbeitet mit einem eigenen Testschlüssel unter HKCU – der echte Autostart bleibt unberührt.</summary>
public sealed class AutostartTests : IDisposable
{
    private readonly string _key = @"Software\DeskFramesTests\" + Guid.NewGuid().ToString("N");
    private const string Exe = @"C:\Program Files\Desk Frames\DeskFrames.exe";

    public void Dispose()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(_key, throwOnMissingSubKey: false);
            // Leeren Elternschlüssel ebenfalls entfernen, damit keine Reste in der Registry bleiben
            using (var parent = Registry.CurrentUser.OpenSubKey(@"Software\DeskFramesTests"))
                if (parent is { SubKeyCount: 0, ValueCount: 0 }) { parent.Close(); Registry.CurrentUser.DeleteSubKey(@"Software\DeskFramesTests", false); }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.IO.IOException) { }
    }

    private string? RawValue()
    {
        using var key = Registry.CurrentUser.OpenSubKey(_key);
        return key?.GetValue(Autostart.ValueName) as string;
    }

    [Fact]
    public void Off_WhenNothingRegistered()
    {
        Assert.False(new Autostart(Exe, _key).IsEnabled);
    }

    [Fact]
    public void Enable_WritesQuotedPath()
    {
        var a = new Autostart(Exe, _key);
        Assert.True(a.Set(true));
        Assert.True(a.IsEnabled);
        Assert.Equal("\"" + Exe + "\"", RawValue());
    }

    [Fact]
    public void Disable_RemovesValue()
    {
        var a = new Autostart(Exe, _key);
        a.Set(true);
        Assert.True(a.Set(false));
        Assert.False(a.IsEnabled);
        Assert.Null(RawValue());
    }

    [Fact]
    public void Disable_WhenNotRegistered_IsFine()
    {
        Assert.True(new Autostart(Exe, _key).Set(false));
    }

    [Fact]
    public void EntryForOtherCopy_CountsAsOff_AndIsNotChangedByReading()
    {
        new Autostart(@"D:\Old\DeskFrames.exe", _key).Set(true);
        var current = new Autostart(Exe, _key);

        Assert.False(current.IsEnabled);
        Assert.Equal("\"D:\\Old\\DeskFrames.exe\"", RawValue());

        current.Set(true);   // Einschalten stellt auf diese Kopie um
        Assert.True(current.IsEnabled);
        Assert.Equal("\"" + Exe + "\"", RawValue());
    }

    [Fact]
    public void PathComparison_IgnoresCase()
    {
        new Autostart(Exe.ToUpperInvariant(), _key).Set(true);
        Assert.True(new Autostart(Exe, _key).IsEnabled);
    }
}
