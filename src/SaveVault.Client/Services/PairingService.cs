using System.Net.Http;
using SaveVault.Core.Api;

namespace SaveVault.Client.Services;

/// <summary>Ergebnis eines Pairing-Versuchs. Trägt bei Erfolg die neue Geräte-ID (nie den Token).</summary>
public sealed record PairingResult(bool Success, string? DeviceId, string? ErrorMessage)
{
    public static PairingResult Ok(string deviceId) => new(true, deviceId, null);
    public static PairingResult Fail(string message) => new(false, null, message);
}

/// <summary>
/// Führt das einmalige Pairing durch: tauscht Server-URL + Pairing-Code gegen einen
/// Geräte-Token und speichert Server-URL, Geräte-ID, Token und Gerätename in der lokalen
/// <see cref="ClientConfig"/>. Fehlerfälle (ungültige URL, falscher Code, Server nicht
/// erreichbar) werden als <see cref="PairingResult"/> zurückgegeben – nicht als Exception.
///
/// Für die Testbarkeit ist die API-Erzeugung über eine Factory injizierbar; im Betrieb
/// wird ein kurzlebiger <see cref="HttpClient"/> mit der angegebenen Server-URL als
/// BaseAddress verwendet.
/// </summary>
public sealed class PairingService
{
    private readonly ClientConfigStore _configStore;
    private readonly Func<string, ISaveVaultApi> _apiFactory;

    public PairingService(ClientConfigStore configStore, Func<string, ISaveVaultApi>? apiFactory = null)
    {
        _configStore = configStore ?? throw new ArgumentNullException(nameof(configStore));
        _apiFactory = apiFactory ?? DefaultApiFactory;
    }

    /// <summary>Tauscht den Pairing-Code gegen einen Token und persistiert die Konfiguration.</summary>
    public async Task<PairingResult> PairAsync(string serverUrl, string code, string deviceName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(serverUrl))
            return PairingResult.Fail("Es wurde keine Server-URL angegeben.");
        if (string.IsNullOrWhiteSpace(code))
            return PairingResult.Fail("Es wurde kein Pairing-Code angegeben.");

        // URL-Allowlist: nur absolute http/https-URLs (keine file://, keine relativen Ziele).
        if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return PairingResult.Fail("Die Server-URL ist ungültig (erwartet http:// oder https://).");
        }

        var name = string.IsNullOrWhiteSpace(deviceName) ? Environment.MachineName : deviceName.Trim();

        try
        {
            var api = _apiFactory(uri.GetLeftPart(UriPartial.Authority));
            var request = new PairRequest(code.Trim(), name, DeviceIdentity.OsDescription, DeviceIdentity.AgentVersion);
            var response = await api.PairAsync(request, ct).ConfigureAwait(false);

            var config = _configStore.Load();
            config.ServerUrl = uri.GetLeftPart(UriPartial.Authority);
            config.DeviceId = response.DeviceId;
            config.DeviceToken = response.DeviceToken;   // bleibt lokal – nie loggen
            config.DeviceName = name;
            _configStore.Save(config);

            return PairingResult.Ok(response.DeviceId);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            // Kein vom Aufrufer angeforderter Abbruch → HttpClient-Timeout.
            return PairingResult.Fail("Zeitüberschreitung beim Verbinden mit dem Server.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (SaveVaultApiException ex)
        {
            // Häufigster Fall: falscher/abgelaufener Pairing-Code → Server antwortet mit Fehlerstatus.
            return PairingResult.Fail("Pairing abgelehnt: " + ex.Message);
        }
        catch (HttpRequestException ex)
        {
            return PairingResult.Fail("Server nicht erreichbar: " + ex.Message);
        }
    }

    private static ISaveVaultApi DefaultApiFactory(string baseAddress)
    {
        var http = new HttpClient { BaseAddress = new Uri(baseAddress), Timeout = TimeSpan.FromSeconds(30) };
        return new SaveVaultApiClient(http);
    }
}
