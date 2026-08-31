using System.Text.Json;
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
    /// Ob über abgeschlossene Übertragungen (gesichert/synchronisiert) benachrichtigt wird
    /// (Default <c>true</c>). Wirkt nur, wenn der Master <see cref="ToastsEnabled"/> an ist.
    /// Fehlt das Feld in einer alten <c>config.json</c>, wird es als <c>true</c> gelesen –
    /// also „an", konsistent mit den anderen Default-AN-Feldern.
    /// </summary>
    public bool NotifyTransfers { get; set; } = true;

    /// <summary>
    /// Ob über Konflikte benachrichtigt wird (Default <c>true</c>). Wirkt nur, wenn der
    /// Master <see cref="ToastsEnabled"/> an ist. Fehlt das Feld in einer alten
    /// <c>config.json</c>, wird es als <c>true</c> gelesen – also „an".
    /// </summary>
    public bool NotifyConflicts { get; set; } = true;

    /// <summary>
    /// Ob der Toast mit System-Ton erscheint (Default <c>true</c>). <c>false</c> = der Toast
    /// erscheint lautlos (kein Pling). Fehlt das Feld in einer alten <c>config.json</c>, wird
    /// es als <c>true</c> gelesen – also „mit Ton".
    /// </summary>
    public bool NotificationSound { get; set; } = true;

    /// <summary>
    /// Ob während eines laufenden Vollbild-/Randlos-Spiels statt des lauten Toasts kurz das
    /// SaveVault-Wasserzeichen in einer Ecke gezeigt wird (Default <c>true</c>). Fehlt das
    /// Feld in einer alten <c>config.json</c>, wird es als <c>true</c> gelesen – also „an".
    /// </summary>
    public bool GameWatermarkEnabled { get; set; } = true;

    /// <summary>
    /// Ecke des Bildschirms, in der das Wasserzeichen erscheint (Default
    /// <see cref="WatermarkCorner.BottomRight"/>). Fehlt oder ist der Wert in einer alten/
    /// fremden <c>config.json</c> unbekannt, fällt er tolerant auf <c>BottomRight</c> zurück
    /// (siehe <see cref="WatermarkCornerJsonConverter"/>) – es wird nie geworfen. Das Attribut
    /// steht bewusst auf der <b>Property</b>: ein Property-Attribut hat die höchste Präzedenz
    /// und schlägt den global in <c>SaveVaultJson.Options.Converters</c> registrierten
    /// <c>JsonStringEnumConverter</c> (der bei einem unbekannten Wert werfen würde).
    /// </summary>
    [JsonConverter(typeof(WatermarkCornerJsonConverter))]
    public WatermarkCorner WatermarkCorner { get; set; } = WatermarkCorner.BottomRight;

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
/// Ecke des Bildschirms für das Spiel-Wasserzeichen. Serialisiert wird über den toleranten
/// <see cref="WatermarkCornerJsonConverter"/> (als String, wie der übrige Enum-Stil des
/// Projekts), der per Attribut an der <c>WatermarkCorner</c>-<b>Property</b> gebunden ist –
/// nicht am Enum-Typ, weil ein Typ-Attribut vom global registrierten
/// <c>JsonStringEnumConverter</c> geschlagen würde und der tolerante Konverter dann nie liefe.
/// </summary>
public enum WatermarkCorner
{
    /// <summary>Unten rechts (Default).</summary>
    BottomRight,

    /// <summary>Oben rechts.</summary>
    TopRight,

    /// <summary>Oben links.</summary>
    TopLeft,

    /// <summary>Unten links.</summary>
    BottomLeft,
}

/// <summary>
/// Toleranter JSON-Konverter für <see cref="WatermarkCorner"/>. Per <c>[JsonConverter]</c> an der
/// <c>WatermarkCorner</c>-Property gebunden (Property-Attribute haben Vorrang vor dem global in
/// <c>SaveVaultJson.Options.Converters</c> gesetzten <c>JsonStringEnumConverter</c>, der bei einem
/// unbekannten String werfen und damit beim Lesen die ganze <c>config.json</c> verwerfen würde).
/// Geschrieben wird
/// weiterhin als String (konsistent mit dem übrigen Serialisierungsstil); gelesen wird tolerant:
/// unbekannter String, Zahl außerhalb des Bereichs, <c>null</c> oder ein unerwarteter Token
/// fallen still auf <see cref="WatermarkCorner.BottomRight"/> zurück – es wird nie geworfen.
/// </summary>
public sealed class WatermarkCornerJsonConverter : JsonConverter<WatermarkCorner>
{
    public override WatermarkCorner Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        try
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.String:
                    var text = reader.GetString();
                    return Enum.TryParse<WatermarkCorner>(text, ignoreCase: true, out var parsed)
                           && Enum.IsDefined(typeof(WatermarkCorner), parsed)
                        ? parsed
                        : WatermarkCorner.BottomRight;

                case JsonTokenType.Number:
                    return reader.TryGetInt32(out var number)
                           && Enum.IsDefined(typeof(WatermarkCorner), number)
                        ? (WatermarkCorner)number
                        : WatermarkCorner.BottomRight;

                default:
                    // null oder ein unerwarteter Token → Default, nie werfen.
                    return WatermarkCorner.BottomRight;
            }
        }
        catch
        {
            // Jeder Ausnahmefall ⇒ Default, damit ein fremder Wert nie die Config sprengt.
            return WatermarkCorner.BottomRight;
        }
    }

    public override void Write(Utf8JsonWriter writer, WatermarkCorner value, JsonSerializerOptions options)
    {
        // Bekannten Wert als String schreiben; ein (theoretisch) unbekannter fällt auf BottomRight.
        var safe = Enum.IsDefined(typeof(WatermarkCorner), value) ? value : WatermarkCorner.BottomRight;
        writer.WriteStringValue(safe.ToString());
    }
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
