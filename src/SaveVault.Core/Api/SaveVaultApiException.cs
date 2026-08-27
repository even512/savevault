using System.Net;

namespace SaveVault.Core.Api;

/// <summary>
/// Fehler bei einem API-Aufruf (nicht-erfolgreicher Status oder leere/ungültige Antwort).
/// Trägt – wo vorhanden – den HTTP-Status und den (gekürzten) Antwort-Body, damit der
/// Aufrufer z. B. „Server offline" von „falscher Pairing-Code" unterscheiden kann.
/// </summary>
public sealed class SaveVaultApiException : Exception
{
    public HttpStatusCode? StatusCode { get; }
    public string? ResponseBody { get; }

    public SaveVaultApiException(
        string message,
        HttpStatusCode? statusCode = null,
        string? responseBody = null,
        Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }
}
