using System.Security.Cryptography;
using System.Text.Json;
using SaveVault.Core.Api;
using SaveVault.Core.Models;
using SaveVault.Core.Serialization;
using SaveVault.Core.Storage;
using SaveVault.Server.Security;

namespace SaveVault.Server.Storage;

/// <summary>
/// Die serverseitige „Wahrheit": Speicher + Versions-Historie + Geräte/Token + Konflikte +
/// Befehls-Warteschlange. Kapselt die gesamte Persistenz.
///
/// Nebenläufigkeit: alle Index-Änderungen laufen unter EINEM <see cref="SemaphoreSlim"/>
/// serialisiert; der Index wird als Ganzes atomar zurückgeschrieben (siehe <see cref="AtomicJson"/>).
/// Datei-Inhalte werden inhaltsadressiert (nach SHA-256) und traversal-frei über
/// <see cref="StoragePaths"/>/<see cref="PathSanitizer"/> abgelegt – nie ein roher Client-String
/// als Pfad. Content-Uploads sind idempotent und laufen außerhalb des Index-Locks.
/// </summary>
public sealed class VaultStore
{
    private const int MaxActivityEntries = 500;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly StoragePaths _paths;
    private readonly string _indexPath;
    private readonly JsonSerializerOptions _json = SaveVaultJson.Options;
    private readonly ILogger<VaultStore> _logger;

    private ServerIndex _index;

    public VaultStore(string dataRoot, ILogger<VaultStore> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _paths = new StoragePaths(dataRoot);
        Directory.CreateDirectory(_paths.DataRoot);
        _indexPath = Path.Combine(_paths.DataRoot, "index.json");

        _index = AtomicJson.ReadOrDefault(_indexPath, _json, () => new ServerIndex());

        // Beim ersten Start einen Pairing-Code bereitstellen (fürs Dashboard sichtbar).
        if (string.IsNullOrWhiteSpace(_index.PairingCode))
        {
            _index.PairingCode = Secrets.NewPairingCode();
            _index.PairingCodeUpdatedUtc = DateTime.UtcNow;
            Save();
        }
    }

    public string DataRoot => _paths.DataRoot;

    // =============================================================================
    // Auth / Pairing
    // =============================================================================

