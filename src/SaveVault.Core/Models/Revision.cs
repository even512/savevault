namespace SaveVault.Core.Models;

/// <summary>
/// Eine serverseitige Revision eines Save-Sets: monoton steigende <see cref="Number"/>,
/// erzeugendes Gerät, Zeitpunkt, der zugehörige <see cref="FileManifest"/>, ob es sich
/// um eine Konflikt-Revision handelt und optional, auf welche Revision sie sich stützt.
/// </summary>
public sealed record Revision(
    long Number,
    GameKey Game,
    string DeviceId,
    DateTime TimestampUtc,
    FileManifest Manifest,
    bool IsConflict = false,
    long? BasedOnRevision = null);
