using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SaveVault.Core.Models;
using SaveVault.Core.Serialization;

namespace SaveVault.Core.Api;

/// <summary>
/// HttpClient-basierte Implementierung des <see cref="ISaveVaultApi"/>-Vertrags. Hält
/// die HTTP-Ausführung bewusst DÜNN: (De-)Serialisierung über die gemeinsamen
/// <see cref="SaveVaultJson"/>-Optionen, einheitliche Fehlerbehandlung über
/// <see cref="SaveVaultApiException"/>, Bearer-Token im Authorization-Header.
///
/// Der <see cref="HttpClient"/> wird injiziert (BaseAddress dort setzen). So bleibt der
/// Client testbar und die Lebensdauer-Verwaltung liegt beim Aufrufer.
/// </summary>
public sealed class SaveVaultApiClient : ISaveVaultApi
{
    private readonly HttpClient _http;
    private readonly JsonSerializerOptions _json;

    public SaveVaultApiClient(HttpClient http, string? deviceToken = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _json = SaveVaultJson.Options;
        if (!string.IsNullOrWhiteSpace(deviceToken))
            SetToken(deviceToken!);
    }

    /// <summary>Setzt (oder ersetzt) den Bearer-Geräte-Token für alle Folgeaufrufe.</summary>
    public void SetToken(string deviceToken)
        => _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", deviceToken);

    public Task<PairResponse> PairAsync(PairRequest request, CancellationToken ct = default)
        => PostJsonAsync<PairRequest, PairResponse>(ApiRoutes.Pair, request, ct);

    public Task<HeartbeatResponse> HeartbeatAsync(HeartbeatRequest request, CancellationToken ct = default)
        => PostJsonAsync<HeartbeatRequest, HeartbeatResponse>(ApiRoutes.Heartbeat, request, ct);

    public Task<GamesResponse> GetGamesAsync(CancellationToken ct = default)
        => GetJsonAsync<GamesResponse>(ApiRoutes.Games, ct);

    public Task<RevisionHead> GetHeadAsync(GameKey game, BucketScope scope = BucketScope.Private, CancellationToken ct = default)
        => GetJsonAsync<RevisionHead>(ApiRoutes.Head(Key(game), scope), ct);

    public Task<RevisionListResponse> GetRevisionsAsync(GameKey game, BucketScope scope = BucketScope.Private, CancellationToken ct = default)
        => GetJsonAsync<RevisionListResponse>(ApiRoutes.Revisions(Key(game), scope), ct);

    public async Task<byte[]?> GetCoverAsync(GameKey game, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(game);
        // gameKey wird über Key(...) URL-kodiert (EscapeDataString) – identisch zu allen anderen
        // Routen. Der Server hasht/saniert ihn zusätzlich serverseitig (KeyFrom → StoragePaths).
        var url = ApiRoutes.Cover(Key(game));
        using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
        if (resp.StatusCode == HttpStatusCode.NotFound)
            return null; // kein Cover vorhanden – der Aufrufer nutzt den Fallback.
        await EnsureSuccessAsync(resp, url).ConfigureAwait(false);
        return await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
    }

    public Task<RevisionDownload> GetRevisionAsync(GameKey game, long revision, BucketScope scope = BucketScope.Private, CancellationToken ct = default)
        => GetJsonAsync<RevisionDownload>(ApiRoutes.Revision(Key(game), revision, scope), ct);

    public Task<UploadRevisionResponse> UploadRevisionAsync(GameKey game, UploadRevisionRequest request, BucketScope scope = BucketScope.Private, CancellationToken ct = default)
        => PostJsonAsync<UploadRevisionRequest, UploadRevisionResponse>(ApiRoutes.Revisions(Key(game), scope), request, ct);

    public async Task UploadContentAsync(GameKey game, string sha256, Stream content, BucketScope scope = BucketScope.Private, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var url = ApiRoutes.Content(Key(game), Uri.EscapeDataString(sha256), scope);
        using var req = new HttpRequestMessage(HttpMethod.Put, url) { Content = new StreamContent(content) };
        req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(resp, url).ConfigureAwait(false);
    }

    public async Task<Stream> DownloadContentAsync(GameKey game, string sha256, BucketScope scope = BucketScope.Private, CancellationToken ct = default)
    {
        var url = ApiRoutes.Content(Key(game), Uri.EscapeDataString(sha256), scope);
        var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(resp, url).ConfigureAwait(false);
        // Bewusst kein using: der zurückgegebene Stream hält die Antwort am Leben,
        // der Aufrufer schließt ihn (siehe Interface-Doku).
        return await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
    }

    public Task<ConflictListResponse> GetConflictsAsync(CancellationToken ct = default)
        => GetJsonAsync<ConflictListResponse>(ApiRoutes.Conflicts, ct);

    public Task<ResolveConflictResponse> ResolveConflictAsync(string conflictId, ResolveConflictRequest request, CancellationToken ct = default)
        => PostJsonAsync<ResolveConflictRequest, ResolveConflictResponse>(
            ApiRoutes.ResolveConflict(Uri.EscapeDataString(conflictId)), request, ct);

    public Task<RestoreResponse> RestoreAsync(GameKey game, RestoreRequest request, CancellationToken ct = default)
        => PostJsonAsync<RestoreRequest, RestoreResponse>(ApiRoutes.Restore(Key(game), BucketScope.Private), request, ct);

    public Task<CommandListResponse> GetCommandsAsync(string deviceId, CancellationToken ct = default)
        => GetJsonAsync<CommandListResponse>(ApiRoutes.Commands(Uri.EscapeDataString(deviceId)), ct);

    public Task<AckResponse> AckCommandAsync(string commandId, CancellationToken ct = default)
        => PostJsonAsync<object, AckResponse>(ApiRoutes.AckCommand(Uri.EscapeDataString(commandId)), new { }, ct);

    // --- interne HTTP-Helfer (dünn) ------------------------------------------------

    private static string Key(GameKey game) => Uri.EscapeDataString(game.Value);

    private async Task<T> GetJsonAsync<T>(string url, CancellationToken ct) where T : class
    {
        using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(resp, url).ConfigureAwait(false);
        var result = await resp.Content.ReadFromJsonAsync<T>(_json, ct).ConfigureAwait(false);
        return result ?? throw new SaveVaultApiException($"Leere Antwort von {url}.");
    }

    private async Task<TResponse> PostJsonAsync<TRequest, TResponse>(string url, TRequest body, CancellationToken ct)
        where TResponse : class
    {
        using var resp = await _http.PostAsJsonAsync(url, body, _json, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(resp, url).ConfigureAwait(false);
        var result = await resp.Content.ReadFromJsonAsync<TResponse>(_json, ct).ConfigureAwait(false);
        return result ?? throw new SaveVaultApiException($"Leere Antwort von {url}.");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string url)
    {
        if (response.IsSuccessStatusCode)
            return;

        string body = string.Empty;
        try
        {
            body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        }
        catch
        {
            // Body ist optional – der Status trägt die wesentliche Information.
        }

        if (body.Length > 512)
            body = body[..512];

        throw new SaveVaultApiException(
            $"HTTP {(int)response.StatusCode} ({response.ReasonPhrase}) bei {url}.",
            response.StatusCode,
            body);
    }
}
