using System.IO;
using System.Net.Http;
using SaveVault.Core.Api;
using SaveVault.Core.Hashing;
using SaveVault.Core.Models;
using SaveVault.Core.Storage;
using SaveVault.Core.Sync;

namespace SaveVault.Client.Services;

/// <summary>Ergebnis eines Sync-Zyklus (für Aufrufer/Tests; die GUI liest den Status aus <see cref="AgentState"/>).</summary>
public sealed record SyncCycleResult(GameKey Game, SyncAction Action, SyncStatus Status, string Message, DateTime TimestampUtc);

/// <summary>
/// Ein vom Server geliefertes Manifest wollte eine Datei <b>außerhalb</b> des gewählten
/// Save-Ordners schreiben (Traversal-Versuch). Der Schreibvorgang wird komplett abgelehnt.
/// </summary>
public sealed class SyncSecurityException : Exception
{
    public GameKey Game { get; }
    public string RejectedPath { get; }

    public SyncSecurityException(GameKey game, string rejectedPath)
        : base($"Unsicherer Zielpfad abgelehnt (kein Schreiben außerhalb des Save-Ordners): '{rejectedPath}'.")
    {
        Game = game;
        RejectedPath = rejectedPath;
    }
}

/// <summary>
/// Kern-Orchestrator des Client-Sync. Bildet die Fälle der verbindlichen
/// <see cref="SyncDecider"/>-Logik (inkl. Reseed bei Server-Verlust) auf die vier API-Aktionen
/// Upload / Download / Conflict / NoOp ab. IO gegen den Server läuft über <see cref="ISaveVaultApi"/> (injiziert,
/// damit testbar), das lokale Scannen über <see cref="ManifestBuilder"/>.
///
/// <b>Sicherheit:</b> Alle vom Server heruntergeladenen Dateien werden ausschließlich über
/// <see cref="ApplyRevisionAsync"/> geschrieben, das jeden relativen Pfad strikt gegen den
/// Save-Ordner validiert (kein <c>..</c>, kein absoluter Pfad, kein Ausbrechen) und den
/// gesamten Schreibvorgang ablehnt, sobald ein Eintrag unsicher ist.
/// </summary>
public sealed class SyncEngine
{
    private readonly ISaveVaultApi _api;
    private readonly SyncStateStore _stateStore;
    private readonly AgentState _state;
    private readonly Func<DeviceInfo> _deviceInfo;
    private readonly ManifestBuilder _manifestBuilder;
    private readonly Func<DateTime> _nowUtc;

