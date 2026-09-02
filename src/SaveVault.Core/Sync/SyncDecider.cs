using SaveVault.Core.Models;

namespace SaveVault.Core.Sync;

/// <summary>
/// Reine Entscheidungslogik des Sync-Zyklus – ohne jede IO, damit sie klar testbar
/// ist. Bildet die Fälle aus der Spec (Abschnitt „Client-Zyklus") exakt ab:
///
///   0. Server &lt; base_revision (Server hat Bucket verloren)  → Upload (Reseed)
///   1. lokal geändert &amp; Server == base_revision       → Upload
///   2. nicht geändert &amp; Server &gt; base_revision      → Download
///   3. lokal geändert &amp; Server &gt; base_revision      → Conflict
///   4. sonst                                             → NoOp
///
/// Fall 0 (Reseed) steht bewusst VOR den übrigen Fällen und greift unabhängig davon,
/// ob sich lokal etwas geändert hat: Ist die Server-Revision unter die lokale
/// base_revision gefallen, wurde der Bucket serverseitig gelöscht/zurückgesetzt; der
/// lokale Stand muss neu eingesät (hochgeladen) werden, sonst käme das Spiel nie zurück.
/// </summary>
public static class SyncDecider
{
    /// <summary>
    /// Trifft die Sync-Entscheidung.
    /// </summary>
    /// <param name="localManifest">Frisch gescanntes lokales Manifest.</param>
    /// <param name="state">Gespeicherter Sync-State (base-Manifest + base_revision).</param>
    /// <param name="serverRevision">Aktuelle Server-Revisionsnummer des Spiels (0 = keine).</param>
    public static SyncDecision Decide(FileManifest localManifest, SyncState state, long serverRevision)
    {
        ArgumentNullException.ThrowIfNull(localManifest);
        ArgumentNullException.ThrowIfNull(state);

        var localChanged = LocalChanged(localManifest, state.BaseManifest);
        var baseRevision = state.BaseRevision;

        // Fall 0 (Reseed): Der Server hat den Bucket verloren/zurückgesetzt (seine Revision
        // liegt UNTER der lokalen base_revision). Egal ob lokal geändert – der lokale Stand
        // wird als neue Revision hochgeladen (neu eingesät), sonst käme ein gelöschtes Spiel
        // nie zurück (früher: NoOp). Die Upload-Basis koppelt der SyncEngine an den Server-Head.
        if (serverRevision < baseRevision)
        {
            return new SyncDecision(
                SyncAction.Upload,
                $"Server-Revision {serverRevision} < base {baseRevision}: Server hat den Bucket verloren → lokalen Stand neu einsäen.");
        }

        // Fall 3: beide Seiten seit dem letzten Sync geändert → echter Konflikt.
        if (localChanged && serverRevision > baseRevision)
        {
            return new SyncDecision(
                SyncAction.Conflict,
                $"Lokal geändert UND Server-Revision {serverRevision} > base {baseRevision}: echter Konflikt, nicht überschreiben.");
        }

        // Fall 1: nur lokal geändert (Server auf Höhe des base) → hochladen.
        // (serverRevision <= baseRevision deckt Server == base sowie den Erstupload ab.)
        if (localChanged)
        {
            return new SyncDecision(
                SyncAction.Upload,
                $"Lokal geändert, Server-Revision {serverRevision} <= base {baseRevision}: neue Revision hochladen.");
        }

        // Fall 2: nur der Server ist neuer → herunterladen.
        if (serverRevision > baseRevision)
        {
            return new SyncDecision(
                SyncAction.Download,
                $"Lokal unverändert, Server-Revision {serverRevision} > base {baseRevision}: aktuelle Revision herunterladen.");
        }

        // Fall 4: nichts zu tun.
        return new SyncDecision(
            SyncAction.NoOp,
            "Lokal unverändert und Server nicht neuer als base: nichts zu tun.");
    }

    /// <summary>
    /// Konflikterkennung, Teilaspekt: hat sich der lokale Ordner seit dem letzten Sync
    /// geändert? Vergleich über den Manifest-Hash. Ohne base-Manifest (noch nie
    /// synchronisiert) gilt jedes nicht-leere lokale Manifest als Änderung.
    /// </summary>
    public static bool LocalChanged(FileManifest local, FileManifest? baseManifest)
    {
        ArgumentNullException.ThrowIfNull(local);
        return baseManifest is null
            ? local.FileCount > 0
            : !string.Equals(local.ManifestHash, baseManifest.ManifestHash, StringComparison.Ordinal);
    }

    /// <summary>
    /// Explizite Konfliktprüfung (gekapselt, für Aufrufer, die nur diese Frage stellen):
    /// lokal geändert UND die Server-Revision ist über die base_revision hinausgelaufen.
    /// </summary>
    public static bool IsConflict(FileManifest localManifest, SyncState state, long serverRevision)
    {
        ArgumentNullException.ThrowIfNull(localManifest);
        ArgumentNullException.ThrowIfNull(state);
        return LocalChanged(localManifest, state.BaseManifest) && serverRevision > state.BaseRevision;
    }
}
