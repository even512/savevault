using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SaveVault.Core.Models;
using SaveVault.Core.Storage;
using SaveVault.Server.Configuration;

namespace SaveVault.Server.Storage;

/// <summary>
/// Beschafft Box-Art/Cover zu einem Spiel über IGDB (wie das dashsharp-Modul „game-releases"):
/// Twitch-OAuth-Token → IGDB-Namenssuche → Cover-Bild von <c>images.igdb.com</c>. Ergebnisse
/// werden auf der Platte zwischengespeichert (<c>&lt;dataRoot&gt;/covers</c>), inkl. Negativ-
/// Markierung, damit erfolglose Suchen nicht ständig wiederholt werden.
///
/// Sicherheit:
/// <list type="bullet">
/// <item>Ausgehende Requests gehen NUR an die fest verdrahteten Hosts
/// <c>id.twitch.tv</c>, <c>api.igdb.com</c>, <c>images.igdb.com</c> (keine vom Client
/// bestimmte Ziel-URL → kein SSRF). Der Spielname geht in den IGDB-Anfragekörper, nie in eine URL.</item>
/// <item>Die <c>image_id</c> aus der IGDB-Antwort wird vor dem Einsetzen in die Bild-URL hart
/// auf <c>[a-z0-9_]</c> gefiltert.</item>
/// <item>Bildgröße ist begrenzt; Zugangsdaten/Token werden nie geloggt oder ausgegeben.</item>
/// <item>Fehler führen NIE zu einer Exception nach außen – der Aufrufer bekommt schlicht
/// „kein Cover" (null).</item>
/// </list>
/// Fehlt die IGDB-Konfiguration, ist der Dienst inaktiv und liefert immer null.
/// </summary>
public sealed class CoverService
{
    private const string TokenUrl = "https://id.twitch.tv/oauth2/token";
    private const string IgdbGamesUrl = "https://api.igdb.com/v4/games";
    private const string ImageBase = "https://images.igdb.com/igdb/image/upload/t_cover_big";
    private const long MaxImageBytes = 8L * 1024 * 1024; // 8 MiB Obergrenze fürs Cover
    private static readonly TimeSpan NegativeCacheTtl = TimeSpan.FromDays(7);

    /// <summary>Name des benannten HttpClients (Timeout/UserAgent, siehe Program.cs).</summary>
    public const string HttpClientName = "igdb";

    private readonly IHttpClientFactory _httpFactory;
    private readonly ServerConfig _cfg;
    private readonly ILogger<CoverService> _logger;
    private readonly string _cacheDir;

    // Pro Spiel-Schlüssel nur EIN gleichzeitiger Beschaffungslauf.
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _perKey = new(StringComparer.Ordinal);

    // Twitch-App-Token (client_credentials) mit Ablauf; erneuern bei Ablauf/401.
    private readonly SemaphoreSlim _tokenGate = new(1, 1);
    private string? _token;
    private DateTime _tokenExpiresUtc;

    public CoverService(IHttpClientFactory httpFactory, ServerConfig cfg, ILogger<CoverService> logger)
    {
        _httpFactory = httpFactory ?? throw new ArgumentNullException(nameof(httpFactory));
        _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cacheDir = Path.Combine(_cfg.DataRoot, "covers");
    }

    private HttpClient NewClient() => _httpFactory.CreateClient(HttpClientName);

    public bool IsEnabled => _cfg.IsCoverEnabled;

    private string CacheFile(GameKey game) => Path.Combine(_cacheDir, PathSanitizer.HashKey(game.Value) + ".jpg");
    private string NegativeMarker(GameKey game) => Path.Combine(_cacheDir, PathSanitizer.HashKey(game.Value) + ".none");

