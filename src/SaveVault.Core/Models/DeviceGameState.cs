namespace SaveVault.Core.Models;

/// <summary>
/// Je-Spiel-Zustand eines Geräts: welche Server-Revision der lokale Stand zuletzt sah
/// (<see cref="BaseRevision"/>) und der aktuelle <see cref="SyncStatus"/>. Wird beim
/// Heartbeat gemeldet.
/// </summary>
public sealed record DeviceGameState(
    GameKey Game,
    long BaseRevision,
    SyncStatus Status);
