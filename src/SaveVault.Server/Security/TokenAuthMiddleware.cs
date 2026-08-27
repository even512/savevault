using System.Net.Http.Headers;
using SaveVault.Core.Api;
using SaveVault.Server.Configuration;
using SaveVault.Server.Endpoints;
using SaveVault.Server.Storage;

namespace SaveVault.Server.Security;

/// <summary>
/// Bewacht alle <c>/api</c>-Endpunkte:
///   * Ist der Server nicht konfiguriert (kein <c>SAVEVAULT_TOKEN</c>) → 503 mit klarer Meldung
///     für JEDEN API-Aufruf (kein Absturz, klarer „nicht eingerichtet"-Pfad).
///   * <c>POST /api/pair</c> ist die einzige token-freie Operation (Einlösung des Pairing-Codes).
///   * Sonst ist ein gültiger Bearer-Token Pflicht: entweder das Master-Token (Admin/Dashboard)
///     oder ein bekannter Geräte-Token. Fehlt/ungültig → 401.
/// Der erkannte <see cref="AuthPrincipal"/> wird in <c>HttpContext.Items</c> abgelegt.
/// Nicht-API-Pfade (statisches Dashboard, /health) laufen unberührt durch.
/// </summary>
public sealed class TokenAuthMiddleware
{
    public const string PrincipalKey = "savevault.principal";

    private readonly RequestDelegate _next;
    private readonly ServerConfig _config;

    public TokenAuthMiddleware(RequestDelegate next, ServerConfig config)
    {
        _next = next;
        _config = config;
    }

    public async Task InvokeAsync(HttpContext context, VaultStore store)
    {
        var path = context.Request.Path;

        // Alles außerhalb von /api (statische Dashboard-Dateien, /health) passiert ungehindert.
        if (!path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        // Ohne Master-Token verweigert der Server jeden API-Aufruf mit klarer Meldung.
        if (!_config.IsConfigured)
        {
            await WriteAsync(context, ApiResults.Error(503,
                "Server ist nicht eingerichtet: SAVEVAULT_TOKEN fehlt. Bitte Token setzen und neu starten."));
            return;
        }

        // Pairing-Einlösung ist bewusst token-frei (der Pairing-Code selbst ist das Geheimnis).
        var isPair = path.Equals(ApiRoutes.Pair, StringComparison.OrdinalIgnoreCase)
                     && HttpMethods.IsPost(context.Request.Method);
        if (isPair)
        {
            await _next(context);
            return;
        }

        var token = ExtractBearer(context.Request.Headers.Authorization);
        if (string.IsNullOrEmpty(token))
        {
            await WriteAsync(context, ApiResults.Error(401, "Kein Token angegeben (Authorization: Bearer …)."));
            return;
        }

        var principal = await AuthenticateAsync(token, store, context.RequestAborted);
        if (principal is null)
        {
            await WriteAsync(context, ApiResults.Error(401, "Ungültiger Token."));
            return;
        }

        context.Items[PrincipalKey] = principal;
        await _next(context);
    }

    private async Task<AuthPrincipal?> AuthenticateAsync(string token, VaultStore store, CancellationToken ct)
    {
        // Master-Token (konstant-zeitiger Vergleich).
        if (!string.IsNullOrEmpty(_config.MasterToken) && Secrets.FixedTimeEquals(token, _config.MasterToken))
            return AuthPrincipal.Master;

        // Sonst gegen die bekannten Geräte-Token (nur als Hash gespeichert) prüfen.
        return await store.ResolveDeviceTokenAsync(token, ct);
    }

    private static string? ExtractBearer(string? headerValue)
    {
        if (string.IsNullOrWhiteSpace(headerValue)) return null;
        if (!AuthenticationHeaderValue.TryParse(headerValue, out var parsed)) return null;
        if (!string.Equals(parsed.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)) return null;
        return string.IsNullOrWhiteSpace(parsed.Parameter) ? null : parsed.Parameter.Trim();
    }

    private static async Task WriteAsync(HttpContext context, IResult result)
        => await result.ExecuteAsync(context);
}