    /// <summary>
    /// Liefert den Pfad zur zwischengespeicherten Cover-Datei des Spiels – bei Bedarf wird sie
    /// zuvor von IGDB geholt. Null, wenn kein Cover verfügbar ist oder der Dienst inaktiv ist.
    /// </summary>
    public async Task<string?> GetCoverFileAsync(GameKey game, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(game);
        if (!IsEnabled)
            return null;

        var file = CacheFile(game);
        if (File.Exists(file))
            return file;

        // Negativ-Cache: kürzlich erfolglos → nicht sofort erneut fragen.
        var marker = NegativeMarker(game);
        if (File.Exists(marker) && DateTime.UtcNow - File.GetLastWriteTimeUtc(marker) < NegativeCacheTtl)
            return null;

        var gate = _perKey.GetOrAdd(game.Value, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Nach dem Warten erneut prüfen (ein paralleler Lauf war evtl. schon erfolgreich).
            if (File.Exists(file))
                return file;

            var ok = await FetchAndCacheAsync(game, file, ct).ConfigureAwait(false);
            if (ok)
                return file;

            TouchNegativeMarker(marker);
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Nie nach außen werfen; Netz-/Parsefehler → „kein Cover".
            _logger.LogDebug("Cover-Beschaffung fehlgeschlagen (Spiel {Key}): {Message}", game.Value, ex.Message);
            TouchNegativeMarker(marker);
            return null;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<bool> FetchAndCacheAsync(GameKey game, string targetFile, CancellationToken ct)
    {
        var imageId = await FindCoverImageIdAsync(game.DisplayName, ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(imageId))
            return false;

        var safeImageId = SanitizeImageId(imageId);
        if (safeImageId.Length == 0)
            return false;

        using var http = NewClient();
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{ImageBase}/{safeImageId}.jpg");
        using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            return false;

        var contentType = resp.Content.Headers.ContentType?.MediaType;
        if (contentType is not null && !contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return false;
        if (resp.Content.Headers.ContentLength is > MaxImageBytes)
            return false;

        Directory.CreateDirectory(_cacheDir);
        var tmp = targetFile + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var dst = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await CopyWithLimitAsync(src, dst, MaxImageBytes, ct).ConfigureAwait(false);
            }

            File.Move(tmp, targetFile, overwrite: true);
            // Etwaige Negativ-Markierung entfernen.
            TryDelete(NegativeMarker(game));
            return true;
        }
        catch
        {
            TryDelete(tmp);
            throw;
        }
    }

    /// <summary>Sucht das Spiel per Name bei IGDB und gibt die <c>image_id</c> des Covers zurück (oder null).</summary>
    private async Task<string?> FindCoverImageIdAsync(string gameName, CancellationToken ct)
    {
        var token = await GetTokenAsync(ct).ConfigureAwait(false);
        if (token is null)
            return null;

        var body = $"search \"{EscapeApicalypse(gameName)}\"; fields name,cover.image_id; limit 5;";

        async Task<HttpResponseMessage> Call(string bearer)
        {
            using var http = NewClient();
            using var req = new HttpRequestMessage(HttpMethod.Post, IgdbGamesUrl)
            {
                Content = new StringContent(body, Encoding.UTF8, "text/plain"),
            };
            req.Headers.TryAddWithoutValidation("Client-ID", _cfg.IgdbClientId);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
            return await http.SendAsync(req, ct).ConfigureAwait(false);
        }

        using var resp = await Call(token).ConfigureAwait(false);
        HttpResponseMessage effective = resp;
        HttpResponseMessage? retry = null;
        try
        {
            if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                // Token abgelaufen/ungültig → einmal frisch holen und wiederholen.
                var fresh = await GetTokenAsync(ct, forceRefresh: true).ConfigureAwait(false);
                if (fresh is null)
                    return null;
                retry = await Call(fresh).ConfigureAwait(false);
                effective = retry;
            }

            if (!effective.IsSuccessStatusCode)
                return null;

            await using var stream = await effective.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return null;

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.TryGetProperty("cover", out var cover)
                    && cover.ValueKind == JsonValueKind.Object
                    && cover.TryGetProperty("image_id", out var imageId)
                    && imageId.ValueKind == JsonValueKind.String)
                {
                    var value = imageId.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                        return value;
                }
            }
            return null;
        }
        finally
        {
            retry?.Dispose();
        }
    }

    /// <summary>Twitch-App-Token (client_credentials), gecacht bis kurz vor Ablauf.</summary>
    private async Task<string?> GetTokenAsync(CancellationToken ct, bool forceRefresh = false)
    {
        if (!forceRefresh && _token is not null && DateTime.UtcNow < _tokenExpiresUtc)
            return _token;

        await _tokenGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!forceRefresh && _token is not null && DateTime.UtcNow < _tokenExpiresUtc)
                return _token;

            var url = $"{TokenUrl}?client_id={Uri.EscapeDataString(_cfg.IgdbClientId!)}"
                    + $"&client_secret={Uri.EscapeDataString(_cfg.IgdbClientSecret!)}"
                    + "&grant_type=client_credentials";

            using var http = NewClient();
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            using var resp = await http.SendAsync(req, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return null;

            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            var root = doc.RootElement;
            if (!root.TryGetProperty("access_token", out var at) || at.ValueKind != JsonValueKind.String)
                return null;

            var seconds = root.TryGetProperty("expires_in", out var ei) && ei.TryGetInt64(out var s) ? s : 3600;
            _token = at.GetString();
            // Sicherheitsabstand von 60 s vor dem echten Ablauf.
            _tokenExpiresUtc = DateTime.UtcNow.AddSeconds(Math.Max(60, seconds - 60));
            return _token;
        }
        finally
        {
            _tokenGate.Release();
        }
    }

    private static async Task CopyWithLimitAsync(Stream src, Stream dst, long maxBytes, CancellationToken ct)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await src.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            total += read;
            if (total > maxBytes)
                throw new InvalidOperationException("Cover-Bild überschreitet die Größengrenze.");
            await dst.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
        }
    }

    /// <summary>Nur Kleinbuchstaben/Ziffern/Unterstrich zulassen – IGDB-image_ids bestehen daraus.</summary>
    private static string SanitizeImageId(string imageId)
    {
        var sb = new StringBuilder(imageId.Length);
        foreach (var ch in imageId)
        {
            if (ch is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_')
                sb.Append(ch);
        }
        return sb.ToString();
    }

    /// <summary>Anführungszeichen/Backslash im Suchbegriff für den Apicalypse-String neutralisieren.</summary>
    private static string EscapeApicalypse(string name)
        => name.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private void TouchNegativeMarker(string marker)
    {
        try
        {
            Directory.CreateDirectory(_cacheDir);
            File.WriteAllBytes(marker, Array.Empty<byte>());
        }
        catch { /* best effort – Negativ-Cache ist nur Optimierung */ }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best effort */ }
    }
}
