using System.IO;
using System.Net.Http;
using System.Threading;
using SaveVault.Core.Api;
using SaveVault.Core.Ludusavi;
using SaveVault.Core.Models;

namespace SaveVault.Client.Services;

/// <summary>
/// Der Kopf des Client-Hintergrunds: bindet Konfiguration, Ordner-Registry, Erkennung,
/// Watcher, Sync-Engine, Befehls-Poller und Heartbeat zu einem laufenden Dienst zusammen.
/// Reine Logik, <b>kein WPF</b> – die GUI (Schritt 6) liest ausschließlich die
/// beobachtbare Status-Fläche <see cref="State"/> und ruft Aktionen wie
/// <see cref="PairAsync"/>, <see cref="AddManualFolder"/> oder <see cref="SyncNowAsync"/> auf.
///
/// Ablauf bei <see cref="StartAsync"/>: ist der Client nicht eingerichtet, bleibt der Agent
/// im Ruhezustand (kein Absturz). Sonst laufen an: ein Watcher je Save-Ordner (entprellt),
/// ein periodischer Rescan als Sicherheitsnetz gegen verlorene FS-Ereignisse, der
/// Befehls-Poller und der Heartbeat. Der Sync eines Save-Sets ist pro Spiel serialisiert
/// (SemaphoreSlim), damit Watcher-Ereignis und Rescan nie gleichzeitig laufen.
/// </summary>
public sealed class ClientAgent : IAsyncDisposable
{
    private readonly AppPaths _paths;
    private readonly ClientConfigStore _configStore;
    private readonly SyncStateStore _stateStore;
    private readonly SaveFolderRegistry _registry;
    private readonly GameExclusionStore _exclusions;
    private readonly CoverCache _covers;
    private readonly GameDiscovery _discovery;
    private readonly PairingService _pairing;
    private readonly TimeSpan _debounce;

    private readonly GameSerializer _serializer = new();
    private readonly List<FolderWatcher> _watchers = new();
    private readonly object _lifecycleLock = new();

    private HttpClient? _http;
    private SaveVaultApiClient? _api;
    private SyncEngine? _engine;
    private CommandPoller? _commandPoller;
    private HeartbeatReporter? _heartbeat;

    private CancellationTokenSource? _cts;
    private Task? _rescanLoop;
    private Task? _commandLoop;
    private Task? _heartbeatLoop;
    private bool _running;

    /// <summary>Die beobachtbare Status-Fläche (von der GUI zu konsumieren).</summary>
    public AgentState State { get; }

    /// <summary>
    /// Lazy Box-Art-Cache für die GUI (ein Cover je Aufruf). Holt das Cover über den aktuellen
    /// Geräte-Token/HttpClient; ohne laufende Verbindung oder ohne Cover → <c>null</c> (Fallback).
    /// </summary>
    public CoverCache Covers => _covers;

    public ClientAgent(AppPaths? paths = null, LudusaviClient? ludusavi = null, TimeSpan? debounce = null)
    {
        _paths = paths ?? new AppPaths();
        _configStore = new ClientConfigStore(_paths);
        _stateStore = new SyncStateStore(_paths);
        _registry = new SaveFolderRegistry(_paths);
        _exclusions = new GameExclusionStore(_paths);
        _discovery = new GameDiscovery(ludusavi ?? new LudusaviClient());
        _pairing = new PairingService(_configStore);
        _debounce = debounce ?? TimeSpan.FromSeconds(2);
        State = new AgentState();

        // Cover werden über den JEWEILS aktuellen API-Client geholt (er entsteht/vergeht mit
        // Start/Stop). Ohne verbundenen Client liefert der Fetcher null → die GUI nutzt den Fallback.
        _covers = new CoverCache(_paths, (game, ct) =>
        {
            var api = _api;
            return api is null ? Task.FromResult<byte[]?>(null) : api.GetCoverAsync(game, ct);
        });
    }

    /// <summary>Ob der Agent gerade seine Netz-/Watcher-Schleifen betreibt.</summary>
    public bool IsRunning { get { lock (_lifecycleLock) return _running; } }

    // --- Lebenszyklus --------------------------------------------------------------