    /// <summary>Prüft einen Geräte-Token gegen die bekannten Geräte (konstant-zeitig via Hash).</summary>
    public async Task<AuthPrincipal?> ResolveDeviceTokenAsync(string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var hash = Secrets.HashToken(token);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            foreach (var d in _index.Devices)
            {
                if (!string.IsNullOrEmpty(d.TokenHash) && Secrets.FixedTimeEquals(d.TokenHash, hash))
                    return AuthPrincipal.ForDevice(d.Id);
            }
            return null;
        }
        finally { _gate.Release(); }
    }

    /// <summary>Aktueller Pairing-Code + Änderungszeit.</summary>
    public async Task<(string Code, DateTime UpdatedUtc)> GetPairingCodeAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (string.IsNullOrWhiteSpace(_index.PairingCode))
            {
                _index.PairingCode = Secrets.NewPairingCode();
                _index.PairingCodeUpdatedUtc = DateTime.UtcNow;
                Save();
            }
            return (_index.PairingCode!, _index.PairingCodeUpdatedUtc);
        }
        finally { _gate.Release(); }
    }

    /// <summary>Erzeugt einen neuen Pairing-Code („Erneuern"); der alte gilt danach nicht mehr.</summary>
    public async Task<(string Code, DateTime UpdatedUtc)> RegeneratePairingCodeAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _index.PairingCode = Secrets.NewPairingCode();
            _index.PairingCodeUpdatedUtc = DateTime.UtcNow;
            Save();
            return (_index.PairingCode!, _index.PairingCodeUpdatedUtc);
        }
        finally { _gate.Release(); }
    }

    /// <summary>Löst einen Pairing-Code ein: legt ein Gerät an und gibt einen frischen Token zurück.</summary>
    public async Task<PairResponse> PairAsync(PairRequest req, CancellationToken ct)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.Code))
            throw new VaultException(400, "Pairing-Code fehlt.");

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var current = _index.PairingCode ?? string.Empty;
            var provided = req.Code.Trim();
            // Vergleich case-insensitiv (Code wird lesbar dargestellt), konstant-zeitig.
            if (!Secrets.FixedTimeEquals(current.ToUpperInvariant(), provided.ToUpperInvariant()))
                throw new VaultException(401, "Ungültiger Pairing-Code.");

            var token = Secrets.NewDeviceToken();
            var device = new DeviceRecord
            {
                Id = Secrets.NewId(),
                Name = Clip(req.DeviceName, "Unbenanntes Gerät", 120),
                Os = Clip(req.Os, "unbekannt", 60),
                AgentVersion = Clip(req.AgentVersion, "?", 40),
                LastSeenUtc = DateTime.UtcNow,
                PairedUtc = DateTime.UtcNow,
                TokenHash = Secrets.HashToken(token),
            };
            _index.Devices.Add(device);
            AddActivity(new ActivityEntry
            {
                Id = Secrets.NewId(),
                TimestampUtc = DateTime.UtcNow,
                Action = "pair",
                DeviceId = device.Id,
                DeviceName = device.Name,
                Detail = "Gerät gekoppelt",
            });
            Save();

            return new PairResponse(device.Id, token);
        }
        finally { _gate.Release(); }
    }

    // =============================================================================
    // Heartbeat / Geräte
    // =============================================================================

    public async Task<HeartbeatResponse> HeartbeatAsync(HeartbeatRequest req, CancellationToken ct)
    {
        if (req?.Device is null)
            throw new VaultException(400, "Heartbeat ohne Geräteangabe.");

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var device = _index.Devices.FirstOrDefault(d => d.Id == req.Device.Id)
                ?? throw new VaultException(404, "Unbekanntes Gerät – bitte erneut koppeln.");

            device.Name = Clip(req.Device.Name, device.Name, 120);
            device.Os = Clip(req.Device.Os, device.Os, 60);
            device.AgentVersion = Clip(req.Device.AgentVersion, device.AgentVersion, 40);
            device.LastSeenUtc = DateTime.UtcNow;

            foreach (var gs in req.GameStates ?? Array.Empty<DeviceGameState>())
            {
                if (gs?.Game is null) continue;
                SetDeviceGameState(device.Id, gs.Game.Value, gs.BaseRevision, gs.Status);
            }

            var pending = _index.Commands.Count(c => c.TargetDeviceId == device.Id);
            Save();
            return new HeartbeatResponse(DateTime.UtcNow, pending);
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<DeviceInfo>> ListDevicesAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return _index.Devices
                .Select(d => new DeviceInfo(d.Id, d.Name, d.Os, d.AgentVersion, d.LastSeenUtc))
                .ToList();
        }
        finally { _gate.Release(); }
    }

    // =============================================================================
    // Spiele / Revisionen
    // =============================================================================

    public async Task<GamesResponse> GetGamesAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var list = new List<GameSummary>(_index.Games.Count);
            foreach (var g in _index.Games)
            {
                list.Add(new GameSummary(
                    ToGameKey(g),
                    g.CurrentRevision,
                    ComputeGameStatus(g),
                    g.CurrentFileCount,
                    g.CurrentTotalBytes));
            }
            return new GamesResponse(list);
        }
        finally { _gate.Release(); }
    }

    public async Task<RevisionHead> GetHeadAsync(GameKey game, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var g = FindGame(game.Value);
            return new RevisionHead(g is null ? game : ToGameKey(g), g?.CurrentRevision ?? 0);
        }
        finally { _gate.Release(); }
    }

    public async Task<RevisionListResponse> GetRevisionsAsync(GameKey game, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var g = FindGame(game.Value);
            if (g is null)
                return new RevisionListResponse(game, Array.Empty<RevisionInfo>());

            var infos = new List<RevisionInfo>();
            for (var n = g.LastRevisionNumber; n >= 1; n--)
            {
                var rev = LoadRevision(g, n);
                if (rev is null) continue;
                infos.Add(new RevisionInfo(
                    rev.Number, rev.DeviceId, rev.TimestampUtc,
                    rev.Manifest.TotalBytes, rev.Manifest.FileCount, rev.Manifest.ManifestHash,
                    rev.IsConflict, rev.BasedOnRevision));
            }
            return new RevisionListResponse(ToGameKey(g), infos);
        }
        finally { _gate.Release(); }
    }

    public async Task<RevisionDownload> GetRevisionAsync(GameKey game, long revision, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var g = FindGame(game.Value)
                ?? throw new VaultException(404, "Unbekanntes Spiel.");
            var rev = LoadRevision(g, revision)
                ?? throw new VaultException(404, $"Revision {revision} nicht gefunden.");
            return new RevisionDownload(rev.Number, ToGameKey(g), rev.DeviceId, rev.TimestampUtc, rev.Manifest);
        }
        finally { _gate.Release(); }
    }

    public async Task<UploadRevisionResponse> RegisterRevisionAsync(
        GameKey routeGame, UploadRevisionRequest req, CancellationToken ct)
    {
        if (req?.Manifest is null || req.Device is null)
            throw new VaultException(400, "Unvollständige Revisionsanmeldung.");

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var g = GetOrCreateGame(routeGame);
            var cur = g.CurrentRevision;

            if (!req.IsConflict && req.BasedOnRevision.HasValue && req.BasedOnRevision.Value != cur)
            {
                throw new VaultException(409,
                    $"Veraltete Basis-Revision: der Server steht bereits auf Revision {cur}. Bitte erneut abgleichen.");
            }

            // Upload-Gerät als bekannt führen (Selbstauskunft aktualisieren, falls vorhanden).
            TouchDevice(req.Device);

            var number = g.LastRevisionNumber + 1;
            var rev = new Revision(
                number, ToGameKey(g), req.Device.Id, DateTime.UtcNow,
                req.Manifest, req.IsConflict, req.BasedOnRevision);
            WriteRevision(g, rev);
            g.LastRevisionNumber = number;

            if (req.IsConflict)
            {
                RegisterConflict(g, req.Device.Id, number, cur);
                SetDeviceGameState(req.Device.Id, g.KeyValue, cur, SyncStatus.Conflict);
                AddActivity(new ActivityEntry
                {
                    Id = Secrets.NewId(),
                    TimestampUtc = DateTime.UtcNow,
                    Action = "conflict",
                    GameKeyValue = g.KeyValue,
                    GameDisplayName = g.DisplayName,
                    DeviceId = req.Device.Id,
                    DeviceName = req.Device.Name,
                    Revision = number,
                    Bytes = req.Manifest.TotalBytes,
                    FileCount = req.Manifest.FileCount,
                    Detail = "Konflikt-Revision hochgeladen (nichts überschrieben)",
                });
            }
            else
            {
                g.CurrentRevision = number;
                g.CurrentFileCount = req.Manifest.FileCount;
                g.CurrentTotalBytes = req.Manifest.TotalBytes;
                SetDeviceGameState(req.Device.Id, g.KeyValue, number, SyncStatus.Synced);
                AddActivity(new ActivityEntry
                {
                    Id = Secrets.NewId(),
                    TimestampUtc = DateTime.UtcNow,
                    Action = "upload",
                    GameKeyValue = g.KeyValue,
                    GameDisplayName = g.DisplayName,
                    DeviceId = req.Device.Id,
                    DeviceName = req.Device.Name,
                    Revision = number,
                    Bytes = req.Manifest.TotalBytes,
                    FileCount = req.Manifest.FileCount,
                    Detail = "Neue Version hochgeladen",
                });
            }

            var gameKey = ToGameKey(g);
            var missing = req.Manifest.Entries
                .Select(e => e.Sha256)
                .Where(sha => !ContentExists(gameKey, sha))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            Save();
            return new UploadRevisionResponse(number, missing);
        }
        finally { _gate.Release(); }
    }

    // =============================================================================
    // Inhalte (inhaltsadressiert) – außerhalb des Index-Locks, idempotent
    // =============================================================================

    /// <summary>Ob der Inhalt zu einem Hash bereits gespeichert ist.</summary>
    public bool ContentExists(GameKey game, string sha256)
    {
        if (!IsValidSha256(sha256)) return false;
        return File.Exists(_paths.ContentFile(game, sha256));
    }

    /// <summary>
    /// Speichert einen hochgeladenen Datei-Inhalt. Der Inhalt wird beim Streamen mitgehasht und
    /// muss dem angegebenen SHA-256 entsprechen (sonst 400) – so kann ein Client keinen falschen
    /// Inhalt unter fremdem Hash unterschieben. Idempotent: existiert der Inhalt schon, No-Op.
    /// </summary>
    public async Task StoreContentAsync(GameKey game, string sha256, Stream body, CancellationToken ct)
    {
        if (!IsValidSha256(sha256))
            throw new VaultException(400, "Ungültiger Inhalts-Hash.");

        var target = _paths.ContentFile(game, sha256);
        if (File.Exists(target))
            return; // schon vorhanden – nichts zu tun

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var tmp = target + ".tmp-" + Guid.NewGuid().ToString("N");

        try
        {
            string computed;
            await using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var sha = SHA256.Create())
            await using (var crypto = new CryptoStream(fs, sha, CryptoStreamMode.Write))
            {
                await body.CopyToAsync(crypto, ct).ConfigureAwait(false);
                await crypto.FlushFinalBlockAsync(ct).ConfigureAwait(false);
                computed = Convert.ToHexStringLower(sha.Hash!);
            }

            if (!string.Equals(computed, sha256.ToLowerInvariant(), StringComparison.Ordinal))
            {
                TryDelete(tmp);
                throw new VaultException(400, "Inhalt entspricht nicht dem angegebenen Hash.");
            }

            try
            {
                File.Move(tmp, target, overwrite: true);
            }
            catch (IOException) when (File.Exists(target))
            {
                // Ein paralleler Upload desselben Hashes war schneller – Inhalt ist da.
                TryDelete(tmp);
            }
        }
        catch
        {
            TryDelete(tmp);
            throw;
        }
    }

    /// <summary>Öffnet den Inhalt zu einem Hash zum Lesen; null, wenn nicht vorhanden.</summary>
    public Stream? OpenContent(GameKey game, string sha256)
    {
        if (!IsValidSha256(sha256)) return null;
        var path = _paths.ContentFile(game, sha256);
        if (!File.Exists(path)) return null;
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    // =============================================================================
    // Konflikte
    // =============================================================================

    public async Task<ConflictListResponse> GetConflictsAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var open = _index.Conflicts.Where(c => !c.Resolved).ToList();
            return new ConflictListResponse(open);
        }
        finally { _gate.Release(); }
    }

    public async Task<ResolveConflictResponse> ResolveConflictAsync(
        string conflictId, ResolveConflictRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(conflictId) || req is null)
            throw new VaultException(400, "Unvollständige Konfliktlösung.");

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var idx = _index.Conflicts.FindIndex(c => c.Id == conflictId && !c.Resolved);
            if (idx < 0)
                throw new VaultException(404, "Kein offener Konflikt mit dieser ID.");

            var conflict = _index.Conflicts[idx];
            var g = FindGame(conflict.Game.Value)
                ?? throw new VaultException(404, "Spiel des Konflikts nicht mehr vorhanden.");

            if (req.Resolution == ConflictResolutionKind.KeepDevice)
                ResolveKeepDevice(g, conflict, req);
            else
                ResolveKeepBoth(g, conflict);

            _index.Conflicts[idx] = conflict with { Resolved = true };
            Save();
            return new ResolveConflictResponse(true);
        }
        finally { _gate.Release(); }
    }

    private void ResolveKeepDevice(GameRecord g, Conflict conflict, ResolveConflictRequest req)
    {
        // Gewinner bestimmen: bevorzugt explizite Angabe, sonst der beteiligte Nicht-Head-Stand.
        long winnerRev;
        string winnerDevice;
        if (req.WinningRevision.HasValue)
        {
            winnerRev = req.WinningRevision.Value;
            winnerDevice = req.WinningDeviceId
                ?? conflict.Participants.FirstOrDefault(p => p.Revision == winnerRev)?.DeviceId
                ?? "";
        }
        else if (!string.IsNullOrWhiteSpace(req.WinningDeviceId))
        {
            var p = conflict.Participants.FirstOrDefault(x => x.DeviceId == req.WinningDeviceId)
                ?? throw new VaultException(400, "Gewinner-Gerät gehört nicht zum Konflikt.");
            winnerRev = p.Revision;
            winnerDevice = p.DeviceId;
        }
        else
        {
            throw new VaultException(400, "Für „Gerät behalten“ fehlt der Gewinner.");
        }

        var winning = LoadRevision(g, winnerRev)
            ?? throw new VaultException(404, $"Gewinner-Revision {winnerRev} nicht gefunden.");

        // Neue Head-Revision anlegen, die das Gewinner-Manifest übernimmt (Historie bleibt,
        // Monotonie bleibt). Inhalte liegen bereits vor (beide Stände sind gespeichert).
        var number = g.LastRevisionNumber + 1;
        var newRev = new Revision(
            number, ToGameKey(g), winnerDevice, DateTime.UtcNow,
            winning.Manifest, IsConflict: false, BasedOnRevision: g.CurrentRevision);
        WriteRevision(g, newRev);
        g.LastRevisionNumber = number;
        g.CurrentRevision = number;
        g.CurrentFileCount = winning.Manifest.FileCount;
        g.CurrentTotalBytes = winning.Manifest.TotalBytes;

        // Allen beteiligten Geräten (außer dem Gewinner) den Download der Lösung als Befehl geben.
        foreach (var p in conflict.Participants)
        {
            if (p.DeviceId == winnerDevice) continue;
            EnqueueCommand(new Command(
                Secrets.NewId(), CommandType.ApplyResolution, p.DeviceId, ToGameKey(g),
                DateTime.UtcNow, TargetRevision: number, Resolution: ConflictResolutionKind.KeepDevice,
                ConflictId: conflict.Id));
            SetDeviceGameState(p.DeviceId, g.KeyValue, g.CurrentRevision, SyncStatus.Pending);
        }
        SetDeviceGameState(winnerDevice, g.KeyValue, number, SyncStatus.Synced);

        AddActivity(new ActivityEntry
        {
            Id = Secrets.NewId(),
            TimestampUtc = DateTime.UtcNow,
            Action = "resolve",
            GameKeyValue = g.KeyValue,
            GameDisplayName = g.DisplayName,
            DeviceId = winnerDevice,
            Revision = number,
            Bytes = winning.Manifest.TotalBytes,
            FileCount = winning.Manifest.FileCount,
            Detail = "Konflikt gelöst: Gerät behalten",
        });
    }

    private void ResolveKeepBoth(GameRecord g, Conflict conflict)
    {
        // Head-Seite bleibt unverändert die aktuelle Revision. Die andere (Verlierer-)Fassung
        // wird als umbenanntes ZWEITES Save-Set abgelegt – nichts wird gelöscht.
        var forkParticipant = conflict.Participants.FirstOrDefault(p => p.Revision != g.CurrentRevision)
            ?? conflict.Participants.OrderBy(p => p.Revision).FirstOrDefault();
        if (forkParticipant is null)
            throw new VaultException(400, "Konflikt ohne Beteiligte – nichts zu behalten.");

        var loser = LoadRevision(g, forkParticipant.Revision)
            ?? throw new VaultException(404, $"Verlierer-Revision {forkParticipant.Revision} nicht gefunden.");

        var loserDevice = _index.Devices.FirstOrDefault(d => d.Id == forkParticipant.DeviceId);
        var loserName = loserDevice?.Name ?? forkParticipant.DeviceId;

        var forkValue = $"{g.KeyValue}#conflict-{forkParticipant.Revision}";
        var forkDisplay = $"{g.DisplayName} (Konflikt {loserName})";
        var forkKey = new GameKey(forkValue, forkDisplay);

        var fork = FindGame(forkValue);
        if (fork is null)
        {
            fork = new GameRecord
            {
                KeyValue = forkValue,
                DisplayName = forkDisplay,
                IsFork = true,
            };
            _index.Games.Add(fork);
        }

        // Inhalte des Verlierer-Manifests in den neuen Bucket kopieren (inhaltsadressiert).
        foreach (var entry in loser.Manifest.Entries)
        {
            var src = _paths.ContentFile(ToGameKey(g), entry.Sha256);
            var dst = _paths.ContentFile(forkKey, entry.Sha256);
            if (File.Exists(src) && !File.Exists(dst))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                try { File.Copy(src, dst); }
                catch (IOException) when (File.Exists(dst)) { /* parallel schon da */ }
            }
        }

        var number = fork.LastRevisionNumber + 1;
        var forkRev = new Revision(
            number, forkKey, forkParticipant.DeviceId, DateTime.UtcNow,
            loser.Manifest, IsConflict: false, BasedOnRevision: null);
        WriteRevision(fork, forkRev);
        fork.LastRevisionNumber = number;
        fork.CurrentRevision = number;
        fork.CurrentFileCount = loser.Manifest.FileCount;
        fork.CurrentTotalBytes = loser.Manifest.TotalBytes;

        // Beteiligte auf dem Originalspiel wieder als „steht"/„nachzuziehen" markieren.
        foreach (var p in conflict.Participants)
        {
            var status = p.Revision == g.CurrentRevision ? SyncStatus.Synced : SyncStatus.Pending;
            SetDeviceGameState(p.DeviceId, g.KeyValue, g.CurrentRevision, status);
        }

        AddActivity(new ActivityEntry
        {
            Id = Secrets.NewId(),
            TimestampUtc = DateTime.UtcNow,
            Action = "resolve",
            GameKeyValue = g.KeyValue,
            GameDisplayName = g.DisplayName,
            DeviceId = forkParticipant.DeviceId,
            Revision = forkParticipant.Revision,
            Bytes = loser.Manifest.TotalBytes,
            FileCount = loser.Manifest.FileCount,
            Detail = $"Konflikt gelöst: beide behalten → „{forkDisplay}\"",
        });
    }

    // =============================================================================
    // Restore + Befehls-Warteschlange
    // =============================================================================

    public async Task<RestoreResponse> RestoreAsync(GameKey game, RestoreRequest req, CancellationToken ct)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.TargetDeviceId))
            throw new VaultException(400, "Unvollständige Wiederherstellungs-Anforderung.");

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var g = FindGame(game.Value)
                ?? throw new VaultException(404, "Unbekanntes Spiel.");
            if (LoadRevision(g, req.TargetRevision) is null)
                throw new VaultException(404, $"Revision {req.TargetRevision} nicht gefunden.");
            if (_index.Devices.All(d => d.Id != req.TargetDeviceId))
                throw new VaultException(404, "Zielgerät unbekannt.");

            EnqueueCommand(new Command(
                Secrets.NewId(), CommandType.Restore, req.TargetDeviceId, ToGameKey(g),
                DateTime.UtcNow, TargetRevision: req.TargetRevision));
            SetDeviceGameState(req.TargetDeviceId, g.KeyValue, g.CurrentRevision, SyncStatus.Pending);

            AddActivity(new ActivityEntry
            {
                Id = Secrets.NewId(),
                TimestampUtc = DateTime.UtcNow,
                Action = "restore",
                GameKeyValue = g.KeyValue,
                GameDisplayName = g.DisplayName,
                DeviceId = req.TargetDeviceId,
                Revision = req.TargetRevision,
                Detail = "Wiederherstellung angefordert",
            });
            Save();
            return new RestoreResponse(true);
        }
        finally { _gate.Release(); }
    }

    public async Task<CommandListResponse> GetCommandsAsync(string deviceId, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var list = _index.Commands.Where(c => c.TargetDeviceId == deviceId).ToList();
            return new CommandListResponse(list);
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Bestätigt (entfernt) einen Befehl. Nur das ausführende Gerät (oder das Master-Token) darf
    /// das – sonst 403; unbekannte ID → 404. So kann kein Gerät fremde Befehle abräumen.
    /// </summary>
    public async Task<AckResponse> AckCommandAsync(string commandId, AuthPrincipal principal, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(commandId))
            throw new VaultException(400, "Befehls-ID fehlt.");

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var cmd = _index.Commands.FirstOrDefault(c => c.Id == commandId)
                ?? throw new VaultException(404, "Befehl nicht gefunden.");
            if (!principal.CanActAsDevice(cmd.TargetDeviceId))
                throw new VaultException(403, "Befehl gehört einem anderen Gerät.");

            _index.Commands.RemoveAll(c => c.Id == commandId);
            Save();
            return new AckResponse(true);
        }
        finally { _gate.Release(); }
    }

    // =============================================================================
    // Dashboard-Zusatz
    // =============================================================================

    public async Task<IReadOnlyList<ActivityEntry>> GetActivityAsync(int limit, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return _index.Activity
                .OrderByDescending(a => a.TimestampUtc)
                .Take(limit is > 0 and <= MaxActivityEntries ? limit : 100)
                .ToList();
        }
        finally { _gate.Release(); }
    }

    // =============================================================================
    // Interne Helfer (nur unter Lock aufrufen, wo sie den Index anfassen)
    // =============================================================================

    private GameRecord? FindGame(string keyValue)
        => _index.Games.FirstOrDefault(g => g.KeyValue == keyValue);

    private GameRecord GetOrCreateGame(GameKey game)
    {
        var g = FindGame(game.Value);
        if (g is not null) return g;

        g = new GameRecord
        {
            KeyValue = game.Value,
            DisplayName = game.DisplayName,
            Store = game.Store,
            StoreId = game.StoreId,
        };
        _index.Games.Add(g);
        return g;
    }

    private static GameKey ToGameKey(GameRecord g)
        => new(g.KeyValue, string.IsNullOrWhiteSpace(g.DisplayName) ? g.KeyValue : g.DisplayName, g.Store, g.StoreId);

    private SyncStatus ComputeGameStatus(GameRecord g)
    {
        if (_index.Conflicts.Any(c => !c.Resolved && c.Game.Value == g.KeyValue))
            return SyncStatus.Conflict;

        var states = _index.GameStates.Where(s => s.GameKeyValue == g.KeyValue).ToList();
        if (states.Count > 0 && states.Any(s => s.BaseRevision < g.CurrentRevision))
            return SyncStatus.Pending;

        return SyncStatus.Synced;
    }

    private void SetDeviceGameState(string deviceId, string gameKeyValue, long baseRevision, SyncStatus status)
    {
        var s = _index.GameStates.FirstOrDefault(x => x.DeviceId == deviceId && x.GameKeyValue == gameKeyValue);
        if (s is null)
        {
            s = new DeviceGameStateRecord { DeviceId = deviceId, GameKeyValue = gameKeyValue };
            _index.GameStates.Add(s);
        }
        s.BaseRevision = baseRevision;
        s.Status = status;
        s.UpdatedUtc = DateTime.UtcNow;
    }

    private void TouchDevice(DeviceInfo info)
    {
        var d = _index.Devices.FirstOrDefault(x => x.Id == info.Id);
        if (d is null) return; // unbekannte Geräte werden nicht implizit angelegt (nur via Pairing)
        d.Name = Clip(info.Name, d.Name, 120);
        d.Os = Clip(info.Os, d.Os, 60);
        d.AgentVersion = Clip(info.AgentVersion, d.AgentVersion, 40);
        d.LastSeenUtc = DateTime.UtcNow;
    }

    private void RegisterConflict(GameRecord g, string deviceId, long conflictRevision, long currentRevision)
    {
        var existing = _index.Conflicts.FirstOrDefault(c => !c.Resolved && c.Game.Value == g.KeyValue);
        if (existing is not null)
        {
            if (existing.Participants.All(p => p.DeviceId != deviceId))
            {
                var parts = existing.Participants.ToList();
                parts.Add(new ConflictParticipant(deviceId, conflictRevision));
                var i = _index.Conflicts.IndexOf(existing);
                _index.Conflicts[i] = existing with { Participants = parts };
            }
            return;
        }

        var participants = new List<ConflictParticipant> { new(deviceId, conflictRevision) };
        if (currentRevision > 0)
        {
            var head = LoadRevision(g, currentRevision);
            if (head is not null && head.DeviceId != deviceId)
                participants.Add(new ConflictParticipant(head.DeviceId, currentRevision));
        }
        _index.Conflicts.Add(new Conflict(
            Secrets.NewId(), ToGameKey(g), participants, DateTime.UtcNow));
    }

    private void EnqueueCommand(Command cmd) => _index.Commands.Add(cmd);

    private void AddActivity(ActivityEntry entry)
    {
        _index.Activity.Add(entry);
        if (_index.Activity.Count > MaxActivityEntries)
        {
            _index.Activity = _index.Activity
                .OrderByDescending(a => a.TimestampUtc)
                .Take(MaxActivityEntries)
                .ToList();
        }
    }

    private Revision? LoadRevision(GameRecord g, long number)
    {
        if (number < 1) return null;
        var path = Path.Combine(_paths.RevisionDirectory(ToGameKey(g), number), "revision.json");
        return AtomicJson.ReadOrDefault<Revision?>(path, _json, () => null);
    }

    private void WriteRevision(GameRecord g, Revision rev)
    {
        var dir = _paths.RevisionDirectory(ToGameKey(g), rev.Number);
        Directory.CreateDirectory(dir);
        AtomicJson.Write(Path.Combine(dir, "revision.json"), rev, _json);
    }

    private void Save() => AtomicJson.Write(_indexPath, _index, _json);

    private static bool IsValidSha256(string? s)
        => !string.IsNullOrEmpty(s) && s.Length == 64 && s.All(Uri.IsHexDigit);

    private static string Clip(string? value, string fallback, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        var v = value.Trim();
        return v.Length > max ? v[..max] : v;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best effort */ }
    }
}
