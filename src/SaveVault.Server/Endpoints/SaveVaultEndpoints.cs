using Microsoft.AspNetCore.Http.Features;
using SaveVault.Core.Api;
using SaveVault.Core.Models;
using SaveVault.Server.Configuration;
using SaveVault.Server.Security;
using SaveVault.Server.Storage;

namespace SaveVault.Server.Endpoints;

/// <summary>
/// Bildet den kompletten <see cref="ISaveVaultApi"/>-/<see cref="ApiRoutes"/>-Vertrag auf HTTP ab
/// und ergänzt die vom Web-Dashboard (Schritt 4) benötigten Zusatz-Endpunkte (Geräte-Liste,
/// Verlauf, Pairing-Code). Alle fremd gelieferten Spielschlüssel/Hashes werden serverseitig über
/// den Core (StoragePaths/PathSanitizer) gehasht/saniert – nie roh als Pfad benutzt.
/// </summary>
public static class SaveVaultEndpoints
{
    public static void MapSaveVault(this WebApplication app)
    {
        // --- Health (ohne Auth) -----------------------------------------------------
        // needsSetup steuert das Dashboard: true → Ersteinrichtung anzeigen, sonst Login.
        app.MapGet("/health", async (VaultStore store, CancellationToken ct) => Results.Json(new
        {
            status = "ok",
            needsSetup = !await store.HasAdminAsync(ct),
        }));

        var api = app.MapGroup(ApiRoutes.Base);

        // --- Dashboard-Anmeldung (token-frei; ersetzt das frühere Master-Token) ------
        // Ersteinrichtung: legt das einzige Admin-Konto an – nur solange keins existiert (sonst 409).
        api.MapPost("/setup", async (SetupRequest req, VaultStore store, CancellationToken ct) =>
        {
            if (req is null) return ApiResults.Error(400, "Ungültige Anfrage.");
            return Results.Json(await store.SetupAdminAsync(req.Username, req.Password, ct));
        });

        // Anmeldung mit Benutzername + Passwort → Session-Token (ratenbegrenzt im Store).
        api.MapPost("/login", async (LoginRequest req, VaultStore store, CancellationToken ct) =>
        {
            if (req is null) return ApiResults.Error(400, "Ungültige Anfrage.");
            return Results.Json(await store.LoginAsync(req.Username, req.Password, ct));
        });

        // Abmelden: beendet die zum vorgelegten Session-Token gehörende Sitzung.
        api.MapPost("/logout", async (HttpContext ctx, VaultStore store, CancellationToken ct) =>
        {
            var token = BearerToken(ctx);
            if (token is not null)
                await store.LogoutAsync(token, ct);
            return Results.Ok();
        });

        // --- Pairing & Heartbeat ----------------------------------------------------
        api.MapPost("/pair", async (PairRequest req, VaultStore store, CancellationToken ct)
            => Results.Json(await store.PairAsync(req, ct)));

        api.MapPost("/heartbeat", async (HeartbeatRequest req, HttpContext ctx, VaultStore store, CancellationToken ct) =>
        {
            var principal = Principal(ctx);
            if (req.Device is null)
                return ApiResults.Error(400, "Heartbeat ohne Geräteangabe.");
            if (!principal.CanActAsDevice(req.Device.Id))
                return ApiResults.Error(403, "Token gehört zu einem anderen Gerät.");
            // Client-IP serverseitig aus der Verbindung ableiten (nie vom Client gemeldet).
            return Results.Json(await store.HeartbeatAsync(req, ClientIp(ctx), ct));
        });

        // --- Spiele & Revisionen ----------------------------------------------------
        // Master-only: die Liste enthält seit dem Per-Gerät-Umbau die effektiven Bucket-Schlüssel
        // (privat: dev|{ownerDeviceId}|…) und damit fremde Geräte-IDs/Bibliotheken – das gehört nur
        // ins Dashboard, nicht zu einem beliebigen Geräte-Token. Der Client nutzt diese Route nicht.
        api.MapGet("/games", async (HttpContext ctx, VaultStore store, CancellationToken ct) =>
        {
            if (!Principal(ctx).IsMaster) return AdminOnly();
            return Results.Json(await store.GetGamesAsync(ct));
        });

        api.MapGet("/games/{gameKey}/head", async (string gameKey, string? scope, HttpContext ctx, VaultStore store, CancellationToken ct)
            => Results.Json(await store.GetHeadAsync(ResolveGameKey(ctx, gameKey, scope), ct)));

        api.MapGet("/games/{gameKey}/revisions", async (string gameKey, string? scope, HttpContext ctx, VaultStore store, CancellationToken ct)
            => Results.Json(await store.GetRevisionsAsync(ResolveGameKey(ctx, gameKey, scope), ct)));

        api.MapPost("/games/{gameKey}/revisions",
            async (string gameKey, string? scope, UploadRevisionRequest req, HttpContext ctx, VaultStore store, CancellationToken ct) =>
            {
                // Attributions-Spoofing verhindern: ein Gerät darf nur unter der EIGENEN Geräte-ID
                // (oder das Master-Token) eine Revision anmelden – analog zum Heartbeat.
                if (req?.Device is null)
                    return ApiResults.Error(400, "Unvollständige Revisionsanmeldung.");
                if (!Principal(ctx).CanActAsDevice(req.Device.Id))
                    return ApiResults.Error(403, "Token gehört zu einem anderen Gerät.");
                return Results.Json(await store.RegisterRevisionAsync(ResolveGameKey(ctx, gameKey, scope), req, ct));
            });

        api.MapGet("/games/{gameKey}/revisions/{number:long}",
            async (string gameKey, long number, string? scope, HttpContext ctx, VaultStore store, CancellationToken ct)
            => Results.Json(await store.GetRevisionAsync(ResolveGameKey(ctx, gameKey, scope), number, ct)));

        // Export einer Revision als ZIP (Originalstruktur der Savegames + SaveVault-Info.txt mit
        // dem Standard-Save-Pfad). Master-only (Dashboard). Der 404-Fall (unbekannte Revision) wird
        // von GetRevisionAsync geworfen, BEVOR in den Body gestreamt wird → saubere Fehlerantwort.
        api.MapGet("/games/{gameKey}/revisions/{number:long}/export",
            async (string gameKey, long number, string? scope, HttpContext ctx, VaultStore store, CancellationToken ct) =>
            {
                if (!Principal(ctx).IsMaster) return AdminOnly();

                var key = ResolveGameKey(ctx, gameKey, scope);
                var rev = await store.GetRevisionAsync(key, number, ct);
                var deviceName = await store.GetDeviceNameAsync(rev.DeviceId, ct) ?? rev.DeviceId;

                // ZipArchive schreibt sein Central Directory beim Dispose synchron – Kestrel verbietet
                // synchrones IO am Response-Body per Default. Für diesen (seltenen, master-only)
                // Download gezielt erlauben, damit der ZIP direkt gestreamt werden kann.
                var bodyControl = ctx.Features.Get<IHttpBodyControlFeature>();
                if (bodyControl is not null)
                    bodyControl.AllowSynchronousIO = true;

                ctx.Response.ContentType = "application/zip";
                ctx.Response.Headers.ContentDisposition =
                    $"attachment; filename=\"{RevisionExporter.SuggestFileName(rev)}\"";
                await RevisionExporter.WriteZipAsync(
                    rev, deviceName, sha => store.OpenContent(key, sha), ctx.Response.Body, ct);
                return Results.Empty;
            });

        // --- Inhalte (inhaltsadressiert) --------------------------------------------
        api.MapPut("/games/{gameKey}/content/{hash}",
            async (string gameKey, string hash, string? scope, HttpContext ctx, VaultStore store, CancellationToken ct) =>
            {
                // Große Savegames: die Standard-Body-Grenze nur für diesen Upload-Endpunkt aufheben.
                var sizeFeature = ctx.Features.Get<IHttpMaxRequestBodySizeFeature>();
                if (sizeFeature is { IsReadOnly: false })
                    sizeFeature.MaxRequestBodySize = null;

                var key = ResolveGameKey(ctx, gameKey, scope);
                await store.StoreContentAsync(key, hash, ctx.Request.Body, ct);
                // Head erst nach VOLLSTÄNDIGEM Content vorrücken: nach jedem gespeicherten Blob prüfen,
                // ob eine angemeldete Pending-Revision nun komplett ist, und sie dann finalisieren.
                await store.TryFinalizePendingAsync(key, ct);
                return Results.Ok();
            });

        api.MapGet("/games/{gameKey}/content/{hash}",
            (string gameKey, string hash, string? scope, HttpContext ctx, VaultStore store) =>
            {
                var stream = store.OpenContent(ResolveGameKey(ctx, gameKey, scope), hash);
                return stream is null
                    ? ApiResults.Error(404, "Inhalt nicht gefunden.")
                    : Results.Stream(stream, "application/octet-stream");
            });

        // --- Restore ----------------------------------------------------------------
        api.MapPost("/games/{gameKey}/restore",
            async (string gameKey, string? scope, RestoreRequest req, HttpContext ctx, VaultStore store, CancellationToken ct)
            => Results.Json(await store.RestoreAsync(ResolveGameKey(ctx, gameKey, scope), req, ct)));

        // --- Konflikte --------------------------------------------------------------
        api.MapGet("/conflicts", async (VaultStore store, CancellationToken ct)
            => Results.Json(await store.GetConflictsAsync(ct)));

        api.MapPost("/conflicts/{conflictId}/resolve",
            async (string conflictId, ResolveConflictRequest req, VaultStore store, CancellationToken ct)
            => Results.Json(await store.ResolveConflictAsync(conflictId, req, ct)));

        // --- Befehls-Warteschlange --------------------------------------------------
        api.MapGet("/commands", async (string? deviceId, HttpContext ctx, VaultStore store, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(deviceId))
                return ApiResults.Error(400, "deviceId fehlt.");
            if (!Principal(ctx).CanActAsDevice(deviceId))
                return ApiResults.Error(403, "Fremde Befehls-Warteschlange.");
            return Results.Json(await store.GetCommandsAsync(deviceId, ct));
        });

