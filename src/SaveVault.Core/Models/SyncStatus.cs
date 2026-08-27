namespace SaveVault.Core.Models;

/// <summary>
/// Sync-Zustand eines Spiels aus Sicht eines Geräts. Entspricht den Status-Kategorien
/// der Oberfläche (die Farbzuordnung trifft die UI, nicht die Domäne).
/// </summary>
public enum SyncStatus
{
    Synced,
    Syncing,
    Conflict,
    Pending,
    Offline,
    Error
}