    /// <summary>Startet den Hintergrunddienst. Nicht eingerichtet → Ruhezustand, kein Fehler.</summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        var config = _configStore.Load();
        State.SetConfigured(config.IsConfigured);

        // Einmalige Migration auf geräte-eigene Buckets (siehe specs/savevault-change-per-device-sync.md):
        // den lokalen Basis-Stand einmalig verwerfen, damit jedes Spiel als Revision 1 in den privaten
        // Bucket neu eingesät wird (Per-Gerät-Backup), statt gegen den alten globalen Verlauf zu laufen.
        // Nur einmal – danach persistiert das Flag in der Config.
        if (!config.PerDeviceBucketsMigrated)
        {
            _stateStore.ResetAllState();
            config.PerDeviceBucketsMigrated = true;
            _configStore.Save(config);
        }

        if (!config.IsConfigured)
            return; // „nicht eingerichtet" – auf Pairing warten.

        if (!Uri.TryCreate(config.ServerUrl, UriKind.Absolute, out var serverUri)
            || (serverUri.Scheme != Uri.UriSchemeHttp && serverUri.Scheme != Uri.UriSchemeHttps))
        {
            State.MarkServerUnreachable("Ungültige Server-URL in der Konfiguration.");
            return;
        }

        lock (_lifecycleLock)
        {
            if (_running)
                return;
            _running = true;
        }

        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        // Server-API mit BaseAddress + Token verdrahten.
        _http = new HttpClient { BaseAddress = serverUri, Timeout = TimeSpan.FromMinutes(5) };
        _api = new SaveVaultApiClient(_http, config.DeviceToken);

        _engine = new SyncEngine(_api, _stateStore, State, () => DeviceIdentity.FromConfig(_configStore.Load(), DateTime.UtcNow));
        _commandPoller = new CommandPoller(_api, _configStore, _registry, _engine, State, _serializer);
        _heartbeat = new HeartbeatReporter(_api, _configStore, _registry, _stateStore, State);

        // Erkennung (best effort) und Ordner in der Status-Fläche vormerken.
        await RefreshDiscoveryAsync(token).ConfigureAwait(false);
        foreach (var entry in _registry.GetAll())
            State.EnsureGame(entry.Game, entry.FolderPath, _stateStore.Load(entry.Game).BaseRevision);

        // Persistierten Ausschluss-Zustand in die Anzeige nachziehen (überlebt so den Neustart).
        foreach (var entry in _registry.GetAll())
            if (_exclusions.IsExcluded(entry.Game))
                State.SetExcluded(entry.Game, true);

        StartWatchers(token);

