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

    // Einfaches Brute-Force-Limit fürs Pairing: mehr als so viele Fehlversuche in einem
    // gleitenden Zeitfenster sperren das Einlösen vorübergehend (429). Läuft im Speicher und
    // ohne den Index-Lock zu blockieren (keine künstliche Verzögerung unter Lock → kein DoS).
    private const int MaxPairFailures = 10;
    private static readonly TimeSpan PairFailureWindow = TimeSpan.FromMinutes(5);

    // Anmelde-Bremse (Passwort-Raten) analog zum Pairing, plus die Session-Lebensdauer.
    private const int MaxLoginFailures = 10;
    private static readonly TimeSpan LoginFailureWindow = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(30);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly StoragePaths _paths;
    private readonly string _indexPath;
    private readonly JsonSerializerOptions _json = SaveVaultJson.Options;
    private readonly ILogger<VaultStore> _logger;

    private ServerIndex _index;

    // Pairing-Fehlversuchszähler (gleitendes Fenster) – siehe MaxPairFailures.
    private int _pairFailureCount;
    private DateTime _pairFailureWindowStartUtc;

    // Anmelde-Fehlversuchszähler (gleitendes Fenster) – siehe MaxLoginFailures.
    private int _loginFailureCount;
    private DateTime _loginFailureWindowStartUtc;

    public VaultStore(string dataRoot, ILogger<VaultStore> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _paths = new StoragePaths(dataRoot);
        Directory.CreateDirectory(_paths.DataRoot);
        _indexPath = Path.Combine(_paths.DataRoot, "index.json");

        _index = AtomicJson.ReadOrDefault(_indexPath, _json, () => new ServerIndex());

        MigrateIfNeeded();

        // Beim ersten Start einen Pairing-Code bereitstellen (fürs Dashboard sichtbar).
        if (string.IsNullOrWhiteSpace(_index.PairingCode))
        {
            _index.PairingCode = Secrets.NewPairingCode();
            _index.PairingCodeUpdatedUtc = DateTime.UtcNow;
            Save();
        }
    }

    /// <summary>Aktuelle Schema-Version des Index (siehe <see cref="MigrateIfNeeded"/>).</summary>
    private const int CurrentIndexVersion = 2;

    /// <summary>
    /// Einmalige, idempotente Migration beim Start. Version 1→2 (Umstieg auf geräte-eigene Buckets,
    /// siehe <c>specs/savevault-change-per-device-sync.md</c>): die alten globalen Buckets werden
    /// eingefroren – kein Gerät synct mehr automatisch gegen sie (die Clients adressieren ab jetzt
    /// nur noch ihren privaten Bucket-Schlüssel). Damit der bisherige Konflikt-Sturm (Folge des
    /// gemeinsamen globalen Verlaufs) sofort verstummt, werden alle noch offenen Konflikte als gelöst
    /// markiert. Nichts wird gelöscht: die Konflikt-Revisionen und alle Blobs bleiben als lesbare
    /// Legacy-Historie erhalten.
    /// </summary>
    private void MigrateIfNeeded()
    {
        if (_index.Version >= CurrentIndexVersion)
            return;

        for (var i = 0; i < _index.Conflicts.Count; i++)
        {
            if (!_index.Conflicts[i].Resolved)
                _index.Conflicts[i] = _index.Conflicts[i] with { Resolved = true };
        }

        _index.Version = CurrentIndexVersion;
        Save();
    }

    public string DataRoot => _paths.DataRoot;

    // =============================================================================
    // Admin-Konto / Dashboard-Anmeldung (ersetzt das frühere Master-Token)
    // =============================================================================

    /// <summary>Ob bereits ein Admin-Konto eingerichtet ist (sonst: Ersteinrichtung nötig).</summary>
    public async Task<bool> HasAdminAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try { return _index.Admin is not null; }
        finally { _gate.Release(); }
    }

    /// <summary>Synchron nutzbare Variante für den Start (Log-Meldung); blockiert kurz das Gate.</summary>
    public bool HasAdmin
    {
        get
        {
            _gate.Wait();
            try { return _index.Admin is not null; }
            finally { _gate.Release(); }
        }
    }

    /// <summary>
    /// Richtet das (einzige) Admin-Konto ein und meldet direkt an (Session). Schlägt mit 409 fehl,
    /// wenn bereits ein Konto existiert – so kann die offene Ersteinrichtung nur EINMAL genutzt werden.
    /// </summary>
    public async Task<LoginResponse> SetupAdminAsync(string username, string password, CancellationToken ct)
    {
        ValidateCredentials(username, password);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_index.Admin is not null)
                throw new VaultException(409, "Der Server ist bereits eingerichtet.");

            _index.Admin = new AdminAccount
            {
                Username = username.Trim(),
                PasswordHash = Secrets.HashPassword(password),
                CreatedUtc = DateTime.UtcNow,
            };
            var response = CreateSessionLocked(_index.Admin.Username);
            Save();
            return response;
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Meldet mit Benutzername + Passwort an und gibt einen Session-Token zurück. Ratenbegrenzt
    /// (gleitendes Fehlversuchsfenster → 429); falsche Zugangsdaten → 401. Kein Konto → 401.
    /// </summary>
    public async Task<LoginResponse> LoginAsync(string username, string password, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
            throw new VaultException(400, "Benutzername und Passwort sind erforderlich.");

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var now = DateTime.UtcNow;
            if (now - _loginFailureWindowStartUtc > LoginFailureWindow)
            {
                _loginFailureWindowStartUtc = now;
                _loginFailureCount = 0;
            }
            if (_loginFailureCount >= MaxLoginFailures)
                throw new VaultException(429, "Zu viele Fehlversuche. Bitte später erneut versuchen.");

            var admin = _index.Admin;
            var ok = admin is not null
                     && string.Equals(admin.Username, username.Trim(), StringComparison.OrdinalIgnoreCase)
                     && Secrets.VerifyPassword(password, admin.PasswordHash);
            if (!ok)
            {
                _loginFailureCount++;
                throw new VaultException(401, "Benutzername oder Passwort ist falsch.");
            }

            _loginFailureCount = 0;
            var response = CreateSessionLocked(admin!.Username);
            Save();
            return response;
        }
        finally { _gate.Release(); }
    }

    /// <summary>Löst einen Session-Token zu einem Master-Prinzipal auf (oder null, wenn ungültig/abgelaufen).</summary>
    public async Task<AuthPrincipal?> ResolveSessionAsync(string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var hash = Secrets.HashToken(token);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var now = DateTime.UtcNow;
            var match = _index.Sessions.Any(s => s.ExpiresUtc > now && Secrets.FixedTimeEquals(s.TokenHash, hash));
            return match ? AuthPrincipal.Master : null;
        }
        finally { _gate.Release(); }
    }

    /// <summary>Beendet die zum Token gehörende Sitzung (idempotent).</summary>
    public async Task LogoutAsync(string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token)) return;
        var hash = Secrets.HashToken(token);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_index.Sessions.RemoveAll(s => Secrets.FixedTimeEquals(s.TokenHash, hash)) > 0)
                Save();
        }
        finally { _gate.Release(); }
    }

    /// <summary>Erzeugt eine neue Sitzung (nur unter <see cref="_gate"/> aufrufen).</summary>
    private LoginResponse CreateSessionLocked(string username)
    {
        var now = DateTime.UtcNow;
        _index.Sessions.RemoveAll(s => s.ExpiresUtc <= now); // abgelaufene aufräumen
        var token = Secrets.NewSessionToken();
        var expires = now.Add(SessionLifetime);
        _index.Sessions.Add(new SessionRecord { TokenHash = Secrets.HashToken(token), ExpiresUtc = expires });
        return new LoginResponse(token, expires, username);
    }

    private static void ValidateCredentials(string username, string password)
    {
        var user = username?.Trim() ?? string.Empty;
        if (user.Length < 3)
            throw new VaultException(400, "Der Benutzername muss mindestens 3 Zeichen haben.");
        if (user.Length > 60)
            throw new VaultException(400, "Der Benutzername ist zu lang (max. 60 Zeichen).");
        if (string.IsNullOrEmpty(password) || password.Length < 8)
            throw new VaultException(400, "Das Passwort muss mindestens 8 Zeichen haben.");
        if (password.Length > 200)
            throw new VaultException(400, "Das Passwort ist zu lang (max. 200 Zeichen).");
    }

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
            // Brute-Force-Schutz: gleitendes Fehlversuchsfenster zurücksetzen, wenn abgelaufen.
            var now = DateTime.UtcNow;
            if (now - _pairFailureWindowStartUtc > PairFailureWindow)
            {
                _pairFailureWindowStartUtc = now;
                _pairFailureCount = 0;
            }
            if (_pairFailureCount >= MaxPairFailures)
                throw new VaultException(429,
                    "Zu viele fehlgeschlagene Kopplungsversuche. Bitte später erneut versuchen.");

            var current = _index.PairingCode ?? string.Empty;
            var provided = req.Code.Trim();
            // Vergleich case-insensitiv (Code wird lesbar dargestellt), konstant-zeitig.
            if (string.IsNullOrEmpty(current)
                || !Secrets.FixedTimeEquals(current.ToUpperInvariant(), provided.ToUpperInvariant()))
            {
                _pairFailureCount++;
                throw new VaultException(401, "Ungültiger Pairing-Code.");
            }

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

            // Single-use: der eben eingelöste Code wird sofort durch einen frischen ersetzt, damit
            // derselbe Code nicht ein zweites Mal ein Gerät koppeln kann. Bereits gekoppelte Geräte
            // behalten ihren Token (nur der Pairing-Code wechselt). Der neue Code steht im Dashboard.
            _pairFailureCount = 0;
            _index.PairingCode = Secrets.NewPairingCode();
            _index.PairingCodeUpdatedUtc = DateTime.UtcNow;
            Save();

            return new PairResponse(device.Id, token);
        }
        finally { _gate.Release(); }
    }

    // =============================================================================
    // Heartbeat / Geräte
    // =============================================================================

    public async Task<HeartbeatResponse> HeartbeatAsync(HeartbeatRequest req, string? ipAddress, CancellationToken ct)
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

            // Serverseitig beobachtete Client-IP (nur fürs Dashboard). Leere/unbekannte Adresse
            // toleriert – der bisherige Wert bleibt dann erhalten.
            if (!string.IsNullOrWhiteSpace(ipAddress))
                device.LastIpAddress = Clip(ipAddress, device.LastIpAddress ?? string.Empty, 64);

            foreach (var gs in req.GameStates ?? Array.Empty<DeviceGameState>())
            {
                if (gs?.Game is null) continue;
                // Der Heartbeat trägt den kanonischen GameKey (echter Anzeigename + Store/StoreId).
                // Serverseitig gehört der Zustand zum PRIVATEN Bucket dieses Geräts (Per-Gerät-Backup):
                // auf den effektiven Bucket-Schlüssel abbilden, damit Anzeigename und Status am selben
                // Bucket hängen wie die von diesem Gerät hochgeladenen Revisionen. Der Owner kommt aus
                // dem (bereits token-geprüften) Gerät, nie aus Client-Eingaben.
                var bucketKey = BucketKey.Resolve(gs.Game, BucketScope.Private, device.Id);
                ApplyGameKeyMetadata(bucketKey);
                SetDeviceGameState(device.Id, bucketKey.Value, gs.BaseRevision, gs.Status);
            }

            var pending = _index.Commands.Count(c => c.TargetDeviceId == device.Id);
            Save();
            return new HeartbeatResponse(DateTime.UtcNow, pending);
        }
        finally { _gate.Release(); }
    }

    /// <summary>Anzeigename eines Geräts (oder null, wenn unbekannt) – z. B. für den Export.</summary>
    public async Task<string?> GetDeviceNameAsync(string deviceId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) return null;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return _index.Devices.FirstOrDefault(d => d.Id == deviceId)?.Name;
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<DeviceView>> ListDevicesAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Aktuelle Spielgrößen je Bucket vorab nachschlagen (spart wiederholtes Suchen).
            var bytesByGame = _index.Games.ToDictionary(g => g.KeyValue, g => g.CurrentTotalBytes, StringComparer.Ordinal);

            var list = new List<DeviceView>(_index.Devices.Count);
            foreach (var d in _index.Devices)
            {
                // Ein Gerät „hält" ein Spiel lokal, sobald es dafür eine Basis-Revision > 0 meldet.
                long storageBytes = 0;
                var gameCount = 0;
                foreach (var s in _index.GameStates)
                {
                    if (s.DeviceId != d.Id || s.BaseRevision <= 0) continue;
                    gameCount++;
                    if (bytesByGame.TryGetValue(s.GameKeyValue, out var bytes))
                        storageBytes += bytes;
                }

                list.Add(new DeviceView(
                    d.Id, d.Name, d.Os, d.AgentVersion, d.LastSeenUtc,
                    d.LastIpAddress, storageBytes, gameCount));
            }
            return list;
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Alle per Heartbeat gemeldeten Per-Spiel-Geräte-Zustände als flache Liste (master-only,
    /// fürs Spiel-Drawer des Dashboards). Der Anzeigename je Spiel wird – wie in den übrigen
    /// Store-Methoden – aus dem Bucket-Datensatz nachgezogen.
    /// </summary>
    public async Task<GameStatesResponse> GetGameStatesAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var list = new List<DeviceGameStatus>(_index.GameStates.Count);
            foreach (var s in _index.GameStates)
            {
                var g = FindGame(s.GameKeyValue);
                var key = g is null
                    ? new GameKey(s.GameKeyValue, s.GameKeyValue)
                    : ToGameKey(g);
                list.Add(new DeviceGameStatus(s.DeviceId, key, s.BaseRevision, s.Status));
            }
            return new GameStatesResponse(list);
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
                    g.CurrentTotalBytes,
                    BucketKey.ToWire(BucketKey.ScopeOf(g.KeyValue)),
                    BucketKey.OwnerOf(g.KeyValue),
                    BucketKey.Original(ToGameKey(g)).Value,
                    g.IsFork));
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
                    rev.IsConflict, rev.BasedOnRevision, rev.SaveRoot));
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
            return new RevisionDownload(rev.Number, ToGameKey(g), rev.DeviceId, rev.TimestampUtc, rev.Manifest, rev.SaveRoot);
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
                req.Manifest, req.IsConflict, req.BasedOnRevision,
                SaveRoot: Clip(req.SaveRoot, string.Empty, 400) is { Length: > 0 } sr ? sr : null);
            WriteRevision(g, rev);
            g.LastRevisionNumber = number;

            // Fehlende Blobs VOR der Head-/Metadaten-Entscheidung bestimmen: nur wenn alles schon
            // vorliegt (z.B. Dedup), darf der Head sofort vorrücken. Fehlt etwas, bleibt der Head
            // stehen, bis der letzte Blob per Content-PUT eintrifft (siehe TryFinalizePendingAsync).
            var gameKey = ToGameKey(g);
            var missing = req.Manifest.Entries
                .Select(e => e.Sha256)
                .Where(sha => !ContentExists(gameKey, sha))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

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
            else if (missing.Count == 0)
            {
                // Alle Blobs bereits vorhanden → sofort finalisieren (Head rückt vor, "upload"-Activity).
                FinalizeUpload(g, rev);
            }
            else
            {
                // Blobs fehlen noch: Head NICHT vorrücken. Revision als „pending" vormerken; das Gerät
                // bleibt im Zustand „Syncing", die "upload"-Activity kommt erst beim Finalisieren.
                if (!g.PendingRevisions.Contains(number))
                    g.PendingRevisions.Add(number);
                SetDeviceGameState(req.Device.Id, g.KeyValue, cur, SyncStatus.Syncing);
            }

            Save();
            return new UploadRevisionResponse(number, missing);
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Finalisiert alle Pending-Revisionen eines Spiels, deren Blobs inzwischen vollständig sind –
    /// entlang der Kette: Es wird nur eine Revision zum Head gemacht, die genau an den aktuellen
    /// Head anschließt (<c>BasedOnRevision ?? CurrentRevision == CurrentRevision</c>) UND deren
    /// sämtliche Manifest-Blobs vorliegen. Danach kann die nächste Pending anschließen. So rückt der
    /// Head erst vor, wenn ein vollständiges Save-Set auf der Platte liegt. Idempotent und
    /// nebenläufigkeitssicher (läuft unter <see cref="_gate"/>); typischerweise aufgerufen, nachdem
    /// ein Content-Blob per PUT eintraf.
    /// </summary>
    public async Task TryFinalizePendingAsync(GameKey game, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var g = FindGame(game.Value);
            if (g is null || g.PendingRevisions.Count == 0) return;

            var gameKey = ToGameKey(g);
            var changed = false;

            bool finalizedOne;
            do
            {
                finalizedOne = false;

                // Kandidaten aufsteigend prüfen, damit die Kette lückenlos ab dem aktuellen Head
                // vorrückt (die kleinste passende zuerst).
                foreach (var pendingNumber in g.PendingRevisions.OrderBy(n => n).ToList())
                {
                    var rev = LoadRevision(g, pendingNumber);
                    if (rev is null)
                    {
                        // Korrupt/fehlend: aus der Pending-Liste nehmen (kein Head, kein Datenverlust).
                        g.PendingRevisions.Remove(pendingNumber);
                        changed = true;
                        continue;
                    }

                    // Schließt sie genau an den aktuellen Head an?
                    if ((rev.BasedOnRevision ?? g.CurrentRevision) != g.CurrentRevision)
                        continue;

                    // Alle Blobs vorhanden?
                    if (rev.Manifest.Entries.Any(e => !ContentExists(gameKey, e.Sha256)))
                        continue;

                    FinalizeUpload(g, rev);
                    changed = true;
                    finalizedOne = true;
                    break; // Head hat sich bewegt – Schleife neu, die nächste Pending könnte folgen.
                }
            }
            while (finalizedOne);

            if (changed) Save();
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
            // Der Gewinner MUSS am Konflikt beteiligt sein – sonst käme ein leeres Gerät ("") in die
            // Schleife und selbst der echte Gewinner bekäme fälschlich einen Download-Befehl.
            var p = conflict.Participants.FirstOrDefault(x => x.Revision == winnerRev)
                ?? throw new VaultException(400, "Gewinner-Revision gehört nicht zum Konflikt.");
            if (!string.IsNullOrWhiteSpace(req.WinningDeviceId)
                && !string.Equals(req.WinningDeviceId, p.DeviceId, StringComparison.Ordinal))
                throw new VaultException(400, "Gewinner-Gerät und -Revision passen nicht zusammen.");
            winnerDevice = p.DeviceId;
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
            winning.Manifest, IsConflict: false, BasedOnRevision: g.CurrentRevision,
            SaveRoot: winning.SaveRoot);
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
        // „Beide behalten": Das Original-Save-Set behält die GEWINNER-Fassung (die aktuelle Head-
        // Revision) und bekommt – analog KeepDevice – eine neue Head-Revision, damit alle Nicht-
        // Gewinner divergieren und einen Download-Befehl erhalten. Die VERLIERER-Fassung wird als
        // umbenanntes zweites Save-Set (Fork) dauerhaft abgelegt; nichts wird gelöscht. Ergebnis:
        // jedes beteiligte Gerät konvergiert auf die Gewinner-Fassung im Original-Save-Set, während
        // die Verlierer-Fassung server-seitig im Fork-Bucket erhalten bleibt.
        var forkParticipant = conflict.Participants.FirstOrDefault(p => p.Revision != g.CurrentRevision)
            ?? conflict.Participants.OrderBy(p => p.Revision).FirstOrDefault();
        if (forkParticipant is null)
            throw new VaultException(400, "Konflikt ohne Beteiligte – nichts zu behalten.");

        var loser = LoadRevision(g, forkParticipant.Revision)
            ?? throw new VaultException(404, $"Verlierer-Revision {forkParticipant.Revision} nicht gefunden.");

        // Gewinner = die aktuelle Head-Revision des Original-Save-Sets.
        var winner = LoadRevision(g, g.CurrentRevision)
            ?? throw new VaultException(404, $"Aktuelle Revision {g.CurrentRevision} nicht gefunden.");
        var winnerDevice = winner.DeviceId;

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
        CopyManifestBlobs(ToGameKey(g), forkKey, loser.Manifest);

        var forkNumber = fork.LastRevisionNumber + 1;
        var forkRev = new Revision(
            forkNumber, forkKey, forkParticipant.DeviceId, DateTime.UtcNow,
            loser.Manifest, IsConflict: false, BasedOnRevision: null,
            SaveRoot: loser.SaveRoot);
        WriteRevision(fork, forkRev);
        fork.LastRevisionNumber = forkNumber;
        fork.CurrentRevision = forkNumber;
        fork.CurrentFileCount = loser.Manifest.FileCount;
        fork.CurrentTotalBytes = loser.Manifest.TotalBytes;

        // Neue Head-Revision des Original-Save-Sets mit der Gewinner-Fassung (analog KeepDevice):
        // hebt CurrentRevision an, sodass Nicht-Gewinner beim Abgleich divergieren.
        var headNumber = g.LastRevisionNumber + 1;
        var newHead = new Revision(
            headNumber, ToGameKey(g), winnerDevice, DateTime.UtcNow,
            winner.Manifest, IsConflict: false, BasedOnRevision: g.CurrentRevision,
            SaveRoot: winner.SaveRoot);
        WriteRevision(g, newHead);
        g.LastRevisionNumber = headNumber;
        g.CurrentRevision = headNumber;
        g.CurrentFileCount = winner.Manifest.FileCount;
        g.CurrentTotalBytes = winner.Manifest.TotalBytes;

        // Befehle einreihen: jedes Nicht-Gewinner-Gerät lädt die neue Head-Revision des Originals –
        // so bleibt KEIN beteiligtes Gerät divergent. Der Gewinner hat die Fassung bereits lokal.
        foreach (var p in conflict.Participants)
        {
            if (string.Equals(p.DeviceId, winnerDevice, StringComparison.Ordinal))
            {
                SetDeviceGameState(p.DeviceId, g.KeyValue, headNumber, SyncStatus.Synced);
                continue;
            }
            EnqueueCommand(new Command(
                Secrets.NewId(), CommandType.ApplyResolution, p.DeviceId, ToGameKey(g),
                DateTime.UtcNow, TargetRevision: headNumber, Resolution: ConflictResolutionKind.KeepBoth,
                ConflictId: conflict.Id));
            SetDeviceGameState(p.DeviceId, g.KeyValue, g.CurrentRevision, SyncStatus.Pending);
        }

        AddActivity(new ActivityEntry
        {
            Id = Secrets.NewId(),
            TimestampUtc = DateTime.UtcNow,
            Action = "resolve",
            GameKeyValue = g.KeyValue,
            GameDisplayName = g.DisplayName,
            DeviceId = forkParticipant.DeviceId,
            Revision = headNumber,
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
    // Dashboard: Teilen etablieren + Legacy löschen (master-only)
    // =============================================================================

    /// <summary>
    /// Etabliert einen GETEILTEN Bucket für ein Spiel, indem der aktuelle Stand des gewählten Geräts
    /// (dessen privater Bucket) als geteilte Revision 1 kopiert wird. 409, wenn schon ein geteilter
    /// Stand existiert; 404, wenn das Quell-Gerät keinen Stand hat. Blobs werden inhaltsadressiert
    /// kopiert (nichts am privaten Bucket verändert).
    /// </summary>
    public async Task<ShareSeedResponse> SeedSharedFromDeviceAsync(GameKey canonicalGame, string sourceDeviceId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sourceDeviceId))
            throw new VaultException(400, "Quell-Gerät fehlt.");

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var privateKey = BucketKey.Resolve(canonicalGame, BucketScope.Private, sourceDeviceId);
            var priv = FindGame(privateKey.Value);
            if (priv is null || priv.CurrentRevision <= 0)
                throw new VaultException(404, "Das gewählte Gerät hat für dieses Spiel keinen Stand.");
            if (priv.IsFork)
                throw new VaultException(400, "Konflikt-Kopien können nicht geteilt werden.");

            var sharedKey = BucketKey.Resolve(canonicalGame, BucketScope.Shared, null);
            var shared = FindGame(sharedKey.Value);
            if (shared is not null && shared.CurrentRevision > 0)
                throw new VaultException(409, "Für dieses Spiel gibt es bereits einen geteilten Stand.");

            var head = LoadRevision(priv, priv.CurrentRevision)
                ?? throw new VaultException(404, "Quell-Revision nicht gefunden.");

            if (shared is null)
            {
                shared = new GameRecord
                {
                    KeyValue = sharedKey.Value,
                    DisplayName = priv.DisplayName,
                    Store = priv.Store,
                    StoreId = priv.StoreId,
                };
                _index.Games.Add(shared);
            }

            // Inhalte (Blobs) inhaltsadressiert vom privaten in den geteilten Bucket kopieren.
            CopyManifestBlobs(privateKey, sharedKey, head.Manifest);

            // Erste Revision eines frisch angelegten Buckets → keine Basis (analog Fork/Erst-Upload).
            var number = shared.LastRevisionNumber + 1;
            var rev = new Revision(
                number, ToGameKey(shared), sourceDeviceId, DateTime.UtcNow,
                head.Manifest, IsConflict: false, BasedOnRevision: null, SaveRoot: head.SaveRoot);
            WriteRevision(shared, rev);
            shared.LastRevisionNumber = number;
            shared.CurrentRevision = number;
            shared.CurrentFileCount = head.Manifest.FileCount;
            shared.CurrentTotalBytes = head.Manifest.TotalBytes;

            var srcName = _index.Devices.FirstOrDefault(d => d.Id == sourceDeviceId)?.Name ?? sourceDeviceId;
            AddActivity(new ActivityEntry
            {
                Id = Secrets.NewId(),
                TimestampUtc = DateTime.UtcNow,
                Action = "upload",
                GameKeyValue = shared.KeyValue,
                GameDisplayName = shared.DisplayName,
                DeviceId = sourceDeviceId,
                DeviceName = srcName,
                Revision = number,
                Bytes = head.Manifest.TotalBytes,
                FileCount = head.Manifest.FileCount,
                Detail = "Geteilter Stand im Dashboard etabliert",
            });

            Save();
            return new ShareSeedResponse(number);
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Löscht einen eingefrorenen LEGACY-Bucket samt Revisionen und Blobs. Nur Legacy (kein Präfix)
    /// ist zulässig – private/geteilte Buckets bleiben geschützt (400). Das Verzeichnis wird
    /// traversal-sicher über <see cref="StoragePaths"/> aufgelöst und nur gelöscht, wenn es garantiert
    /// unter dem Datenverzeichnis liegt.
    /// </summary>
    public async Task DeleteLegacyBucketAsync(GameKey bucketKey, CancellationToken ct)
    {
        if (BucketKey.ScopeOf(bucketKey.Value) != BucketScope.Legacy)
            throw new VaultException(400, "Nur eingefrorene Legacy-Buckets können hier gelöscht werden.");

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var g = FindGame(bucketKey.Value)
                ?? throw new VaultException(404, "Unbekannter Bucket.");
            // Konflikt-Kopien tragen zwar keinen Scope-Präfix (sehen also „legacy" aus), sind aber
            // bewahrte Verlierer-Stände einer KeepBoth-Lösung – nie über diese Route zu löschen.
            if (g.IsFork)
                throw new VaultException(400, "Konflikt-Kopien sind keine Legacy-Buckets.");

            var dir = _paths.GameDirectory(bucketKey);
            if (_paths.IsWithinData(dir) && Directory.Exists(dir))
            {
                try { Directory.Delete(dir, recursive: true); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* best effort */ }
            }

            _index.Games.Remove(g);
            _index.GameStates.RemoveAll(s => s.GameKeyValue == bucketKey.Value);
            _index.Conflicts.RemoveAll(c => c.Game.Value == bucketKey.Value);
            _index.Commands.RemoveAll(cmd => cmd.Game.Value == bucketKey.Value);
            _index.Activity.RemoveAll(a => a.GameKeyValue == bucketKey.Value);
            Save();
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

    /// <summary>
    /// Reichert einen bereits bestehenden Spiel-Bucket mit dem echten Anzeigenamen und (falls
    /// vorhanden) Store/StoreId aus einem Client-<see cref="GameKey"/> an. Der gehashte
    /// Bucket-Schlüssel (<see cref="GameRecord.KeyValue"/>) bleibt unverändert (Pfad-Sicherheit).
    /// Ein Anzeigename, der bloß dem normalisierten Schlüssel entspricht (Fallback), überschreibt
    /// keinen bereits echten Namen.
    /// </summary>
    private void ApplyGameKeyMetadata(GameKey key)
    {
        var g = FindGame(key.Value);
        if (g is null) return; // Bucket entsteht erst beim Upload; hier nur anreichern.

        if (!string.IsNullOrWhiteSpace(key.DisplayName)
            && !string.Equals(key.DisplayName, key.Value, StringComparison.Ordinal))
            g.DisplayName = key.DisplayName;
        if (!string.IsNullOrWhiteSpace(key.Store))
            g.Store = key.Store;
        if (!string.IsNullOrWhiteSpace(key.StoreId))
            g.StoreId = key.StoreId;
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

    /// <summary>
    /// Rückt den Head eines Spiels auf eine (nun vollständige) Nicht-Konflikt-Revision vor:
    /// entfernt sie aus <see cref="GameRecord.PendingRevisions"/>, setzt Head + Kennzahlen, meldet
    /// das Gerät als „Synced" und schreibt die einmalige "upload"-Activity. Der Gerätename wird –
    /// anders als beim Anmelden (<c>req.Device.Name</c>) – aus dem Index nachgeschlagen (beim
    /// späteren Finalisieren liegt kein Request mehr vor); Fallback ist die rohe DeviceId.
    /// Nur unter <see cref="_gate"/> aufrufen.
    /// </summary>
    private void FinalizeUpload(GameRecord g, Revision rev)
    {
        g.PendingRevisions.Remove(rev.Number);

        g.CurrentRevision = rev.Number;
        g.CurrentFileCount = rev.Manifest.FileCount;
        g.CurrentTotalBytes = rev.Manifest.TotalBytes;
        SetDeviceGameState(rev.DeviceId, g.KeyValue, rev.Number, SyncStatus.Synced);

        var deviceName = _index.Devices.FirstOrDefault(d => d.Id == rev.DeviceId)?.Name ?? rev.DeviceId;
        AddActivity(new ActivityEntry
        {
            Id = Secrets.NewId(),
            TimestampUtc = DateTime.UtcNow,
            Action = "upload",
            GameKeyValue = g.KeyValue,
            GameDisplayName = g.DisplayName,
            DeviceId = rev.DeviceId,
            DeviceName = deviceName,
            Revision = rev.Number,
            Bytes = rev.Manifest.TotalBytes,
            FileCount = rev.Manifest.FileCount,
            Detail = "Neue Version hochgeladen",
        });
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

    /// <summary>
    /// Kopiert die im Manifest referenzierten Blobs inhaltsadressiert vom Quell- in den
    /// Ziel-Bucket (je Blob No-Op, wenn er im Ziel schon liegt). Der Quell-Bucket bleibt
    /// unverändert. Wird beim KeepBoth-Fork und beim Etablieren eines geteilten Buckets genutzt.
    /// </summary>
    private void CopyManifestBlobs(GameKey sourceKey, GameKey targetKey, FileManifest manifest)
    {
        foreach (var entry in manifest.Entries)
        {
            var src = _paths.ContentFile(sourceKey, entry.Sha256);
            var dst = _paths.ContentFile(targetKey, entry.Sha256);
            if (File.Exists(src) && !File.Exists(dst))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                try { File.Copy(src, dst); }
                catch (IOException) when (File.Exists(dst)) { /* parallel schon da */ }
            }
        }
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
