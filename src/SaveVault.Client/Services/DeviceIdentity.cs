using System.Reflection;
using System.Runtime.InteropServices;
using SaveVault.Core.Models;

namespace SaveVault.Client.Services;

/// <summary>
/// Baut die Selbstauskunft dieses Geräts (<see cref="DeviceInfo"/>) aus der Konfiguration
/// und der Laufzeitumgebung. Der Agent-Versionsstring kommt aus der Assembly, das
/// Betriebssystem aus <see cref="RuntimeInformation.OSDescription"/>.
/// </summary>
public static class DeviceIdentity
{
    /// <summary>Die Version dieses Client-Agents (Assembly-Version, Fallback „0.0.0").</summary>
    public static string AgentVersion { get; } =
        typeof(DeviceIdentity).Assembly.GetName().Version?.ToString() ?? "0.0.0";

    /// <summary>Betriebssystem-Beschreibung dieses Geräts.</summary>
    public static string OsDescription
    {
        get
        {
            var desc = RuntimeInformation.OSDescription;
            return string.IsNullOrWhiteSpace(desc) ? Environment.OSVersion.ToString() : desc.Trim();
        }
    }

    /// <summary>Baut die <see cref="DeviceInfo"/> aus einer Konfiguration (LastSeen = jetzt).</summary>
    public static DeviceInfo FromConfig(ClientConfig config, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(config);
        var id = string.IsNullOrWhiteSpace(config.DeviceId) ? string.Empty : config.DeviceId!;
        var name = string.IsNullOrWhiteSpace(config.DeviceName) ? Environment.MachineName : config.DeviceName!;
        return new DeviceInfo(id, name, OsDescription, AgentVersion, nowUtc);
    }
}
