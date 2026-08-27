namespace SaveVault.Core.Models;

/// <summary>Art eines Server→Client-Befehls.</summary>
public enum CommandType
{
    /// <summary>Eine bestimmte (alte) Revision wiederherstellen.</summary>
    Restore,

    /// <summary>Eine Konfliktentscheidung anwenden (Download der Gewinner-Revision).</summary>
    ApplyResolution
}

/// <summary>Wie ein Konflikt gelöst wird.</summary>
public enum ConflictResolutionKind
{
    /// <summary>Ein Gerät gewinnt; sein Stand wird die neue aktuelle Revision.</summary>
    KeepDevice,

    /// <summary>Beide behalten: die Verlierer-Fassung wird als umbenanntes Save-Set abgelegt.</summary>
    KeepBoth
}

/// <summary>
/// Ein Befehl aus dem Server→Client-Kanal (der Client pollt die Warteschlange).
/// Je nach <see cref="Type"/> sind <see cref="TargetRevision"/> bzw.
/// <see cref="Resolution"/>/<see cref="ConflictId"/> gesetzt.
/// </summary>
public sealed record Command(
    string Id,
    CommandType Type,
    string TargetDeviceId,
    GameKey Game,
    DateTime CreatedUtc,
    long? TargetRevision = null,
    ConflictResolutionKind? Resolution = null,
    string? ConflictId = null);
