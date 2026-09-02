using System.IO;
using SaveVault.Core.Models;
using SaveVault.Core.Storage;
using SaveVault.Core.Sync;

namespace SaveVault.Client.Services;

/// <summary>Persistierte Konflikt-Marke: der Manifest-Hash der zuletzt hochgeladenen
/// Konflikt-Fassung eines Spiels (getrennt vom echten <see cref="SyncState"/>).</summary>
internal sealed class ConflictMark
{
    public string? ManifestHash { get; set; }
}

/// <summary>
/// Persistiert je Save-Set einen <see cref="SyncState"/> unter
/// <c>%AppData%\SaveVault\state\&lt;hash&gt;.json</c>. Der Dateiname wird aus dem
/// <see cref="GameKey.Value"/> <b>gehasht</b> (nie der rohe Schlüssel als Pfad,
/// analog zur serverseitigen Ablage) – so kann ein fremder Spielname nie in einen
/// Pfad ausbrechen. Geschrieben wird atomar, geladen tolerant (fehlt/kaputt →
/// <see cref="SyncState.Initial"/>).
/// </summary>
public sealed class SyncStateStore
{
    private readonly AppPaths _paths;

    public SyncStateStore(AppPaths paths)
        => _paths = paths ?? throw new ArgumentNullException(nameof(paths));

    /// <summary>Lädt den Sync-State eines Spiels oder den Startzustand, falls keiner existiert.</summary>
    public SyncState Load(GameKey game)
    {
        ArgumentNullException.ThrowIfNull(game);
        return JsonFileStore.Read<SyncState>(PathFor(game)) ?? SyncState.Initial(game);
    }

    /// <summary>Speichert den Sync-State eines Spiels atomar.</summary>
    public void Save(SyncState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        JsonFileStore.Write(PathFor(state.Game), state);
    }

    /// <summary>
    /// Lädt den Manifest-Hash der zuletzt hochgeladenen Konflikt-Fassung eines Spiels
    /// (oder <c>null</c>, wenn keine Konflikt-Marke existiert). Bewusst getrennt vom
    /// <see cref="SyncState"/>, damit die Konflikt-Nachverfolgung den echten Basis-Stand
    /// (<see cref="SyncState.BaseRevision"/>/<see cref="SyncState.BaseManifest"/>) nie verfälscht.
    /// </summary>
    public string? LoadConflictHash(GameKey game)
    {
        ArgumentNullException.ThrowIfNull(game);
        return JsonFileStore.Read<ConflictMark>(ConflictPathFor(game))?.ManifestHash;
    }

    /// <summary>Merkt sich den Manifest-Hash der zuletzt gemeldeten Konflikt-Fassung.</summary>
    public void SaveConflictHash(GameKey game, string manifestHash)
    {
        ArgumentNullException.ThrowIfNull(game);
        JsonFileStore.Write(ConflictPathFor(game), new ConflictMark { ManifestHash = manifestHash });
    }

    /// <summary>
    /// Löscht ALLE persistierten Sync-States (und Konflikt-Marken). Einmalige Migration auf
    /// geräte-eigene Buckets: der lokale Basis-Stand wird verworfen, damit der aktuelle lokale
    /// Save beim nächsten Zyklus als Revision 1 in den PRIVATEN Bucket neu eingesät wird (Backup),
    /// statt gegen den alten globalen Verlauf zu laufen. Tolerant – einzelne Löschfehler werden
    /// geschluckt (im schlimmsten Fall reseedet ein einzelnes Spiel erst beim nächsten Anlauf).
    /// </summary>
    public void ResetAllState()
    {
        try
        {
            var dir = _paths.StateDirectory;
            if (!Directory.Exists(dir))
                return;
            foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
            {
                try { File.Delete(file); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* best effort */ }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            // Nicht kritisch – die Migration wird beim nächsten Start erneut versucht, solange das
            // Flag in der Config nicht gesetzt werden konnte.
        }
    }

    /// <summary>Löscht die Konflikt-Marke (nach Auflösung/erfolgreichem Sync).</summary>
    public void ClearConflictHash(GameKey game)
    {
        ArgumentNullException.ThrowIfNull(game);
        try
        {
            var path = ConflictPathFor(game);
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nicht kritisch – die Marke wird beim nächsten Konflikt ohnehin überschrieben.
        }
    }

    private string PathFor(GameKey game)
        => Path.Combine(_paths.StateDirectory, PathSanitizer.HashKey(game.Value) + ".json");

    private string ConflictPathFor(GameKey game)
        => Path.Combine(_paths.StateDirectory, PathSanitizer.HashKey(game.Value) + ".conflict.json");
}
