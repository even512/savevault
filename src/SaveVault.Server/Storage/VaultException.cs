namespace SaveVault.Server.Storage;

/// <summary>
/// Erwarteter, auf einen HTTP-Status abbildbarer Fehlerzustand (z. B. 404 unbekanntes Spiel,
/// 409 veraltete Basis-Revision, 400 ungültige Eingabe). Die Meldung ist bewusst deutsch und
/// nutzertauglich und enthält NIE ein Secret. Unerwartete Fehler bleiben normale Exceptions
/// (→ 500, generische Meldung).
/// </summary>
public sealed class VaultException : Exception
{
    public int StatusCode { get; }

    public VaultException(int statusCode, string message) : base(message)
        => StatusCode = statusCode;
}
