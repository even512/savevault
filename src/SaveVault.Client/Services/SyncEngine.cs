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
/// Kern-Orchestrator des Client-Sync. Bildet je Save-Set genau die vier Fälle der
/// verbindlichen <see cref="SyncDecider"/>-Logik auf API-Aufrufe ab (Upload / Download /
/// Conflict / NoOp). IO gegen den Server läuft über <see cref="ISaveVaultApi"/> (injiziert,
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
    /// Führt einen vollständigen Sync-Zyklus für ein Save-Set aus: lokal scannen,
    /// Server-Head erfragen, entscheiden und die Entscheidung ausführen.
    /// </summary>
    public async Task<SyncCycleResult> RunCycleAsync(GameKey game, string folder, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(game);
        if (string.IsNullOrWhiteSpace(folder))
            return Report(game, SyncAction.NoOp, SyncStatus.Error, "Kein lokaler Ordner zugeordnet.", folder);

        _state.SetStatus(game, SyncStatus.Syncing, folder: folder);

        try
        {
            var state = _stateStore.Load(game);
            var local = _manifestBuilder.Build(folder, state.BaseManifest, ct);
            var head = await _api.GetHeadAsync(game, ct).ConfigureAwait(false);
            _state.MarkServerReachable(_nowUtc());

            var decision = SyncDecider.Decide(local, state, head.CurrentRevision);
            return decision.Action switch
            {
                SyncAction.Upload => await UploadAsync(game, folder, local, state, ct).ConfigureAwait(false),
                SyncAction.Download => await DownloadAsync(game, folder, head.CurrentRevision, ct).ConfigureAwait(false),
                SyncAction.Conflict => await ConflictAsync(game, folder, local, state, ct).ConfigureAwait(false),
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

    // --- die vier Fälle ------------------------------------------------------------

    private async Task<SyncCycleResult> UploadAsync(GameKey game, string folder, FileManifest local, SyncState state, CancellationToken ct)
    {
        var request = new UploadRevisionRequest(_deviceInfo(), local, IsConflict: false, BasedOnRevision: state.BaseRevision, SaveRoot: folder);
        var response = await _api.UploadRevisionAsync(game, request, ct).ConfigureAwait(false);
        await UploadMissingContentsAsync(game, folder, local, response.MissingHashes, ct).ConfigureAwait(false);

        _stateStore.Save(state with { BaseRevision = response.Revision, BaseManifest = local });
        _stateStore.ClearConflictHash(game); // sauberer Upload – etwaige Konflikt-Marke ist überholt.
        // Echte Übertragung abgeschlossen → meldenswert (Toast „gesichert").
        _state.NotifySyncActivity(game, SyncActivityKind.Uploaded);
        return Report(game, SyncAction.Upload, SyncStatus.Synced,
            $"Hochgeladen → Revision {response.Revision}", folder, response.Revision);
    }

    private async Task<SyncCycleResult> DownloadAsync(GameKey game, string folder, long serverRevision, CancellationToken ct)
    {
        var revision = await _api.GetRevisionAsync(game, serverRevision, ct).ConfigureAwait(false);
        await ApplyRevisionAsync(game, folder, revision.Manifest, revision.Number, ct).ConfigureAwait(false);
        // Echte Übertragung abgeschlossen → meldenswert (Toast „synchronisiert").
        _state.NotifySyncActivity(game, SyncActivityKind.Downloaded);
        return Report(game, SyncAction.Download, SyncStatus.Synced,
            $"Heruntergeladen ← Revision {revision.Number}", folder, revision.Number);
    }

    private async Task<SyncCycleResult> ConflictAsync(GameKey game, string folder, FileManifest local, SyncState state, CancellationToken ct)
    {
        // Konflikt: NICHTS überschreiben. Der Sync-State bleibt unverändert – so bleibt das
        // Spiel markiert, bis der Nutzer löst.
        //
        // Erneut-Upload vermeiden (M1): Hat sich die lokale Fassung seit dem letzten
        // Konflikt-Upload NICHT geändert, wird keine neue Konflikt-Revision angemeldet – die
        // Markierung bleibt bloß bestehen. Sonst entstünde bei jedem Rescan/Watcher-Tick ein
        // weiterer Revisions-Eintrag.
        var lastConflictHash = _stateStore.LoadConflictHash(game);
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
        var response = await _api.UploadRevisionAsync(game, request, ct).ConfigureAwait(false);
        await UploadMissingContentsAsync(game, folder, local, response.MissingHashes, ct).ConfigureAwait(false);
        _stateStore.SaveConflictHash(game, local.ManifestHash);

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
    /// Schreibt alle Dateien eines Server-Manifests in den Save-Ordner und zieht den
    /// lokalen Sync-State auf die gegebene Revision nach. Auch von <see cref="CommandPoller"/>
    /// (Restore / Konfliktlösung) genutzt – <b>der einzige Ort</b>, an dem Fremd-Manifeste
    /// auf die Platte geschrieben werden.
    ///
    /// Pfad-Validierung: jeder relative Pfad wird über
    /// <see cref="PathSanitizer.TryResolveWithin"/> gegen den Ordner-Root geprüft (kein
    /// <c>..</c>, kein absoluter/rooted Pfad, kein Ausbrechen). Ist auch nur ein Eintrag
    /// unsicher, wird <see cref="SyncSecurityException"/> geworfen und <b>nichts</b>
    /// geschrieben (Validierung komplett vor dem ersten Schreibzugriff).
    /// </summary>
    public async Task ApplyRevisionAsync(GameKey game, string folder, FileManifest manifest, long revisionNumber, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(manifest);
        if (string.IsNullOrWhiteSpace(folder))
            throw new SyncSecurityException(game, "(kein Ordner)");

        var fullFolder = Path.GetFullPath(folder);
        Directory.CreateDirectory(fullFolder);

        // Pass 1: ALLE Zielpfade validieren, bevor irgendetwas geschrieben wird.
        var plan = new List<(string FullPath, FileEntry Entry)>(manifest.Entries.Count);
        foreach (var entry in manifest.Entries)
        {
            if (!PathSanitizer.TryResolveWithin(fullFolder, entry.RelativePath, out var target))
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
                await using (var source = await _api.DownloadContentAsync(game, entry.Sha256, ct).ConfigureAwait(false))
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

        _stateStore.Save(new SyncState(game, revisionNumber, manifest));
        _stateStore.ClearConflictHash(game); // Stand ist auf eine echte Revision nachgezogen – Konflikt-Marke hinfällig.
    }

    // --- Hochladen fehlender Inhalte -----------------------------------------------

    private async Task UploadMissingContentsAsync(GameKey game, string folder, FileManifest local, IReadOnlyList<string> missingHashes, CancellationToken ct)
    {
        if (missingHashes.Count == 0)
            return;

        var fullFolder = Path.GetFullPath(folder);

        // Hash → erster passender relativer Pfad im lokalen Manifest.
        var pathByHash = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in local.Entries)
            pathByHash.TryAdd(entry.Sha256, entry.RelativePath);

        foreach (var hash in missingHashes)
        {
            ct.ThrowIfCancellationRequested();
            if (!pathByHash.TryGetValue(hash, out var rel))
                continue;
            // Defensive Absicherung: auch unser eigenes Manifest darf nicht aus dem Ordner führen.
            if (!PathSanitizer.TryResolveWithin(fullFolder, rel, out var source))
                continue;
            if (!File.Exists(source))
                continue;

            await using var fs = File.OpenRead(source);
            await _api.UploadContentAsync(game, hash, fs, ct).ConfigureAwait(false);
        }
    }

    private SyncCycleResult Report(GameKey game, SyncAction action, SyncStatus status, string message, string? folder, long? baseRevision = null)
    {
        _state.SetStatus(game, status, action: message, folder: folder, baseRevision: baseRevision);
        return new SyncCycleResult(game, action, status, message, _nowUtc());
    }
}
