namespace SaveVault.Server.Security;

/// <summary>
/// Ergebnis der Token-Prüfung: entweder das Master-Token (Admin, z. B. Dashboard) oder ein
/// bestimmtes Gerät. Wird nach erfolgreicher Auth im HttpContext hinterlegt, damit Endpunkte
/// prüfen können, ob ein Gerät nur auf seine eigenen Daten (Befehle) zugreift.
/// </summary>
public sealed record AuthPrincipal(bool IsMaster, string? DeviceId)
{
    public static AuthPrincipal Master { get; } = new(true, null);

    public static AuthPrincipal ForDevice(string deviceId) => new(false, deviceId);

    /// <summary>Darf dieser Prinzipal im Namen des angegebenen Geräts handeln?</summary>
    public bool CanActAsDevice(string deviceId)
        => IsMaster || string.Equals(DeviceId, deviceId, StringComparison.Ordinal);
}
