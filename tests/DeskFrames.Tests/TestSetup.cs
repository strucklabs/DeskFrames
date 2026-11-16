using System.IO;
using System.Runtime.CompilerServices;

namespace DeskFrames.Tests;

internal static class TestSetup
{
    /// <summary>
    /// Läuft vor allen Tests: Fehler, die Tests absichtlich auslösen (kaputte frames.json …), landen in einem
    /// temporären Protokoll statt in %APPDATA%\DeskFrames\error.log des Nutzers.
    /// </summary>
    [ModuleInitializer]
    internal static void RedirectErrorLog() =>
        ErrorLog.FilePath = Path.Combine(Path.GetTempPath(), "DeskFramesTests_error.log");
}

/// <summary>Eigener temporärer Ordner pro Test, wird danach gelöscht.</summary>
public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "DeskFramesTests_" + Guid.NewGuid().ToString("N"));
    public TempDir() => Directory.CreateDirectory(Path);
    public string File(string name) => System.IO.Path.Combine(Path, name);
    public void Dispose() { try { Directory.Delete(Path, recursive: true); } catch (IOException) { } }
}
