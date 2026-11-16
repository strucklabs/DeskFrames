using System.IO;

namespace DeskFrames;

/// <summary>
/// Schreibt unerwartete Fehler nach %APPDATA%\DeskFrames\error.log, statt sie stillschweigend zu verlieren.
/// Die Datei wird bei 1 MB geleert, damit sie nicht unbegrenzt wächst.
/// </summary>
internal static class ErrorLog
{
    private static readonly object Lock = new();

    /// <summary>Setzbar nur für die Tests – sie prüfen absichtlich kaputte Dateien und sollen das echte Protokoll nicht füllen.</summary>
    public static string FilePath { get; internal set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeskFrames", "error.log");

    public static void Write(Exception ex) => Write(ex.ToString());

    public static void Write(string message)
    {
        try
        {
            lock (Lock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 1024 * 1024) File.Delete(FilePath);
                File.AppendAllText(FilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
            }
        }
        catch (IOException) { /* Protokollieren darf das Programm nie zum Absturz bringen */ }
        catch (UnauthorizedAccessException) { }
    }
}
