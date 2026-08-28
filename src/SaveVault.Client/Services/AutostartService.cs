using Microsoft.Win32;

namespace SaveVault.Client.Services;

/// <summary>
/// Kapselt den Windows-Autostart über den Benutzer-Run-Key
/// <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c> (Wertname <c>SaveVault</c>).
/// Bewusst nur <b>HKCU</b> – kein <c>HKLM</c>, keine Admin-Rechte. Der geschriebene Pfad
/// stammt ausschließlich aus <see cref="Environment.ProcessPath"/> (dem echten Apphost-
/// <c>.exe</c>), nie aus Nutzereingabe; er wird stets in Anführungszeichen geschrieben.
/// Alle Registry-Zugriffe sind gekapselt: schlägt einer fehl, wird das ruhig behandelt –
/// nie eine unbehandelte Ausnahme, die App läuft weiter.
/// </summary>
public static class AutostartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>Wertname unter dem Run-Key (identifiziert unseren Eintrag).</summary>
    private const string ValueName = "SaveVault";

    /// <summary>
    /// <c>true</c>, wenn der Run-Key-Wert existiert und auf den aktuellen exe-Pfad zeigt
    /// (Anführungszeichen werden dabei toleriert). Bei fehlendem exe-Pfad oder einem
    /// Registry-Fehler wird <c>false</c> gemeldet.
    /// </summary>
    public static bool IsEnabled()
    {
        var exePath = CurrentExePath();
        if (string.IsNullOrWhiteSpace(exePath))
            return false;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            if (key?.GetValue(ValueName) is not string stored)
                return false;

            return PathsEqual(Unquote(stored), exePath);
        }
        catch
        {
            // Lesefehler (z. B. Rechte/Policy) → Autostart gilt als nicht gesetzt.
            return false;
        }
    }

    /// <summary>
    /// Gleicht den Run-Key an den gewünschten Zustand an: <paramref name="enabled"/>=<c>true</c>
    /// schreibt (bzw. aktualisiert) den Wert auf den quotierten aktuellen exe-Pfad – deckt so
    /// auch eine verschobene exe ab; <c>false</c> entfernt den Eintrag (idempotent, kein Fehler,
    /// wenn er nicht existiert). Fehlt der exe-Pfad oder scheitert ein Zugriff, kehrt die
    /// Methode ruhig zurück, ohne zu werfen.
    /// </summary>
    public static void Apply(bool enabled)
    {
        if (enabled)
            Enable();
        else
            Disable();
    }

    /// <summary>Trägt den quotierten exe-Pfad in den Run-Key ein (idempotent).</summary>
    public static void Enable()
    {
        var exePath = CurrentExePath();
        if (string.IsNullOrWhiteSpace(exePath))
            return; // Ohne echten exe-Pfad (z. B. reiner DLL-Start) nichts tun.

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            key?.SetValue(ValueName, "\"" + exePath + "\"", RegistryValueKind.String);
        }
        catch
        {
            // Schreibfehler still behandeln – der Rest der App läuft weiter.
        }
    }

    /// <summary>Entfernt den Run-Key-Wert, falls vorhanden (idempotent).</summary>
    public static void Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch
        {
            // Löschfehler still behandeln.
        }
    }

    /// <summary>Der echte Apphost-<c>.exe</c>-Pfad (nicht die DLL); ggf. <c>null</c>/leer.</summary>
    private static string? CurrentExePath() => Environment.ProcessPath;

    /// <summary>Entfernt umschließende Anführungszeichen eines gespeicherten Wertes.</summary>
    private static string Unquote(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
            trimmed = trimmed[1..^1];
        return trimmed;
    }

    /// <summary>Vergleicht zwei Pfade tolerant (Trim + case-insensitiv, wie auf Windows üblich).</summary>
    private static bool PathsEqual(string a, string b)
        => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
}
