using Microsoft.Win32;

namespace DeskFrames;

/// <summary>
/// „Start with Windows“ über HKCU\Software\Microsoft\Windows\CurrentVersion\Run – ohne Admin-Rechte, nur für
/// den angemeldeten Benutzer. Die Registry ist die einzige Quelle der Wahrheit (nicht frames.json).
/// </summary>
internal sealed class Autostart
{
    public const string DefaultKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ValueName = "DeskFrames";

    private readonly string _keyPath;
    private readonly string _exePath;

    /// <param name="exePath">Programmdatei; Standard ist die laufende EXE.</param>
    /// <param name="keyPath">Nur für Tests: anderer Schlüssel unter HKCU.</param>
    public Autostart(string? exePath = null, string keyPath = DefaultKeyPath)
    {
        _exePath = exePath ?? Environment.ProcessPath ?? throw new InvalidOperationException("Program path unknown.");
        _keyPath = keyPath;
    }

    /// <summary>Befehl, der eingetragen wird: Pfad in Anführungszeichen (Leerzeichen im Pfad).</summary>
    public string Command => $"\"{_exePath}\"";

    /// <summary>
    /// An, wenn der Eintrag auf genau diese EXE zeigt. Zeigt er auf eine andere (Programm verschoben, zweite Kopie),
    /// gilt er als aus – Einschalten stellt ihn dann auf diese EXE um. Nie stillschweigend umbiegen.
    /// </summary>
    public bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(_keyPath);
                return key?.GetValue(ValueName) is string s && string.Equals(s.Trim(), Command, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
            {
                ErrorLog.Write($"Autostart not readable: {ex.Message}");
                return false;
            }
        }
    }

    /// <summary>Ein- oder ausschalten. Gibt false zurück, wenn die Registry nicht beschreibbar war.</summary>
    public bool Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(_keyPath, writable: true);
            if (enabled) key.SetValue(ValueName, Command, RegistryValueKind.String);
            else key.DeleteValue(ValueName, throwOnMissingValue: false);
            return true;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
        {
            ErrorLog.Write($"Autostart not changed: {ex.Message}");
            return false;
        }
    }

}
