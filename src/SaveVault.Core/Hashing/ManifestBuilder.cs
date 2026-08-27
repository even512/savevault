using SaveVault.Core.Models;

namespace SaveVault.Core.Hashing;

/// <summary>
/// Baut ein <see cref="FileManifest"/> aus einem Verzeichnis: sammelt rekursiv alle
/// Dateien, normalisiert die relativen Pfade auf '/', und hasht sie mit SHA-256.
/// Als Vorfilter kann ein altes Manifest übergeben werden – unveränderte Dateien
/// (gleiche Größe + gleiche Schreibzeit) werden dann NICHT neu gehasht.
/// Reine Datei-IO, keine Netz-/Prozess-Abhängigkeit.
/// </summary>
public sealed class ManifestBuilder
{
    /// <summary>
    /// Baut das Manifest für <paramref name="rootDirectory"/>. Existiert der Ordner
    /// nicht, wird ein leeres Manifest zurückgegeben (kein Absturz). Nicht lesbare
    /// Dateien/Unterordner werden übersprungen statt den ganzen Scan abzubrechen.
    /// </summary>
    public FileManifest Build(string rootDirectory, FileManifest? previous = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
            throw new ArgumentException("rootDirectory darf nicht leer sein.", nameof(rootDirectory));

        if (!Directory.Exists(rootDirectory))
            return FileManifest.Empty;

        var root = Path.GetFullPath(rootDirectory);
        var previousByPath = BuildLookup(previous);
        var entries = new List<FileEntry>();

        foreach (var file in EnumerateFilesSafe(root))
        {
            ct.ThrowIfCancellationRequested();

            FileInfo info;
            try
            {
                info = new FileInfo(file);
                if (!info.Exists) continue;
            }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }

            var rel = NormalizeRelative(Path.GetRelativePath(root, file));
            var size = info.Length;
            var mtime = info.LastWriteTimeUtc;

            string hash;
            if (previous is not null
                && previousByPath.TryGetValue(rel, out var prev)
                && prev.Size == size
                && prev.LastWriteUtc == mtime)
            {
                // Vorfilter: unverändert laut Größe + Schreibzeit → Hash übernehmen.
                hash = prev.Sha256;
            }
            else
            {
                try
                {
                    hash = FileHasher.HashFile(file);
                }
                catch (IOException) { continue; }          // z. B. gerade gesperrte Datei
                catch (UnauthorizedAccessException) { continue; }
            }

            entries.Add(new FileEntry(rel, hash, size, mtime));
        }

        return FileManifest.Create(entries);
    }

    /// <summary>
    /// Vergleicht zwei Manifeste. <paramref name="old"/> darf null sein (dann gilt alles
    /// im aktuellen Manifest als hinzugefügt).
    /// </summary>
    public static ManifestDiff Diff(FileManifest? old, FileManifest current)
    {
        ArgumentNullException.ThrowIfNull(current);

        var oldByPath = BuildLookup(old);
        var currentPaths = new HashSet<string>(StringComparer.Ordinal);
        var added = new List<FileEntry>();
        var changed = new List<FileEntry>();

        foreach (var e in current.Entries)
        {
            currentPaths.Add(e.RelativePath);
            if (!oldByPath.TryGetValue(e.RelativePath, out var o))
                added.Add(e);
            else if (!string.Equals(o.Sha256, e.Sha256, StringComparison.Ordinal))
                changed.Add(e);
        }

        var removed = new List<FileEntry>();
        if (old is not null)
        {
            foreach (var o in old.Entries)
                if (!currentPaths.Contains(o.RelativePath))
                    removed.Add(o);
        }

        return new ManifestDiff(added, changed, removed);
    }

    private static Dictionary<string, FileEntry> BuildLookup(FileManifest? manifest)
    {
        var map = new Dictionary<string, FileEntry>(StringComparer.Ordinal);
        if (manifest is null) return map;
        foreach (var e in manifest.Entries)
            map[e.RelativePath] = e;
        return map;
    }

    private static string NormalizeRelative(string relative)
        => relative.Replace('\\', '/');

    /// <summary>
    /// Rekursive, fehler-tolerante Datei-Aufzählung: nicht lesbare Unterordner werden
    /// übersprungen (statt den ganzen Scan mit einer Exception zu beenden).
    /// </summary>
    private static IEnumerable<string> EnumerateFilesSafe(string root)
    {
        var stack = new Stack<string>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var dir = stack.Pop();

            string[] subDirs;
            try { subDirs = Directory.GetDirectories(dir); }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }

            foreach (var d in subDirs)
                stack.Push(d);

            string[] files;
            try { files = Directory.GetFiles(dir); }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }

            foreach (var f in files)
                yield return f;
        }
    }
}
