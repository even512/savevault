namespace SaveVault.Core.Models;

/// <summary>Ein an einem Konflikt beteiligter Stand (Gerät + dessen Revision).</summary>
public sealed record ConflictParticipant(string DeviceId, long Revision);

/// <summary>
/// Ein erkannter Konflikt an einem Save-Set: beide Seiten haben seit dem letzten Sync
/// geändert. Solange er nicht gelöst ist, wird nichts überschrieben.
/// </summary>
public sealed record Conflict(
    string Id,
    GameKey Game,
    IReadOnlyList<ConflictParticipant> Participants,
    DateTime DetectedUtc,
    bool Resolved = false);
