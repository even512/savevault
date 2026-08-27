namespace SaveVault.Core.Models;

/// <summary>Beschreibt ein bekanntes Client-Gerät (Selbstauskunft beim Heartbeat).</summary>
public sealed record DeviceInfo(
    string Id,
    string Name,
    string Os,
    string AgentVersion,
    DateTime LastSeenUtc);
