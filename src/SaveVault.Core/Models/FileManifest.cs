using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace SaveVault.Core.Models;

/// <summary>
/// Ein Datei-Snapshot eines Save-Sets: die Menge der <see cref="FileEntry"/> plus
/// abgeleitete Kennzahlen (Gesamtgröße, Dateizahl) und ein deterministischer
/// <see cref="ManifestHash"/> über die nach Pfad sortierten Einträge. Der Manifest-Hash
/// dient als schnelle Gleichheitsprüfung („hat sich der Ordner geändert?").
/// </summary>
public sealed class FileManifest
{
    public IReadOnlyList<FileEntry> Entries { get; }
    public long TotalBytes { get; }
    public int FileCount { get; }

    /// <summary>Hex-SHA-256 über die sortierten Einträge (Pfad, Hash, Größe).</summary>
    public string ManifestHash { get; }

    [JsonConstructor]
    public FileManifest(IReadOnlyList<FileEntry> entries, long totalBytes, int fileCount, string manifestHash)
    {
        Entries = entries ?? Array.Empty<FileEntry>();
        TotalBytes = totalBytes;
        FileCount = fileCount;
        ManifestHash = manifestHash ?? string.Empty;
    }

    /// <summary>Baut ein Manifest aus Einträgen: sortiert, zählt und berechnet den Prüfhash.</summary>
    public static FileManifest Create(IEnumerable<FileEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var sorted = entries.OrderBy(e => e.RelativePath, StringComparer.Ordinal).ToArray();

        long total = 0;
        using var inc = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var nul = new byte[] { 0 };
        var nl = new byte[] { (byte)'\n' };
        foreach (var e in sorted)
        {
            total += e.Size;
            inc.AppendData(Encoding.UTF8.GetBytes(e.RelativePath));
            inc.AppendData(nul);
            inc.AppendData(Encoding.UTF8.GetBytes(e.Sha256));
            inc.AppendData(nul);
            inc.AppendData(Encoding.UTF8.GetBytes(e.Size.ToString(CultureInfo.InvariantCulture)));
            inc.AppendData(nl);
        }
        var hash = Convert.ToHexStringLower(inc.GetHashAndReset());
        return new FileManifest(sorted, total, sorted.Length, hash);
    }

    /// <summary>Ein leeres Manifest (kein Ordner / keine Dateien).</summary>
    public static FileManifest Empty { get; } = Create(Array.Empty<FileEntry>());

    public FileEntry? Find(string relativePath)
        => Entries.FirstOrDefault(e => string.Equals(e.RelativePath, relativePath, StringComparison.Ordinal));
}
