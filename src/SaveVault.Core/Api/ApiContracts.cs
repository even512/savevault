using SaveVault.Core.Models;

namespace SaveVault.Core.Api;

// =================================================================================
// API-DTOs – der gemeinsame Request/Response-Vertrag der Endpunkte.
// Server (Schritt 3) implementiert sie, Client (Schritt 5) konsumiert sie.
// Serialisierung über SaveVault.Core.Serialization.SaveVaultJson (Enums als String).
// =================================================================================

// --- Dashboard-Anmeldung (Benutzer/Passwort → Session-Token) -----------------------

/// <summary>Ersteinrichtung: legt das (einzige) Admin-Konto an. Nur solange keins existiert.</summary>
public sealed record SetupRequest(string Username, string Password);

/// <summary>Anmeldung mit Benutzername + Passwort.</summary>
public sealed record LoginRequest(string Username, string Password);

/// <summary>Antwort auf erfolgreiche Anmeldung/Einrichtung: Session-Token + Ablauf + Anzeigename.</summary>
public sealed record LoginResponse(string SessionToken, DateTime ExpiresUtc, string Username);

// --- Pairing: Code -> Geräte-Token -------------------------------------------------

/// <summary>Client tauscht einen Pairing-Code samt Selbstauskunft gegen einen Geräte-Token.</summary>
public sealed record PairRequest(string Code, string DeviceName, string Os, string AgentVersion);

/// <summary>Antwort auf ein erfolgreiches Pairing.</summary>
public sealed record PairResponse(string DeviceId, string DeviceToken);

// --- Heartbeat / Registrierung -----------------------------------------------------

/// <summary>Regelmäßige Zustandsmeldung: Gerät + je-Spiel-Zustand.</summary>
public sealed record HeartbeatRequest(DeviceInfo Device, IReadOnlyList<DeviceGameState> GameStates);

/// <summary>Antwort auf einen Heartbeat (Serverzeit + Anzahl offener Befehle).</summary>
public sealed record HeartbeatResponse(DateTime ServerTimeUtc, int PendingCommandCount);

// --- Spiele / Revisionen -----------------------------------------------------------

/// <summary>Übersichtseintrag eines Spiels für Listen/Dashboard.</summary>
public sealed record GameSummary(
    GameKey Game,
    long CurrentRevision,
    SyncStatus Status,
    int FileCount,
    long TotalBytes);

/// <summary>Liste aller dem Server bekannten Spiele.</summary>
public sealed record GamesResponse(IReadOnlyList<GameSummary> Games);

/// <summary>Delta-Abfrage: die aktuelle Server-Revisionsnummer eines Spiels (0 = keine).</summary>
public sealed record RevisionHead(GameKey Game, long CurrentRevision);

/// <summary>Metadaten einer Revision (ohne Datei-Inhalte) für Verlauf/Listen.</summary>
public sealed record RevisionInfo(
    long Number,
    string DeviceId,
    DateTime TimestampUtc,
    long TotalBytes,
    int FileCount,
    string ManifestHash,
    bool IsConflict,
    long? BasedOnRevision,
    string? SaveRoot = null);

/// <summary>Versionsverlauf eines Spiels.</summary>
public sealed record RevisionListResponse(GameKey Game, IReadOnlyList<RevisionInfo> Revisions);

// --- Upload / Download -------------------------------------------------------------

/// <summary>
/// Meldet eine neue Revision an (Metadaten + Manifest). Die Datei-Inhalte werden danach
/// separat inhaltsadressiert übertragen (siehe <see cref="UploadRevisionResponse.MissingHashes"/>
/// und <c>UploadContentAsync</c>). <paramref name="IsConflict"/> markiert eine Konflikt-Revision.
/// </summary>
public sealed record UploadRevisionRequest(
    DeviceInfo Device,
    FileManifest Manifest,
    bool IsConflict,
    long? BasedOnRevision,
    string? SaveRoot = null);

/// <summary>
/// Antwort auf die Revisions-Anmeldung: die zugeteilte Nummer und die Hashes der
/// Dateien, deren Inhalt der Server noch nicht kennt und die hochzuladen sind.
/// </summary>
public sealed record UploadRevisionResponse(long Revision, IReadOnlyList<string> MissingHashes);

/// <summary>Eine Revision zum Herunterladen (Manifest; Inhalte über <c>DownloadContentAsync</c>).</summary>
public sealed record RevisionDownload(
    long Number,
    GameKey Game,
    string DeviceId,
    DateTime TimestampUtc,
    FileManifest Manifest,
    string? SaveRoot = null);

// --- Konflikte ---------------------------------------------------------------------

/// <summary>Alle offenen Konflikte.</summary>
public sealed record ConflictListResponse(IReadOnlyList<Conflict> Conflicts);

/// <summary>
/// Konfliktlösung: entweder ein Gerät gewinnt (<see cref="ConflictResolutionKind.KeepDevice"/>
/// mit <paramref name="WinningDeviceId"/>/<paramref name="WinningRevision"/>) oder beide
/// werden behalten (<see cref="ConflictResolutionKind.KeepBoth"/>).
/// </summary>
public sealed record ResolveConflictRequest(
    ConflictResolutionKind Resolution,
    string? WinningDeviceId = null,
    long? WinningRevision = null);

public sealed record ResolveConflictResponse(bool Accepted);

// --- Restore -----------------------------------------------------------------------

/// <summary>Fordert an, eine alte Revision auf ein Zielgerät wiederherzustellen.</summary>
public sealed record RestoreRequest(string TargetDeviceId, long TargetRevision);

public sealed record RestoreResponse(bool Accepted);

// --- Befehls-Warteschlange ---------------------------------------------------------

/// <summary>Die für ein Gerät anstehenden Befehle.</summary>
public sealed record CommandListResponse(IReadOnlyList<Command> Commands);

/// <summary>Generische Bestätigung (z. B. Befehl als erledigt markiert).</summary>
public sealed record AckResponse(bool Accepted);

// --- Dashboard-Übersichten (master-only) -------------------------------------------

/// <summary>
/// Anzeigedaten eines Geräts fürs Dashboard (master-only). <see cref="IpAddress"/> und
/// <see cref="StorageBytes"/> sind serverseitig abgeleitet – nie vom Client gemeldet
/// (die Selbstauskunft steckt in <see cref="DeviceInfo"/>). <see cref="StorageBytes"/> ist
/// die Summe der aktuellen Spielgrößen, die dieses Gerät lokal hält, <see cref="GameCount"/>
/// die Anzahl ebendieser Spiele.
/// </summary>
public sealed record DeviceView(
    string Id,
    string Name,
    string Os,
    string AgentVersion,
    DateTime LastSeenUtc,
    string? IpAddress,
    long StorageBytes,
    int GameCount);

/// <summary>
/// Per-Spiel-Geräte-Status (master-only, fürs Spiel-Drawer): welche Basis-Revision ein
/// Gerät zuletzt für ein Spiel meldete und mit welchem <see cref="SyncStatus"/>.
/// </summary>
public sealed record DeviceGameStatus(
    string DeviceId,
    GameKey Game,
    long BaseRevision,
    SyncStatus Status);

/// <summary>Flache Liste aller gemeldeten Per-Spiel-Geräte-Zustände.</summary>
public sealed record GameStatesResponse(IReadOnlyList<DeviceGameStatus> States);