        var interval = config.SyncInterval;
        _rescanLoop = RunLoopAsync(interval, RescanAllAsync, token, runImmediately: true);
        _commandLoop = RunLoopAsync(interval, c => _commandPoller!.PollOnceAsync(c), token, runImmediately: true);
        _heartbeatLoop = RunLoopAsync(interval, c => _heartbeat!.SendOnceAsync(c), token, runImmediately: true);
    }

    /// <summary>Stoppt alle Schleifen und Watcher und gibt die Netz-Ressourcen frei.</summary>
    public async Task StopAsync()
    {
        lock (_lifecycleLock)
        {
            if (!_running)
                return;
            _running = false;
        }

        try { _cts?.Cancel(); } catch { /* ignore */ }

        DisposeWatchers();

        await WhenAllQuiet(_rescanLoop, _commandLoop, _heartbeatLoop).ConfigureAwait(false);
        _rescanLoop = _commandLoop = _heartbeatLoop = null;

        _cts?.Dispose();
        _cts = null;
        _http?.Dispose();
        _http = null;
        _api = null;
        _engine = null;
        _commandPoller = null;
        _heartbeat = null;
    }

    // --- Aktionen für die GUI ------------------------------------------------------

    /// <summary>Führt das Pairing durch und startet den Dienst bei Erfolg neu.</summary>
    public async Task<PairingResult> PairAsync(string serverUrl, string code, string deviceName, CancellationToken ct = default)
    {
        var result = await _pairing.PairAsync(serverUrl, code, deviceName, ct).ConfigureAwait(false);
        if (result.Success)
        {
            await StopAsync().ConfigureAwait(false);
            await StartAsync(ct).ConfigureAwait(false);
        }
        return result;
    }

    /// <summary>
    /// Fügt einen manuell gewählten Ordner für ein Spiel hinzu (validiert den Ordner),
    /// nimmt ihn in die Status-Fläche auf und startet – falls laufend – einen Watcher.
    /// </summary>
    public void AddManualFolder(GameKey game, string folderPath)
    {
        _registry.AddManual(game, folderPath);
        var entry = _registry.TryGet(game);
        if (entry is null)
            return;

        State.EnsureGame(entry.Game, entry.FolderPath, _stateStore.Load(entry.Game).BaseRevision);

        CancellationToken token;
        lock (_lifecycleLock)
        {
            if (!_running || _cts is null)
                return;
            token = _cts.Token;
        }
        AddWatcher(entry, token);
        _ = SyncGameSafeAsync(entry.Game, entry.FolderPath, token);
    }

    /// <summary>Ob ein Spiel aktuell dauerhaft vom Sync ausgeschlossen ist.</summary>
    public bool IsExcluded(GameKey game)
    {
        ArgumentNullException.ThrowIfNull(game);
        return _exclusions.IsExcluded(game);
    }

    /// <summary>
    /// Nimmt ein Spiel dauerhaft vom Sync („Sync pausieren"): persistiert den Ausschluss und
    /// zieht den Anzeige-Zustand nach. Ab sofort überspringt der einzige Sync-Choke-Point
    /// (<see cref="SyncGameSafeAsync"/>) dieses Spiel bei Rescan, Watcher und „Jetzt
    /// synchronisieren". Ein laufender Watcher darf bleiben – er löst nur keinen Sync mehr aus.
    /// </summary>
    public void ExcludeGame(GameKey game)
    {
        ArgumentNullException.ThrowIfNull(game);
        _exclusions.Add(game);
        State.SetExcluded(game, true);
    }

    /// <summary>
    /// Hebt den Ausschluss eines Spiels wieder auf („Wieder einschließen"): entfernt ihn aus dem
    /// persistenten Store, zieht den Anzeige-Zustand nach und stößt – falls der Dienst läuft und
    /// ein Ordner bekannt ist – gleich einen Sync an, damit der Rückstand aufgeholt wird.
    /// </summary>
    public void IncludeGame(GameKey game)
    {
        ArgumentNullException.ThrowIfNull(game);
        _exclusions.Remove(game);
        State.SetExcluded(game, false);

        var entry = _registry.TryGet(game);
        if (entry is null)
            return;

        CancellationToken token;
        lock (_lifecycleLock)
        {
            if (!_running || _cts is null)
                return;
            token = _cts.Token;
        }
        _ = SyncGameSafeAsync(entry.Game, entry.FolderPath, token);
    }

    /// <summary>Erkennt Spiele neu über ludusavi und übernimmt sie in die Registry.</summary>
    public async Task<DiscoveryResult> RefreshDiscoveryAsync(CancellationToken ct = default)
    {
        DiscoveryResult result;
        try
        {
            result = await _discovery.DiscoverAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new DiscoveryResult(_discovery.IsAvailable, Array.Empty<DiscoveredGame>(), ex.Message);
        }

        if (result.Games.Count > 0)
        {
            _registry.SetDiscovered(result.Games.Select(g => (g.Game, g.SaveFolder)));
            foreach (var g in result.Games)
                State.EnsureGame(g.Game, g.SaveFolder, _stateStore.Load(g.Game).BaseRevision);
        }

        // Übersprungene Spiele dauerhaft sichtbar machen (mit Hinweis „manuell zuordnen").
        // Nur bei einer tatsächlich erfolgreichen Erkennung – ein ludusavi-Aussetzer (nicht
        // verfügbar / Fehler) darf bestehende Skip-Marker nicht löschen.
        if (result.LudusaviAvailable && result.Error is null)
        {
            State.ReplaceSkipped(result.Skipped
                .Select(s => (GameKey.FromName(s.Name), SkipReasonText(s)))
                .ToList());
        }
        return result;

        static string SkipReasonText(SkippedGame s) => s.Reason switch
        {
            SkipReason.TooLarge => s.Detail is null
                ? "Save-Set zu groß – bitte gezielt einen kleineren Unterordner zuordnen."
                : $"Save-Set zu groß ({s.Detail}) – bitte gezielt einen kleineren Unterordner zuordnen.",
            _ => "Save-Ordner nicht eindeutig – bitte manuell den richtigen Save-Ordner zuordnen.",
        };
    }

    /// <summary>Stößt einen Sync-Durchlauf über alle bekannten Save-Sets an.</summary>
    public async Task SyncNowAsync(CancellationToken ct = default)
    {
        CancellationToken token = ct;
        lock (_lifecycleLock)
        {
            if (_running && _cts is not null)
                token = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token).Token;
        }
        await RescanAllAsync(token).ConfigureAwait(false);
    }

    // --- dünne Durchreicher für die GUI (Konflikt-Dialog) --------------------------
    // Reine Weiterleitung an die Server-API, ohne eigene Domänenlogik. Ist der Agent
    // nicht eingerichtet/laufend (kein _api) oder antwortet der Server mit Fehler, wird
    // ein leeres/negatives Ergebnis geliefert – nie eine Exception in die GUI.

    /// <summary>Alle offenen Konflikte (leer, wenn nicht eingerichtet/erreichbar).</summary>
    public async Task<IReadOnlyList<Conflict>> GetConflictsAsync(CancellationToken ct = default)
    {
        var api = _api;
        if (api is null)
            return Array.Empty<Conflict>();
        try
        {
            var response = await api.GetConflictsAsync(ct).ConfigureAwait(false);
            return response.Conflicts;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Array.Empty<Conflict>();
        }
    }

    /// <summary>Versionsverlauf eines Spiels (leer, wenn nicht eingerichtet/erreichbar).</summary>
    public async Task<IReadOnlyList<RevisionInfo>> GetRevisionsAsync(GameKey game, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(game);
        var api = _api;
        if (api is null)
            return Array.Empty<RevisionInfo>();
        try
        {
            var response = await api.GetRevisionsAsync(game, ct: ct).ConfigureAwait(false);
            return response.Revisions;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Array.Empty<RevisionInfo>();
        }
    }

    /// <summary>
    /// Löst einen Konflikt (Gewinner wählen oder beide behalten). Bei Annahme wird sofort
    /// ein Sync angestoßen, damit das Ergebnis lokal ankommt. Liefert <c>false</c>, wenn
    /// nicht eingerichtet, abgelehnt oder ein Fehler auftrat.
    /// </summary>
    public async Task<bool> ResolveConflictAsync(string conflictId, ResolveConflictRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(conflictId))
            return false;
        ArgumentNullException.ThrowIfNull(req);
        var api = _api;
        if (api is null)
            return false;
        try
        {
            var response = await api.ResolveConflictAsync(conflictId, req, ct).ConfigureAwait(false);
            if (response.Accepted)
                await SyncNowAsync(ct).ConfigureAwait(false);
            return response.Accepted;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Stellt eine ältere Revision eines Spiels auf diesem Gerät wieder her („Rückspiel").
    /// Reine Weiterleitung an die Server-API: der Server hinterlegt einen Restore-Befehl für
    /// dieses Gerät, der über den bestehenden Befehls-Poller / <see cref="SyncEngine.ApplyRevisionAsync"/>
    /// lokal angewandt wird – es entsteht <b>kein</b> neuer Schreibpfad. Bei Annahme wird sofort
    /// ein Sync angestoßen, damit das Ergebnis lokal ankommt. Liefert <c>false</c>, wenn nicht
    /// eingerichtet, abgelehnt oder ein Fehler auftrat – nie eine Exception in die GUI.
    /// </summary>
    public async Task<bool> RestoreAsync(GameKey game, long targetRevision, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(game);
        var api = _api;
        if (api is null)
            return false;
        var deviceId = CurrentDeviceId;
        if (string.IsNullOrWhiteSpace(deviceId))
            return false;
        try
        {
            var request = new RestoreRequest(deviceId, targetRevision);
            var response = await api.RestoreAsync(game, request, ct).ConfigureAwait(false);
            if (response.Accepted)
                await SyncNowAsync(ct).ConfigureAwait(false);
            return response.Accepted;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Geräte-ID dieses Geräts (aus der lokalen Konfiguration) oder <c>null</c>.</summary>
    public string? CurrentDeviceId => _configStore.Load().DeviceId;

    /// <summary>Anzeigename dieses Geräts (aus der lokalen Konfiguration) oder <c>null</c>.</summary>
    public string? CurrentDeviceName => _configStore.Load().DeviceName;

    // --- interne Abläufe -----------------------------------------------------------

    private async Task RescanAllAsync(CancellationToken ct)
    {
        foreach (var entry in _registry.GetAll())
        {
            ct.ThrowIfCancellationRequested();
            await SyncGameSafeAsync(entry.Game, entry.FolderPath, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Führt einen Sync-Zyklus pro Spiel exklusiv aus – über <b>dasselbe</b> Gate wie die
    /// Befehls-Anwendung im <see cref="CommandPoller"/> (B1), damit Rescan/Watcher und ein
    /// Restore/Resolve desselben Spiels nie gleichzeitig in den Ordner schreiben. Das Gate
    /// wird nur hier (äußerste Ebene) genommen – <see cref="SyncEngine.ApplyRevisionAsync"/>
    /// nimmt es nicht, sonst würde der Download-Fall es rekursiv anfordern (Deadlock).
    /// </summary>
    private async Task SyncGameSafeAsync(GameKey game, string folder, CancellationToken ct)
    {
        // Einziger Choke-Point für ALLE automatischen und manuellen Sync-Pfade (periodischer
        // Rescan über RescanAllAsync, FolderWatcher-Trigger, „Jetzt synchronisieren" via
        // SyncNowAsync→RescanAllAsync und die Direktaufrufe aus AddManualFolder/IncludeGame):
        // ist das Spiel ausgeschlossen, wird hier früh ausgestiegen – es wird nie gesynct.
        if (_exclusions.IsExcluded(game))
            return;

        var engine = _engine;
        if (engine is null)
            return;

        try
        {
            await _serializer.RunExclusiveAsync(game, c => engine.RunCycleAsync(game, folder, c), ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // erwarteter Abbruch beim Stoppen
        }
        catch (Exception ex)
        {
            State.SetStatus(game, SyncStatus.Error, action: "Sync-Fehler: " + ex.Message, folder: folder);
        }
    }

    private void StartWatchers(CancellationToken token)
    {
        foreach (var entry in _registry.GetAll())
            AddWatcher(entry, token);
    }

    private void AddWatcher(SaveFolderEntry entry, CancellationToken token)
    {
        lock (_lifecycleLock)
        {
            // Doppelte Watcher desselben Ordners vermeiden.
            if (_watchers.Any(w => string.Equals(w.Folder, Path.GetFullPath(entry.FolderPath), StringComparison.OrdinalIgnoreCase)))
                return;

            var watcher = new FolderWatcher(entry.FolderPath, _debounce);
            var game = entry.Game;
            var folder = entry.FolderPath;
            watcher.Changed += changedFolder => { _ = SyncGameSafeAsync(game, folder, token); };
            _watchers.Add(watcher);
        }
    }

    private void DisposeWatchers()
    {
        lock (_lifecycleLock)
        {
            foreach (var w in _watchers)
                w.Dispose();
            _watchers.Clear();
        }
    }

    /// <summary>
    /// Führt <paramref name="body"/> periodisch aus, bis Abbruch. Fehler einer einzelnen
    /// Iteration werden geschluckt, damit die Schleife nie stirbt.
    /// </summary>
    private static async Task RunLoopAsync(TimeSpan interval, Func<CancellationToken, Task> body, CancellationToken ct, bool runImmediately)
    {
        try
        {
            if (runImmediately)
                await SafeInvoke(body, ct).ConfigureAwait(false);

            using var timer = new PeriodicTimer(interval);
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
                await SafeInvoke(body, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // normaler Abbruch beim Stoppen
        }
    }

    private static async Task SafeInvoke(Func<CancellationToken, Task> body, CancellationToken ct)
    {
        try
        {
            await body(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // Iterationsfehler bewusst schlucken – Detail landet bereits in AgentState.
        }
    }

    private static async Task WhenAllQuiet(params Task?[] tasks)
    {
        foreach (var t in tasks)
        {
            if (t is null)
                continue;
            try { await t.ConfigureAwait(false); }
            catch { /* Abbruchfehler ignorieren */ }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _serializer.Dispose();
    }
}
