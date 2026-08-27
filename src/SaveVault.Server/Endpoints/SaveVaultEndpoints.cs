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
        app.MapGet("/health", (ServerConfig cfg) => Results.Json(new
        {
            status = "ok",
            configured = cfg.IsConfigured,
        }));

        var api = app.MapGroup(ApiRoutes.Base);

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
        api.MapGet("/games", async (VaultStore store, CancellationToken ct)
            => Results.Json(await store.GetGamesAsync(ct)));

        api.MapGet("/games/{gameKey}/head", async (string gameKey, VaultStore store, CancellationToken ct)
            => Results.Json(await store.GetHeadAsync(KeyFrom(gameKey), ct)));

        api.MapGet("/games/{gameKey}/revisions", async (string gameKey, VaultStore store, CancellationToken ct)
            => Results.Json(await store.GetRevisionsAsync(KeyFrom(gameKey), ct)));

        api.MapPost("/games/{gameKey}/revisions",
            async (string gameKey, UploadRevisionRequest req, HttpContext ctx, VaultStore store, CancellationToken ct) =>
            {
                // Attributions-Spoofing verhindern: ein Gerät darf nur unter der EIGENEN Geräte-ID
                // (oder das Master-Token) eine Revision anmelden – analog zum Heartbeat.
                if (req?.Device is null)
                    return ApiResults.Error(400, "Unvollständige Revisionsanmeldung.");
                if (!Principal(ctx).CanActAsDevice(req.Device.Id))
                    return ApiResults.Error(403, "Token gehört zu einem anderen Gerät.");
                return Results.Json(await store.RegisterRevisionAsync(KeyFrom(gameKey), req, ct));
            });

        api.MapGet("/games/{gameKey}/revisions/{number:long}",
            async (string gameKey, long number, VaultStore store, CancellationToken ct)
            => Results.Json(await store.GetRevisionAsync(KeyFrom(gameKey), number, ct)));

        // --- Inhalte (inhaltsadressiert) --------------------------------------------
        api.MapPut("/games/{gameKey}/content/{hash}",
            async (string gameKey, string hash, HttpContext ctx, VaultStore store, CancellationToken ct) =>
            {
                // Große Savegames: die Standard-Body-Grenze nur für diesen Upload-Endpunkt aufheben.
                var sizeFeature = ctx.Features.Get<IHttpMaxRequestBodySizeFeature>();
                if (sizeFeature is { IsReadOnly: false })
                    sizeFeature.MaxRequestBodySize = null;

                await store.StoreContentAsync(KeyFrom(gameKey), hash, ctx.Request.Body, ct);
                // Head erst nach VOLLSTÄNDIGEM Content vorrücken: nach jedem gespeicherten Blob prüfen,
                // ob eine angemeldete Pending-Revision nun komplett ist, und sie dann finalisieren.
                await store.TryFinalizePendingAsync(KeyFrom(gameKey), ct);
                return Results.Ok();
            });

        api.MapGet("/games/{gameKey}/content/{hash}",
            (string gameKey, string hash, VaultStore store) =>
            {
                var stream = store.OpenContent(KeyFrom(gameKey), hash);
                return stream is null
                    ? ApiResults.Error(404, "Inhalt nicht gefunden.")
                    : Results.Stream(stream, "application/octet-stream");
            });

        // --- Restore ----------------------------------------------------------------
        api.MapPost("/games/{gameKey}/restore",
            async (string gameKey, RestoreRequest req, VaultStore store, CancellationToken ct)
            => Results.Json(await store.RestoreAsync(KeyFrom(gameKey), req, ct)));

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

        // Server-Info für die Einstellungen (echte Werte aus Config + Umgebung). Master-only.
        // Gibt bewusst KEIN Secret aus (nie das Master-Token).
        api.MapGet("/server-info", (HttpContext ctx, ServerConfig cfg) =>
        {
            if (!Principal(ctx).IsMaster) return AdminOnly();
            return Results.Json(new
            {
                port = cfg.Port,
                dataRoot = cfg.DataRoot,
                configured = cfg.IsConfigured,
                container = Environment.MachineName,
                version = ServerVersion,
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

    private static GameKey KeyFrom(string routeValue)
    {
        if (string.IsNullOrWhiteSpace(routeValue))
            throw new VaultException(400, "Leerer Spielschlüssel.");
        return new GameKey(routeValue, routeValue);
    }
}
