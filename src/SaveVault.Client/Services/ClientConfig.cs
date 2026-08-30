using System.Text.Json.Serialization;

namespace SaveVault.Client.Services;

/// <summary>
/// Lokale Client-Konfiguration (unter <c>%AppData%\SaveVault\config.json</c>). Wird als
/// veränderbare Klasse gehalten, damit sie sauber JSON-serialisierbar ist und die GUI
/// (Schritt 6) Felder setzen kann. Der Geräte-Token bleibt <b>ausschließlich</b> hier
/// lokal und darf nie in Logs/Ausgaben erscheinen.
/// </summary>
public sealed class ClientConfig
{
    /// <summary>Basis-URL des SaveVault-Servers (z. B. <c>http://server:8420</c>).</summary>
    public string? ServerUrl { get; set; }

    /// <summary>Vom Server beim Pairing vergebene Geräte-ID.</summary>
    public string? DeviceId { get; set; }

    /// <summary>Vom Server beim Pairing vergebener Geräte-Token (Bearer). Nie loggen.</summary>
    public string? DeviceToken { get; set; }

    /// <summary>Anzeigename dieses Geräts im Dashboard.</summary>
    public string? DeviceName { get; set; }

    /// <summary>Sync-/Poll-Intervall in Sekunden (Default 60).</summary>
    public int SyncIntervalSeconds { get; set; } = 60;

    /// <summary>
    /// Ob der Client automatisch mit dem Windows-Login starten soll (Default <c>true</c>).
    /// Fehlt das Feld in einer alten <c>config.json</c>, wird es als <c>true</c> gelesen –
    /// also „Autostart an", konsistent mit der Standardentscheidung. Der tatsächliche
    /// Eintrag im Registry-Run-Key wird über <see cref="AutostartService"/> abgeglichen.
    /// </summary>
    public bool AutostartEnabled { get; set; } = true;

    /// <summary>
    /// Ob der Client kurze Windows-Benachrichtigungen (Tray-Toasts) über abgeschlossene
    /// Sync-Aktionen (gesichert/synchronisiert/Konflikt) anzeigen soll (Default <c>true</c>).
    /// Fehlt das Feld in einer alten <c>config.json</c>, wird es als <c>true</c> gelesen –
    /// also „Benachrichtigungen an", konsistent mit den anderen Default-AN-Feldern.
    /// </summary>
    public bool ToastsEnabled { get; set; } = true;

    /// <summary>
    /// Ob der Client vollständig eingerichtet ist (Server-URL + Geräte-ID + Token). Ohne
    /// das gilt der „nicht eingerichtet"-Zustand und es werden keine Netz-Schleifen gestartet.
    /// </summary>
    [JsonIgnore]
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ServerUrl)
        && !string.IsNullOrWhiteSpace(DeviceId)
        && !string.IsNullOrWhiteSpace(DeviceToken);

    /// <summary>Das effektive Sync-Intervall, mindestens 5 Sekunden (Schutz vor 0/Negativ).</summary>
    [JsonIgnore]
    public TimeSpan SyncInterval => TimeSpan.FromSeconds(Math.Max(5, SyncIntervalSeconds));
}

/// <summary>
/// Lädt und speichert die <see cref="ClientConfig"/> atomar und tolerant. Fehlt die Datei
/// oder ist sie beschädigt, liefert <see cref="Load"/> eine leere (nicht eingerichtete)
/// Konfiguration statt zu werfen.
/// </summary>
public sealed class ClientConfigStore
{
    private readonly AppPaths _paths;

    public ClientConfigStore(AppPaths paths)
        => _paths = paths ?? throw new ArgumentNullException(nameof(paths));

    /// <summary>Lädt die Konfiguration (nie <c>null</c>; leer = nicht eingerichtet).</summary>
    public ClientConfig Load()
        => JsonFileStore.Read<ClientConfig>(_paths.ConfigFile) ?? new ClientConfig();

    /// <summary>Speichert die Konfiguration atomar.</summary>
    public void Save(ClientConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        JsonFileStore.Write(_paths.ConfigFile, config);
    }
}
