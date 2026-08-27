using SaveVault.Core.Models;

namespace SaveVault.Core.Sync;

/// <summary>
/// Lokaler Sync-State eines Save-Sets, wie ihn der Client persistiert: das Manifest
/// beim letzten erfolgreichen Sync (<see cref="BaseManifest"/>) und welche Server-
/// Revision der lokale Stand zuletzt sah (<see cref="BaseRevision"/>). Ist noch nie
/// synchronisiert worden, ist <see cref="BaseManifest"/> null und
/// <see cref="BaseRevision"/> 0.
/// </summary>
public sealed record SyncState(
    GameKey Game,
    long BaseRevision,
    FileManifest? BaseManifest)
{
    /// <summary>Startzustand für ein Spiel, das lokal noch nie synchronisiert wurde.</summary>
    public static SyncState Initial(GameKey game) => new(game, 0, null);
}
