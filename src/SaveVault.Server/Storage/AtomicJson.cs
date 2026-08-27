using System.Text.Json;

namespace SaveVault.Server.Storage;

/// <summary>
/// Schreibt/liest JSON-Dateien ATOMAR: geschrieben wird erst in eine Temp-Datei im selben
/// Verzeichnis, die dann per Rename an ihren Platz gezogen wird. So sieht ein Leser nie eine
/// halb geschriebene Datei, und ein Absturz mitten im Schreiben lässt die alte Fassung intakt.
/// </summary>
public static class AtomicJson
{
    /// <summary>Liest und deserialisiert; gibt <paramref name="fallback"/> bei fehlender/kaputter Datei.</summary>
    public static T ReadOrDefault<T>(string path, JsonSerializerOptions options, Func<T> fallback)
    {
        try
        {
            if (!File.Exists(path))
                return fallback();

            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
                return fallback();

            var value = JsonSerializer.Deserialize<T>(json, options);
            return value ?? fallback();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Kaputte/gesperrte Datei soll den Server nicht crashen – mit Default weiterarbeiten.
            return fallback();
        }
    }

    /// <summary>Serialisiert und schreibt atomar (Temp-Datei + Rename mit Overwrite).</summary>
    public static void Write<T>(string path, T value, JsonSerializerOptions options)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var tmp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        var json = JsonSerializer.Serialize(value, options);

        File.WriteAllText(tmp, json);
        try
        {
            // Atomarer Ersatz: unter Windows und Linux ein Rename, kein Teil-Zustand sichtbar.
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            TryDelete(tmp);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best effort – eine verwaiste Temp-Datei ist kein Folgefehler.
        }
    }
}
