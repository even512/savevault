using SaveVault.Core.Models;

namespace SaveVault.Core.Api;

/// <summary>
/// Der SaveVault-API-Vertrag: alle Operationen, die der Client gegen den Server fährt.
/// Der Server (Bau-Plan-Schritt 3) implementiert diese Endpunkte, der Client
/// (Schritt 5) nutzt <see cref="SaveVaultApiClient"/>. Alle Aufrufe außer
/// <see cref="PairAsync"/> setzen einen gültigen Geräte-Token (Bearer) voraus.
/// </summary>
public interface ISaveVaultApi
{
    /// <summary>Pairing-Code gegen einen Geräte-Token tauschen (kein Token nötig).</summary>
    Task<PairResponse> PairAsync(PairRequest request, CancellationToken ct = default);

    /// <summary>Zustand melden (Gerät + je-Spiel-Status) und Serverzeit/offene Befehle erfahren.</summary>
    Task<HeartbeatResponse> HeartbeatAsync(HeartbeatRequest request, CancellationToken ct = default);

    /// <summary>Alle bekannten Spiele mit aktueller Revision und Status.</summary>
    Task<GamesResponse> GetGamesAsync(CancellationToken ct = default);

    /// <summary>Delta-Abfrage: aktuelle Server-Revision eines Spiels.</summary>
    Task<RevisionHead> GetHeadAsync(GameKey game, CancellationToken ct = default);

    /// <summary>Versionsverlauf eines Spiels.</summary>
    Task<RevisionListResponse> GetRevisionsAsync(GameKey game, CancellationToken ct = default);

    /// <summary>Eine konkrete Revision (Manifest) zum Herunterladen holen.</summary>
    Task<RevisionDownload> GetRevisionAsync(GameKey game, long revision, CancellationToken ct = default);

    /// <summary>Neue Revision anmelden; Antwort nennt die fehlenden Datei-Hashes.</summary>
    Task<UploadRevisionResponse> UploadRevisionAsync(GameKey game, UploadRevisionRequest request, CancellationToken ct = default);

    /// <summary>Einen Datei-Inhalt (inhaltsadressiert nach SHA-256) hochladen.</summary>
    Task UploadContentAsync(GameKey game, string sha256, Stream content, CancellationToken ct = default);

    /// <summary>Einen Datei-Inhalt (nach SHA-256) herunterladen. Aufrufer schließt den Stream.</summary>
    Task<Stream> DownloadContentAsync(GameKey game, string sha256, CancellationToken ct = default);

    /// <summary>Alle offenen Konflikte.</summary>
    Task<ConflictListResponse> GetConflictsAsync(CancellationToken ct = default);

    /// <summary>Einen Konflikt lösen (Gewinner wählen oder beide behalten).</summary>
    Task<ResolveConflictResponse> ResolveConflictAsync(string conflictId, ResolveConflictRequest request, CancellationToken ct = default);

    /// <summary>Wiederherstellung einer alten Revision auf ein Zielgerät anfordern.</summary>
    Task<RestoreResponse> RestoreAsync(GameKey game, RestoreRequest request, CancellationToken ct = default);

    /// <summary>Anstehende Befehle für ein Gerät abrufen (Client-Polling).</summary>
    Task<CommandListResponse> GetCommandsAsync(string deviceId, CancellationToken ct = default);

    /// <summary>Einen Befehl als erledigt bestätigen.</summary>
    Task<AckResponse> AckCommandAsync(string commandId, CancellationToken ct = default);
}
