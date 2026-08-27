using System.IO;
using System.Text.Json;
using SaveVault.Core.Serialization;

namespace SaveVault.Client.Services;

/// <summary>
/// Kleiner Helfer für das lokale Lesen/Schreiben von JSON-Dateien:
///   * <b>atomar</b> geschrieben (in eine Temp-Datei, dann ersetzen/umbenennen) – ein
///     Absturz mitten im Schreiben lässt die alte Datei intakt, nie eine halbe Datei.
///   * <b>tolerant</b> geladen: fehlende oder kaputte Dateien liefern <c>null</c> statt
///     zu werfen (der Aufrufer entscheidet über den Default).
/// Nutzt die gemeinsamen <see cref="SaveVaultJson"/>-Optionen (camelCase, Enums als String).
/// </summary>
internal static class JsonFileStore
{
    /// <summary>Schreibt <paramref name="value"/> atomar nach <paramref name="path"/>.</summary>
    public static void Write<T>(string path, T value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(value, SaveVaultJson.Options);
        var tmp = path + ".tmp-" + Guid.NewGuid().ToString("N");

        File.WriteAllText(tmp, json);
        try
        {
            if (File.Exists(path))
                File.Replace(tmp, path, destinationBackupFileName: null);
            else
                File.Move(tmp, path);
        }
        catch
        {
            // Temp-Datei nicht liegen lassen, den eigentlichen Fehler aber weiterreichen.
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best effort */ }
            throw;
        }
    }

    /// <summary>Liest <typeparamref name="T"/> aus <paramref name="path"/> oder <c>null</c>.</summary>
    public static T? Read<T>(string path) where T : class
    {
        try
        {
            if (!File.Exists(path))
                return null;
            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
                return null;
            return JsonSerializer.Deserialize<T>(json, SaveVaultJson.Options);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Kaputte/gesperrte Datei → als „nicht vorhanden" behandeln, kein Absturz.
            return null;
        }
    }
}
