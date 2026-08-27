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

    /// <summary>True, sobald ein nicht-leeres Master-Token vorliegt.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(MasterToken);

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

        return new ServerConfig
        {
            MasterToken = string.IsNullOrWhiteSpace(token) ? null : token.Trim(),
            Port = port,
            DataRoot = data.Trim(),
        };
    }
}