    public SyncEngine(
        ISaveVaultApi api,
        SyncStateStore stateStore,
        AgentState state,
        Func<DeviceInfo> deviceInfo,
        ManifestBuilder? manifestBuilder = null,
        Func<DateTime>? nowUtc = null)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _deviceInfo = deviceInfo ?? throw new ArgumentNullException(nameof(deviceInfo));
        _manifestBuilder = manifestBuilder ?? new ManifestBuilder();
        _nowUtc = nowUtc ?? (() => DateTime.UtcNow);
    }

    /// <summary>
    /// Führt einen vollständigen Sync-Zyklus für ein Save-Set aus: lokal scannen (über <b>alle</b>
    /// Save-Wurzeln des Spiels), Server-Head erfragen, entscheiden und die Entscheidung ausführen.
    /// Der Sync ist pro Spiel serialisiert (ein Gate übers ganze Spiel, alle seine Wurzeln).
    /// </summary>
    public async Task<SyncCycleResult> RunCycleAsync(GameKey game, IReadOnlyList<SaveRoot> roots, BucketScope scope = BucketScope.Private, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(roots);
        var folder = PrimaryFolder(roots);
        if (roots.Count == 0)
            return Report(game, SyncAction.NoOp, SyncStatus.Error, "Kein lokaler Ordner zugeordnet.", folder);

        _state.SetStatus(game, SyncStatus.Syncing, folder: folder);

        try
        {
            var state = _stateStore.Load(game, scope);
            var local = _manifestBuilder.BuildCombined(roots, state.BaseManifest, ct);
            var head = await _api.GetHeadAsync(game, scope, ct).ConfigureAwait(false);
            _state.MarkServerReachable(_nowUtc());

            var decision = SyncDecider.Decide(local, state, head.CurrentRevision);
            return decision.Action switch
            {
                SyncAction.Upload => await UploadAsync(game, roots, local, state, head.CurrentRevision, scope, ct).ConfigureAwait(false),
                SyncAction.Download => await DownloadAsync(game, roots, head.CurrentRevision, scope, ct).ConfigureAwait(false),
                SyncAction.Conflict => await ConflictAsync(game, roots, local, state, scope, ct).ConfigureAwait(false),
                _ => NoOp(game, folder, state.BaseRevision, decision.Reason),
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (SaveVaultApiException ex)
        {
            _state.MarkServerUnreachable(ex.Message);
            return Report(game, SyncAction.NoOp, SyncStatus.Error, "Serverfehler: " + ex.Message, folder);
        }
        catch (HttpRequestException ex)
        {
            _state.MarkServerUnreachable(ex.Message);
            return Report(game, SyncAction.NoOp, SyncStatus.Error, "Server nicht erreichbar: " + ex.Message, folder);
        }
        catch (SyncSecurityException ex)
        {
            return Report(game, SyncAction.NoOp, SyncStatus.Error, ex.Message, folder);
        }
    }

    /// <summary>Der primäre (erste) Ordner eines Root-Sets – für Anzeige/Metadaten.</summary>
    private static string PrimaryFolder(IReadOnlyList<SaveRoot> roots)
        => roots.Count > 0 ? roots[0].Folder : "(kein Ordner)";

    /// <summary>
    /// Löst die Ordner der Wurzeln auf Vollpfade auf und überspringt dabei defekte Einträge
    /// (leerer/ungültiger Pfad) sauber, statt mit einer untypisierten Exception abzubrechen.
    /// </summary>
    private static List<SaveRoot> ResolveRootsSafe(IReadOnlyList<SaveRoot> roots)
    {
        var list = new List<SaveRoot>(roots.Count);
        foreach (var r in roots)
        {
            if (r is null || string.IsNullOrWhiteSpace(r.Folder))
                continue;
            string full;
            try { full = Path.GetFullPath(r.Folder); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { continue; }
            list.Add(new SaveRoot(r.Key, full));
        }
        return list;
    }

    // --- die vier Fälle ------------------------------------------------------------

    private async Task<SyncCycleResult> UploadAsync(GameKey game, IReadOnlyList<SaveRoot> roots, FileManifest local, SyncState state, long serverHeadRevision, BucketScope scope, CancellationToken ct)
    {
        var folder = PrimaryFolder(roots);
        // Upload-Basis an den tatsächlichen Server-Head koppeln (nicht an die lokale
        // base_revision): Der Server verlangt BasedOnRevision == aktuelle Server-Revision,
        // sonst 409. Im Normalfall (Server-Head == base) ist das bit-identisch; beim Reseed
        // (Server-Head < base, Bucket serverseitig verloren) nimmt der Server so die neue
        // Revision an, statt sie als „veraltete Basis" abzulehnen.
        var request = new UploadRevisionRequest(_deviceInfo(), local, IsConflict: false, BasedOnRevision: serverHeadRevision, SaveRoot: folder);
        var response = await _api.UploadRevisionAsync(game, request, scope, ct).ConfigureAwait(false);
        await UploadMissingContentsAsync(game, roots, local, response.MissingHashes, scope, ct).ConfigureAwait(false);

        _stateStore.Save(state with { BaseRevision = response.Revision, BaseManifest = local }, scope);
        _stateStore.ClearConflictHash(game, scope); // sauberer Upload – etwaige Konflikt-Marke ist überholt.
        // Echte Übertragung abgeschlossen → meldenswert (Toast „gesichert").
        _state.NotifySyncActivity(game, SyncActivityKind.Uploaded);
        return Report(game, SyncAction.Upload, SyncStatus.Synced,
            $"Hochgeladen → Revision {response.Revision}", folder, response.Revision);
    }

    private async Task<SyncCycleResult> DownloadAsync(GameKey game, IReadOnlyList<SaveRoot> roots, long serverRevision, BucketScope scope, CancellationToken ct)
    {
        var revision = await _api.GetRevisionAsync(game, serverRevision, scope, ct).ConfigureAwait(false);
        await ApplyRevisionAsync(game, roots, revision.Manifest, revision.Number, scope, ct).ConfigureAwait(false);
        // Echte Übertragung abgeschlossen → meldenswert (Toast „synchronisiert").
        _state.NotifySyncActivity(game, SyncActivityKind.Downloaded);
        return Report(game, SyncAction.Download, SyncStatus.Synced,
            $"Heruntergeladen ← Revision {revision.Number}", PrimaryFolder(roots), revision.Number);
    }

    private async Task<SyncCycleResult> ConflictAsync(GameKey game, IReadOnlyList<SaveRoot> roots, FileManifest local, SyncState state, BucketScope scope, CancellationToken ct)
    {
        var folder = PrimaryFolder(roots);
        // Konflikt: NICHTS überschreiben. Der Sync-State bleibt unverändert – so bleibt das
        // Spiel markiert, bis der Nutzer löst.
        //
        // Erneut-Upload vermeiden (M1): Hat sich die lokale Fassung seit dem letzten
        // Konflikt-Upload NICHT geändert, wird keine neue Konflikt-Revision angemeldet – die
        // Markierung bleibt bloß bestehen. Sonst entstünde bei jedem Rescan/Watcher-Tick ein
        // weiterer Revisions-Eintrag.
        var lastConflictHash = _stateStore.LoadConflictHash(game, scope);
        if (string.Equals(lastConflictHash, local.ManifestHash, StringComparison.Ordinal))
        {
            _state.SetStatus(game, SyncStatus.Conflict,
                action: "Konflikt besteht weiter (unveränderte lokale Fassung, kein erneuter Upload)",
                folder: folder, baseRevision: state.BaseRevision);
            return new SyncCycleResult(game, SyncAction.Conflict, SyncStatus.Conflict,
                "Konflikt unverändert – kein erneuter Upload.", _nowUtc());
        }

        // Neue/geänderte lokale Fassung: als Konflikt-Revision sichern (damit sie nicht
        // verloren geht) und die Konflikt-Marke auf diesen Stand setzen.
        var request = new UploadRevisionRequest(_deviceInfo(), local, IsConflict: true, BasedOnRevision: state.BaseRevision, SaveRoot: folder);
        var response = await _api.UploadRevisionAsync(game, request, scope, ct).ConfigureAwait(false);
        await UploadMissingContentsAsync(game, roots, local, response.MissingHashes, scope, ct).ConfigureAwait(false);
        _stateStore.SaveConflictHash(game, local.ManifestHash, scope);

        // Neu erkannter/geänderter Konflikt (eine echte Konflikt-Revision wurde angelegt) →
        // meldenswert. Der Zweig oben („Konflikt besteht weiter", unveränderte lokale Fassung)
        // lädt NICHTS hoch und ist ein reiner Statuswechsel – dort bewusst KEINE Meldung, sonst
        // entstünde bei jedem Rescan/Watcher-Tick ein Toast.
        _state.NotifySyncActivity(game, SyncActivityKind.Conflict);
        return Report(game, SyncAction.Conflict, SyncStatus.Conflict,
            $"Konflikt erkannt – lokale Fassung als Konflikt-Revision {response.Revision} gesichert, nichts überschrieben",
            folder, state.BaseRevision);
    }

    private SyncCycleResult NoOp(GameKey game, string folder, long baseRevision, string reason)
    {
        // Nur wenn kein Konflikt aussteht auf „Synced" – ein bestehender Konflikt bleibt sichtbar.
        var status = _state.GetStatus(game) == SyncStatus.Conflict ? SyncStatus.Conflict : SyncStatus.Synced;
        _state.SetStatus(game, status, folder: folder, baseRevision: baseRevision);
        return new SyncCycleResult(game, SyncAction.NoOp, status, reason, _nowUtc());
    }

    // --- Schreiben heruntergeladener Dateien (SICHERHEITS-CHOKEPOINT) --------------

    /// <summary>
    /// Schreibt alle Dateien eines Server-Manifests in die Save-Wurzeln des Spiels und zieht den
    /// lokalen Sync-State auf die gegebene Revision nach. Auch von <see cref="CommandPoller"/>
    /// (Restore / Konfliktlösung) genutzt – <b>der einzige Ort</b>, an dem Fremd-Manifeste auf die
    /// Platte geschrieben werden.
    ///
    /// <para><b>Mehr-Root-Routing (SICHERHEIT):</b> Je Manifest-Eintrag wird über
    /// <see cref="SaveRootLayout.TryResolve"/> die zuständige lokale Wurzel und der Rest-Pfad
    /// bestimmt; darauf validiert <see cref="PathSanitizer.TryResolveWithin"/> strikt gegen
    /// <b>genau diese</b> Wurzel (kein <c>..</c>, kein absoluter/rooted Pfad, kein Ausbrechen).
    /// Ein <b>unbekannter/nicht abbildbarer</b> Root-Key (Wurzel auf diesem Gerät nicht vorhanden)
    /// wird <b>übersprungen</b> – kein Blindschreiben außerhalb bekannter Wurzeln. Ein <b>Traversal</b>
    /// (Rest-Pfad bricht aus seiner Wurzel aus) lässt den gesamten Vorgang mit
    /// <see cref="SyncSecurityException"/> scheitern – <b>nichts</b> wird geschrieben
    /// (Alles-oder-nichts). Die komplette Validierung läuft vor dem ersten Schreibzugriff.</para>
    /// </summary>
    public async Task ApplyRevisionAsync(GameKey game, IReadOnlyList<SaveRoot> roots, FileManifest manifest, long revisionNumber, BucketScope scope = BucketScope.Private, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(manifest);
        if (roots.Count == 0)
            throw new SyncSecurityException(game, "(kein Ordner)");

        // Zielordner der Wurzeln vorab auflösen (defekte Roots sauber überspringen, statt mit
        // untypisierter Exception abzubrechen). Bleibt keine gültige Wurzel, wird der gesamte
        // Vorgang abgelehnt (nichts wird geschrieben).
        var resolvedRoots = ResolveRootsSafe(roots);
        if (resolvedRoots.Count == 0)
            throw new SyncSecurityException(game, "(kein gültiger Ordner)");

        // Pass 1: ALLE Einträge auf ihre Wurzel abbilden und validieren, bevor etwas geschrieben wird.
        // Unbekannter Root-Key → Eintrag überspringen; Traversal → gesamter Vorgang abgelehnt.
        var plan = new List<(string FullPath, FileEntry Entry)>(manifest.Entries.Count);
        foreach (var entry in manifest.Entries)
        {
            if (!SaveRootLayout.TryResolve(resolvedRoots, entry.RelativePath, out var folder, out var subPath))
                continue; // unbekannter/nicht abbildbarer Root-Key → nicht schreiben
            if (!PathSanitizer.TryResolveWithin(folder, subPath, out var target))
                throw new SyncSecurityException(game, entry.RelativePath);
            plan.Add((target, entry));
        }

        // Pass 2: inhaltsadressiert herunterladen und atomar an ihren Platz schreiben.
        foreach (var (target, entry) in plan)
        {
            ct.ThrowIfCancellationRequested();
            var dir = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var tmp = target + ".svtmp-" + Guid.NewGuid().ToString("N");
            try
            {
                await using (var source = await _api.DownloadContentAsync(game, entry.Sha256, scope, ct).ConfigureAwait(false))
                await using (var dest = File.Create(tmp))
                {
                    await source.CopyToAsync(dest, ct).ConfigureAwait(false);
                }
                File.Move(tmp, target, overwrite: true);
            }
            catch
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best effort */ }
                throw;
            }
        }

        _stateStore.Save(new SyncState(game, revisionNumber, manifest), scope);
        _stateStore.ClearConflictHash(game, scope); // Stand ist auf eine echte Revision nachgezogen – Konflikt-Marke hinfällig.
    }

    // --- Hochladen fehlender Inhalte -----------------------------------------------

    private async Task UploadMissingContentsAsync(GameKey game, IReadOnlyList<SaveRoot> roots, FileManifest local, IReadOnlyList<string> missingHashes, BucketScope scope, CancellationToken ct)
    {
        if (missingHashes.Count == 0)
            return;

        var resolvedRoots = ResolveRootsSafe(roots);
        if (resolvedRoots.Count == 0)
            return;

        // Hash → erster passender (präfixierter) relativer Pfad im lokalen Manifest.
        var pathByHash = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in local.Entries)
            pathByHash.TryAdd(entry.Sha256, entry.RelativePath);

        foreach (var hash in missingHashes)
        {
            ct.ThrowIfCancellationRequested();
            if (!pathByHash.TryGetValue(hash, out var rel))
                continue;
            // Manifest-Pfad → zuständige Wurzel + Rest-Pfad; dann defensiv gegen die Wurzel validieren
            // (auch das eigene Manifest darf nicht aus der Wurzel führen).
            if (!SaveRootLayout.TryResolve(resolvedRoots, rel, out var folder, out var subPath))
                continue;
            if (!PathSanitizer.TryResolveWithin(folder, subPath, out var source))
                continue;
            if (!File.Exists(source))
                continue;

            await using var fs = File.OpenRead(source);
            await _api.UploadContentAsync(game, hash, fs, scope, ct).ConfigureAwait(false);
        }
    }

    private SyncCycleResult Report(GameKey game, SyncAction action, SyncStatus status, string message, string? folder, long? baseRevision = null)
    {
        _state.SetStatus(game, status, action: message, folder: folder, baseRevision: baseRevision);
        return new SyncCycleResult(game, action, status, message, _nowUtc());
    }
}