        api.MapPost("/commands/{commandId}/ack",
            async (string commandId, HttpContext ctx, VaultStore store, CancellationToken ct)
            => Results.Json(await store.AckCommandAsync(commandId, Principal(ctx), ct)));

        // --- Dashboard-Zusätze (nicht im Client-Vertrag, aber vom Web-UI gebraucht) --
        // Diese Endpunkte geben Administrations-/Übersichtsdaten preis (alle Geräte, Verlauf) bzw.
        // steuern das Pairing – daher NUR mit dem Master-Token (Dashboard), nie mit einem
        // Geräte-Token. Ein Geräte-Token darf hier nichts sehen/ändern (→ 403).
        api.MapGet("/devices", async (HttpContext ctx, VaultStore store, CancellationToken ct) =>
        {
            if (!Principal(ctx).IsMaster) return AdminOnly();
            return Results.Json(await store.ListDevicesAsync(ct));
        });

        api.MapGet("/activity", async (int? limit, HttpContext ctx, VaultStore store, CancellationToken ct) =>
        {
            if (!Principal(ctx).IsMaster) return AdminOnly();
            return Results.Json(await store.GetActivityAsync(limit ?? 100, ct));
        });

        api.MapGet("/pairing-code", async (HttpContext ctx, VaultStore store, CancellationToken ct) =>
        {
            if (!Principal(ctx).IsMaster) return AdminOnly();
            var (code, updated) = await store.GetPairingCodeAsync(ct);
            return Results.Json(new { code, updatedUtc = updated });
        });

