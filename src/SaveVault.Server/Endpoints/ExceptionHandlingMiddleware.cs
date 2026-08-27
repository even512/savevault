using SaveVault.Server.Storage;

namespace SaveVault.Server.Endpoints;

/// <summary>
/// Fängt Fehler zentral ab: eine <see cref="VaultException"/> wird zu ihrem HTTP-Status mit
/// der (deutschen, secret-freien) Meldung; jeder andere Fehler wird als generisches 500
/// beantwortet und serverseitig geloggt – nie geht ein Stacktrace oder Secret an den Client.
/// </summary>
public sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (VaultException ex)
        {
            await WriteError(context, ex.StatusCode, ex.Message);
        }
        catch (BadHttpRequestException ex)
        {
            // Kaputter/zu großer Request-Body, ungültiges JSON o. Ä.
            await WriteError(context, 400, "Ungültige Anfrage: " + ex.Message);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Client hat abgebrochen – keine Antwort nötig, kein Fehler-Log.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unerwarteter Serverfehler bei {Method} {Path}",
                context.Request.Method, context.Request.Path);
            await WriteError(context, 500, "Interner Serverfehler.");
        }
    }

    private static async Task WriteError(HttpContext context, int statusCode, string message)
    {
        if (context.Response.HasStarted)
            return; // Antwort läuft schon – nichts mehr zu retten
        await ApiResults.Error(statusCode, message).ExecuteAsync(context);
    }
}
