using System.Net.Http;
using SaveVault.Core.Api;
using SaveVault.Core.Models;

namespace SaveVault.Client.Services;

/// <summary>
/// Meldet im Sync-Intervall den Zustand dieses Geräts an den Server: die
/// <see cref="DeviceInfo"/> (Selbstauskunft) plus je bekanntem Save-Set einen
/// <see cref="DeviceGameState"/> (zuletzt gesehene Basis-Revision + aktueller lokaler
/// <see cref="SyncStatus"/>). Ein nicht erreichbarer Server wird sauber im
/// <see cref="AgentState"/> vermerkt, nicht geworfen.
/// </summary>
public sealed class HeartbeatReporter
{
    private readonly ISaveVaultApi _api;
    private readonly ClientConfigStore _configStore;
    private readonly SaveFolderRegistry _registry;
    private readonly SyncStateStore _stateStore;
    private readonly AgentState _state;
    private readonly Func<DateTime> _nowUtc;

    public HeartbeatReporter(
        ISaveVaultApi api,
        ClientConfigStore configStore,
        SaveFolderRegistry registry,
        SyncStateStore stateStore,
        AgentState state,
        Func<DateTime>? nowUtc = null)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _configStore = configStore ?? throw new ArgumentNullException(nameof(configStore));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _nowUtc = nowUtc ?? (() => DateTime.UtcNow);
    }

    /// <summary>Sendet einen Heartbeat. Nicht eingerichtet → kein Aufruf.</summary>
    public async Task SendOnceAsync(CancellationToken ct = default)
    {
        var config = _configStore.Load();
        if (!config.IsConfigured)
            return;

        var device = DeviceIdentity.FromConfig(config, _nowUtc());

        var gameStates = new List<DeviceGameState>();
        foreach (var entry in _registry.GetAll())
        {
            var baseRevision = _stateStore.Load(entry.Game).BaseRevision;
            var status = _state.GetStatus(entry.Game) ?? SyncStatus.Synced;
            gameStates.Add(new DeviceGameState(entry.Game, baseRevision, status));
        }

        try
        {
            var response = await _api.HeartbeatAsync(new HeartbeatRequest(device, gameStates), ct).ConfigureAwait(false);
            _state.MarkServerReachable(response.ServerTimeUtc);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (SaveVaultApiException ex)
        {
            _state.MarkServerUnreachable(ex.Message);
        }
        catch (HttpRequestException ex)
        {
            _state.MarkServerUnreachable(ex.Message);
        }
    }
}
