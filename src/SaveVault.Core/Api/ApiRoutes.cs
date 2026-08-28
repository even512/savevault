namespace SaveVault.Core.Api;

/// <summary>
/// Die Endpunkt-Pfade des SaveVault-HTTP-Vertrags – der gemeinsame Nenner von Server
/// (implementiert sie) und Client (ruft sie auf). Ein Spielschlüssel im Pfad ist immer
/// URL-kodiert (<see cref="Uri.EscapeDataString"/>); der Server saniert/hasht ihn
/// serverseitig zusätzlich für die Ablage.
/// </summary>
public static class ApiRoutes
{
    public const string Base = "/api";

    // Dashboard-Anmeldung (Benutzer/Passwort; ersetzt das frühere Master-Token)
    public const string Setup = Base + "/setup";     // Ersteinrichtung (nur solange kein Admin existiert)
    public const string Login = Base + "/login";     // Anmeldung → Session-Token
    public const string Logout = Base + "/logout";   // Session beenden

    // Pairing & Gerät
    public const string Pair = Base + "/pair";
    public const string Heartbeat = Base + "/heartbeat";

    // Spiele & Revisionen
    public const string Games = Base + "/games";
    public static string Head(string gameKeyEncoded) => $"{Base}/games/{gameKeyEncoded}/head";
    public static string Revisions(string gameKeyEncoded) => $"{Base}/games/{gameKeyEncoded}/revisions";
    public static string Revision(string gameKeyEncoded, long number) => $"{Base}/games/{gameKeyEncoded}/revisions/{number}";
    public static string Content(string gameKeyEncoded, string hashEncoded) => $"{Base}/games/{gameKeyEncoded}/content/{hashEncoded}";
    public static string Restore(string gameKeyEncoded) => $"{Base}/games/{gameKeyEncoded}/restore";

    /// <summary>Export einer Revision als ZIP (master-only, Dashboard).</summary>
    public static string Export(string gameKeyEncoded, long number) => $"{Base}/games/{gameKeyEncoded}/revisions/{number}/export";

    /// <summary>Box-Art/Cover eines Spiels (master-only, Dashboard); 404 wenn keins.</summary>
    public static string Cover(string gameKeyEncoded) => $"{Base}/games/{gameKeyEncoded}/cover";

    // Konflikte
    public const string Conflicts = Base + "/conflicts";
    public static string ResolveConflict(string conflictIdEncoded) => $"{Base}/conflicts/{conflictIdEncoded}/resolve";

    // Befehls-Warteschlange (Client pollt)
    public static string Commands(string deviceIdEncoded) => $"{Base}/commands?deviceId={deviceIdEncoded}";
    public static string AckCommand(string commandIdEncoded) => $"{Base}/commands/{commandIdEncoded}/ack";

    // Dashboard-Übersichten (master-only; nicht Teil des Client-Vertrags)
    public const string GameStates = Base + "/game-states";
    public const string ServerInfo = Base + "/server-info";
}
