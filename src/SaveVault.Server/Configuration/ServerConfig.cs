namespace SaveVault.Server.Configuration;

/// <summary>
/// Aus Umgebungsvariablen gelesene Server-Konfiguration. Bewusst tolerant beim Start:
/// fehlt das Master-Token, startet der Server trotzdem (damit Health/Dashboard den
/// „nicht eingerichtet"-Zustand zeigen können), verweigert aber jeden API-Aufruf mit
/// klarer Meldung (siehe Auth-Middleware). Secrets werden NIE geloggt oder ausgegeben.
/// </summary>
public sealed class ServerConfig
{
    /// <summary>Master-Token (Basis fürs Pairing). Leer/null = Server nicht konfiguriert.</summary>
    public string? MasterToken { get; init; }

    /// <summary>Lauscht-Port (Default 8420).</summary>
    public int Port { get; init; } = 8420;

    /// <summary>Datenwurzel für Spiele/Revisionen/Index (Default /data/savevault).</summary>
    public string DataRoot { get; init; } = "/data/savevault";

    /// <summary>IGDB/Twitch-Client-ID für den Box-Art-Bezug (optional). Leer = Cover-Feature aus.</summary>
    public string? IgdbClientId { get; init; }

    /// <summary>IGDB/Twitch-Client-Secret für den Box-Art-Bezug (optional). Leer = Cover-Feature aus.</summary>
    public string? IgdbClientSecret { get; init; }

    /// <summary>True, sobald ein nicht-leeres Master-Token vorliegt.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(MasterToken);

    /// <summary>True, wenn beide IGDB-Zugangsdaten vorliegen (Box-Art aktiv).</summary>
    public bool IsCoverEnabled =>
        !string.IsNullOrWhiteSpace(IgdbClientId) && !string.IsNullOrWhiteSpace(IgdbClientSecret);

    /// <summary>Liest die Konfiguration aus den SAVEVAULT_*-Umgebungsvariablen.</summary>
    public static ServerConfig FromEnvironment()
    {
        var token = Environment.GetEnvironmentVariable("SAVEVAULT_TOKEN");

        var port = 8420;
        var rawPort = Environment.GetEnvironmentVariable("SAVEVAULT_PORT");
        if (!string.IsNullOrWhiteSpace(rawPort)
            && int.TryParse(rawPort, out var parsed)
            && parsed is > 0 and <= 65535)
        {
            port = parsed;
        }

        var data = Environment.GetEnvironmentVariable("SAVEVAULT_DATA");
        if (string.IsNullOrWhiteSpace(data))
            data = "/data/savevault";

        // Box-Art-Zugangsdaten: bevorzugt die SAVEVAULT_-präfixierten Namen, ersatzweise die
        // unpräfixierten IGDB_-Namen – so lassen sich exakt dieselben Zugangsdaten wie im
        // dashsharp-Modul „game-releases" (dort IGDB_CLIENT_ID/SECRET) ohne Umbenennen nutzen.
        var igdbId = FirstNonEmptyEnv("SAVEVAULT_IGDB_CLIENT_ID", "IGDB_CLIENT_ID");
        var igdbSecret = FirstNonEmptyEnv("SAVEVAULT_IGDB_CLIENT_SECRET", "IGDB_CLIENT_SECRET");

        return new ServerConfig
        {
            MasterToken = string.IsNullOrWhiteSpace(token) ? null : token.Trim(),
            Port = port,
            DataRoot = data.Trim(),
            IgdbClientId = igdbId,
            IgdbClientSecret = igdbSecret,
        };
    }

    /// <summary>Erste nicht-leere Umgebungsvariable aus der Namensliste (getrimmt), sonst null.</summary>
    private static string? FirstNonEmptyEnv(params string[] names)
    {
        foreach (var name in names)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }
        return null;
    }
}
