using SaveVault.Core.Models;

namespace SaveVault.Server.Storage;

/// <summary>
/// Der serverseitige Index – die „Wahrheit" über Geräte, Spiele, Konflikte, Befehle und
/// den Aktivitäts-Verlauf. Wird als EINE JSON-Datei (<c>index.json</c>) atomar geschrieben
/// (Temp-Datei + Rename) und unter einem Lock verändert. Die Datei-Inhalte (Blobs) und die
/// Revisions-Manifeste liegen NICHT hier, sondern inhaltsadressiert bzw. je Revision auf der
/// Platte; der Index hält nur die Metadaten/Zeiger.
///
/// Bewusst veränderliche Klassen (keine records): der Index wird im Speicher gehalten,
/// unter Lock mutiert und als Ganzes atomar zurückgeschrieben.
/// </summary>
public sealed class ServerIndex
{
    public int Version { get; set; } = 1;

    /// <summary>Aktueller Pairing-Code (Klartext; LAN-only, wird im Dashboard angezeigt).</summary>
    public string? PairingCode { get; set; }
    public DateTime PairingCodeUpdatedUtc { get; set; }

    public List<DeviceRecord> Devices { get; set; } = new();
    public List<GameRecord> Games { get; set; } = new();
    public List<DeviceGameStateRecord> GameStates { get; set; } = new();
    public List<Conflict> Conflicts { get; set; } = new();
    public List<Command> Commands { get; set; } = new();
    public List<ActivityEntry> Activity { get; set; } = new();
}

/// <summary>
/// Ein bekanntes Gerät. Statt des rohen Geräte-Tokens wird nur dessen SHA-256-Hash
/// gespeichert – so liegt kein wiederverwendbares Secret im Klartext auf der Platte.
/// </summary>
public sealed class DeviceRecord
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Os { get; set; } = string.Empty;
    public string AgentVersion { get; set; } = string.Empty;
    public DateTime LastSeenUtc { get; set; }
    public DateTime PairedUtc { get; set; }

    /// <summary>Hex-SHA-256 des Geräte-Tokens (nie der Token selbst).</summary>
    public string TokenHash { get; set; } = string.Empty;
}

/// <summary>
/// Metadaten eines Spiel-Buckets. Der Ablage-Ordner ergibt sich aus dem gehashten
/// <see cref="KeyValue"/> (siehe StoragePaths); hier stehen Anzeige- und Zeiger-Daten.
/// </summary>
public sealed class GameRecord
{
    public string KeyValue { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Store { get; set; }
    public string? StoreId { get; set; }

    /// <summary>Höchste je vergebene Revisionsnummer (auch Konflikt-Revisionen zählen).</summary>
    public long LastRevisionNumber { get; set; }

    /// <summary>Zeiger auf die aktuelle („Head") Revision; 0 = keine akzeptierte Revision.</summary>
    public long CurrentRevision { get; set; }

    /// <summary>Zwischengespeicherte Kennzahlen der aktuellen Revision (spart Platten-Lesungen).</summary>
    public int CurrentFileCount { get; set; }
    public long CurrentTotalBytes { get; set; }

    /// <summary>True, wenn dieser Bucket durch „Beide behalten (umbenennen)" entstand.</summary>
    public bool IsFork { get; set; }
}

/// <summary>Je-Gerät-je-Spiel-Zustand (zuletzt gesehene Basis-Revision + Status).</summary>
public sealed class DeviceGameStateRecord
{
    public string DeviceId { get; set; } = string.Empty;
    public string GameKeyValue { get; set; } = string.Empty;
    public long BaseRevision { get; set; }
    public SyncStatus Status { get; set; }
    public DateTime UpdatedUtc { get; set; }
}

/// <summary>Ein Eintrag im Aktivitäts-Verlauf (für die „Verlauf"-Ansicht des Dashboards).</summary>
public sealed class ActivityEntry
{
    public string Id { get; set; } = string.Empty;
    public DateTime TimestampUtc { get; set; }

    /// <summary>Aktionsart: upload, download, conflict, resolve, restore, pair.</summary>
    public string Action { get; set; } = string.Empty;

    public string? GameKeyValue { get; set; }
    public string? GameDisplayName { get; set; }
    public string? DeviceId { get; set; }
    public string? DeviceName { get; set; }
    public long? Revision { get; set; }
    public long? Bytes { get; set; }
    public int? FileCount { get; set; }
    public string? Detail { get; set; }
}
