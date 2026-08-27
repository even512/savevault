using System.Globalization;
using SaveVault.Core.Models;

namespace SaveVault.Core.Storage;

/// <summary>
/// Leitet aus einem Daten-Wurzelverzeichnis die konkreten Ablagepfade des Servers ab –
/// immer über <see cref="PathSanitizer"/>, sodass fremde Spielschlüssel/Hashes nie roh
/// in den Pfad geraten. Datei-Inhalte werden inhaltsadressiert (nach ihrem SHA-256)
/// abgelegt, was für sich schon traversal-frei ist.
/// </summary>
public sealed class StoragePaths
{
    private readonly string _dataRoot;

    public StoragePaths(string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        _dataRoot = Path.GetFullPath(dataRoot);
    }

    public string DataRoot => _dataRoot;

    /// <summary>Verzeichnis eines Spiels (Ordnername = gehashter Spielschlüssel).</summary>
    public string GameDirectory(GameKey game)
        => Path.Combine(_dataRoot, "games", PathSanitizer.SafeGameFolder(game));

    /// <summary>Verzeichnis einer konkreten Revision (Metadaten-/Manifest-Ablage).</summary>
    public string RevisionDirectory(GameKey game, long revision)
        => Path.Combine(GameDirectory(game), "rev-" + revision.ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// Ablagepfad für einen inhaltsadressierten Datei-Blob (nach SHA-256). Der Hash
    /// wird zusätzlich saniert, bevor er in den Pfad geht (Gürtel-und-Hosenträger).
    /// </summary>
    public string ContentFile(GameKey game, string sha256)
    {
        var safe = PathSanitizer.SanitizeSegment(sha256);
        var shard = safe.Length >= 2 ? safe[..2] : "00";
        return Path.Combine(GameDirectory(game), "content", shard, safe);
    }

    /// <summary>Prüft, ob ein Pfad garantiert innerhalb des Datenverzeichnisses liegt.</summary>
    public bool IsWithinData(string candidate)
        => PathSanitizer.IsWithinRoot(_dataRoot, candidate);
}
