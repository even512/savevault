using System.Net.Http.Headers;
using SaveVault.Core.Api;
using SaveVault.Server.Configuration;
using SaveVault.Server.Endpoints;
using SaveVault.Server.Storage;

namespace SaveVault.Server.Security;

/// <summary>
/// Bewacht alle <c>/api</c>-Endpunkte:
///   * <c>POST /api/setup</c> und <c>POST /api/login</c> sind IMMER token-frei (Ersteinrichtung
///     und Anmeldung erzeugen erst den Zugang). <c>/api/setup</c> setzt sich selbst außer Kraft,
///     sobald ein Konto existiert (→ 409 im Endpunkt).
///   * Existiert noch KEIN Admin-Konto (Server nicht eingerichtet) → 503 für alle übrigen
///     API-Aufrufe, mit klarer Meldung (das Dashboard zeigt dann die Ersteinrichtung).
///   * <c>POST /api/pair</c> ist token-frei (der Pairing-Code selbst ist das Geheimnis).
///   * Sonst ist ein gültiger Bearer-Token Pflicht: entweder ein Dashboard-Session-Token
///     (Master/Admin) oder ein bekannter Geräte-Token. Fehlt/ungültig → 401.
/// Der erkannte <see cref="AuthPrincipal"/> wird in <c>HttpContext.Items</c> abgelegt.
/// Nicht-API-Pfade (statisches Dashboard, /health) laufen unberührt durch.
/// </summary>
public sealed class TokenAuthMiddleware
{
    public const string PrincipalKey = "savevault.principal";

    private readonly RequestDelegate _next;

    public TokenAuthMiddleware(RequestDelegate next)
    {
        _next = next;
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

        var isPost = HttpMethods.IsPost(context.Request.Method);

        // Ersteinrichtung + Anmeldung sind immer token-frei (die Endpunkte prüfen selbst ihre Regeln).
        var isSetup = isPost && path.Equals(ApiRoutes.Setup, StringComparison.OrdinalIgnoreCase);
        var isLogin = isPost && path.Equals(ApiRoutes.Login, StringComparison.OrdinalIgnoreCase);
        if (isSetup || isLogin)
        {
            await _next(context);
            return;
        }

        // Ohne eingerichtetes Admin-Konto verweigert der Server jeden weiteren API-Aufruf.
        if (!await store.HasAdminAsync(context.RequestAborted))
        {
            await WriteAsync(context, ApiResults.Error(503,
                "Server ist noch nicht eingerichtet. Bitte im Web-Dashboard ein Benutzerkonto anlegen."));
            return;
        }

        // Pairing-Einlösung ist bewusst token-frei (der Pairing-Code selbst ist das Geheimnis).
        var isPair = isPost && path.Equals(ApiRoutes.Pair, StringComparison.OrdinalIgnoreCase);
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
            await WriteAsync(context, ApiResults.Error(401, "Nicht angemeldet oder Sitzung abgelaufen."));
            return;
        }

        context.Items[PrincipalKey] = principal;
        await _next(context);
    }

    private static async Task<AuthPrincipal?> AuthenticateAsync(string token, VaultStore store, CancellationToken ct)
    {
        // Dashboard-Session (Master/Admin)?
        var session = await store.ResolveSessionAsync(token, ct);
        if (session is not null)
            return session;

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
