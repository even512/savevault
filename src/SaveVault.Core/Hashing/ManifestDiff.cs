using SaveVault.Core.Models;

namespace SaveVault.Core.Hashing;

/// <summary>
/// Ergebnis eines Manifest-Vergleichs: welche Dateien hinzugekommen, geändert oder
/// entfernt wurden (jeweils bezogen auf das neue Manifest, außer <see cref="Removed"/>).
/// </summary>
public sealed record ManifestDiff(
    IReadOnlyList<FileEntry> Added,
    IReadOnlyList<FileEntry> Changed,
    IReadOnlyList<FileEntry> Removed)
{
    public bool HasChanges => Added.Count > 0 || Changed.Count > 0 || Removed.Count > 0;

    public static ManifestDiff Empty { get; } = new(
        Array.Empty<FileEntry>(),
        Array.Empty<FileEntry>(),
        Array.Empty<FileEntry>());
}