        api.MapPost("/pairing-code/regenerate", async (HttpContext ctx, VaultStore store, CancellationToken ct) =>
        {
            if (!Principal(ctx).IsMaster) return AdminOnly();
            var (code, updated) = await store.RegeneratePairingCodeAsync(ct);
            return Results.Json(new { code, updatedUtc = updated });
        });

        // Per-Spiel-Geräte-Status (fürs Spiel-Drawer). Master-only wie /devices und /activity.
        api.MapGet("/game-states", async (HttpContext ctx, VaultStore store, CancellationToken ct) =>
        {
            if (!Principal(ctx).IsMaster) return AdminOnly();
            return Results.Json(await store.GetGameStatesAsync(ct));
        });

        // Box-Art/Cover eines Spiels (aus dem Platten-Cache, sonst on-demand via IGDB). Für jedes
        // gültig authentifizierte Prinzipal lesbar – gekoppeltes Gerät ODER Master (konsistent zur
        // bereits geräte-lesbaren /revisions-Route). Die Token-Middleware der /api-Gruppe bleibt
        // davor: anonymer Zugriff ist weiterhin gesperrt. Liefert das Bild (image/jpeg) oder 404,
        // wenn keins verfügbar ist / das Feature inaktiv ist.
        api.MapGet("/games/{gameKey}/cover", async (string gameKey, CoverService covers, CancellationToken ct) =>
        {
            var file = await covers.GetCoverFileAsync(KeyFrom(gameKey), ct);
            return file is null
                ? ApiResults.Error(404, "Kein Cover verfügbar.")
                : Results.File(file, "image/jpeg");
        });

