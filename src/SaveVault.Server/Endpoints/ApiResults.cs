namespace SaveVault.Server.Endpoints;

/// <summary>
/// Einheitliche JSON-Fehlerantworten (<c>{ "error": "..." }</c>) mit deutscher, nutzertauglicher
/// Meldung. Nie werden Stacktraces oder Secrets an den Client gegeben.
/// </summary>
public static class ApiResults
{
    public static IResult Error(int statusCode, string message)
        => Results.Json(new ErrorBody(message), statusCode: statusCode);

    public sealed record ErrorBody(string Error);
}