        // Server-Info für die Einstellungen (echte Werte aus Config + Umgebung). Master-only.
        // Gibt bewusst KEIN Secret aus (nie das Master-Token).
        api.MapGet("/server-info", (HttpContext ctx, ServerConfig cfg) =>
        {
            if (!Principal(ctx).IsMaster) return AdminOnly();
            return Results.Json(new
            {
                port = cfg.Port,
                dataRoot = cfg.DataRoot,
                configured = true, // erreichbar nur mit gültiger Sitzung → Server ist eingerichtet
                container = Environment.MachineName,
                version = ServerVersion,
                // Ob der Server die IGDB-Zugangsdaten sieht (Box-Art aktiv). Diagnose fürs Dashboard;
                // gibt kein Secret preis, nur den An/Aus-Zustand.
                coverEnabled = cfg.IsCoverEnabled,
            });
        });
    }

    /// <summary>Assembly-Version der Server-Assembly (robust; Fallback „?", falls nicht ermittelbar).</summary>
    private static readonly string ServerVersion =
        typeof(SaveVaultEndpoints).Assembly.GetName().Version?.ToString() ?? "?";

    /// <summary>
    /// Ermittelt die Client-IP aus der Verbindung. IPv4-mapped-IPv6-Adressen (<c>::ffff:…</c>)
    /// werden auf ihre IPv4-Form reduziert. Null, wenn keine Adresse vorliegt.
    /// </summary>
    private static string? ClientIp(HttpContext ctx)
    {
        var ip = ctx.Connection.RemoteIpAddress;
        if (ip is null) return null;
        if (ip.IsIPv4MappedToIPv6)
            ip = ip.MapToIPv4();
        return ip.ToString();
    }

    private static IResult AdminOnly()
        => ApiResults.Error(403, "Nur mit dem Master-Token (Dashboard) erlaubt.");

    private static AuthPrincipal Principal(HttpContext ctx)
        => ctx.Items[TokenAuthMiddleware.PrincipalKey] as AuthPrincipal
           ?? throw new VaultException(401, "Kein authentifizierter Kontext.");

    /// <summary>Extrahiert den rohen Bearer-Token aus dem Authorization-Header (oder null).</summary>
    private static string? BearerToken(HttpContext ctx)
    {
        var header = ctx.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(header)) return null;
        const string prefix = "Bearer ";
        if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        var token = header[prefix.Length..].Trim();
        return token.Length == 0 ? null : token;
    }

    private static GameKey KeyFrom(string routeValue)
    {
        if (string.IsNullOrWhiteSpace(routeValue))
            throw new VaultException(400, "Leerer Spielschlüssel.");
        return new GameKey(routeValue, routeValue);
    }

    /// <summary>
    /// Löst den effektiven Bucket-Schlüssel aus Routenschlüssel + <c>?scope=</c> + Prinzipal auf.
    /// Der Owner eines privaten Buckets wird IMMER aus dem authentifizierten Gerät abgeleitet, nie
    /// aus dem Query – so kann ein Gerät nur seinen EIGENEN privaten Bucket ansprechen (Isolation).
    /// Default ohne Scope: Gerät → privat, Master/Dashboard → legacy (roher Schlüssel, wie bisher).
    /// </summary>
    private static GameKey ResolveGameKey(HttpContext ctx, string routeValue, string? scope)
    {
        var principal = Principal(ctx);
        var fallback = principal.IsMaster ? BucketScope.Legacy : BucketScope.Private;

        BucketScope resolved;
        try { resolved = BucketKey.FromWire(scope, fallback); }
        catch (ArgumentException) { throw new VaultException(400, "Unbekannter Bucket-Scope."); }

        // Legacy-Buckets (alter globaler Verlauf) sind eingefroren: nur das Dashboard (Master) darf
        // sie noch lesen/exportieren. Ein Geräte-Token, das ausdrücklich ?scope=legacy schickt, wird
        // abgewiesen – sonst könnte ein Gerät den alten globalen Bucket wieder beschreiben und den
        // Konflikt-Sturm neu auslösen, den die Migration gerade beseitigt.
        if (resolved == BucketScope.Legacy && !principal.IsMaster)
            throw new VaultException(403, "Legacy-Buckets sind nur über das Dashboard zugänglich.");

        var raw = KeyFrom(routeValue);
        if (resolved != BucketScope.Private)
            return BucketKey.Resolve(raw, resolved, null);

        // Privat: Owner = authentifiziertes Gerät (nie Client-gewählt).
        if (string.IsNullOrWhiteSpace(principal.DeviceId))
            throw new VaultException(400, "Privater Bucket erfordert einen Gerätekontext.");
        return BucketKey.Resolve(raw, BucketScope.Private, principal.DeviceId);
    }
}
