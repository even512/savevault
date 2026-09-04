using System.Net.Http;
using SaveVault.Core.Api;
using SaveVault.Core.Models;

namespace SaveVault.Client.Services;

/// <summary>
/// Fragt im Sync-Intervall die Server→Client-Befehls-Warteschlange ab und führt jeden
/// Befehl aus:
///   * <see cref="CommandType.Restore"/>: die Ziel-Revision holen und in den Save-Ordner
///     schreiben (über <see cref="SyncEngine.ApplyRevisionAsync"/> – gleiche Pfad-Validierung),
///     Sync-State nachziehen.
///   * <see cref="CommandType.ApplyResolution"/>: die aktuelle (Gewinner-)Revision holen und
///     schreiben, Sync-State nachziehen, lokalen Konflikt-Status löschen.
/// Erst nach erfolgreicher Ausführung wird der Befehl bestätigt (<c>AckCommandAsync</c>);
/// ein fehlgeschlagener/nicht anwendbarer Befehl bleibt offen (Retry im nächsten Zyklus).
/// Einzelfehler beenden die Schleife nicht.
/// </summary>
public sealed class CommandPoller
{
    private readonly ISaveVaultApi _api;
    private readonly ClientConfigStore _configStore;
    private readonly SaveFolderRegistry _registry;
    private readonly SyncEngine _engine;
    private readonly AgentState _state;
    private readonly GameSerializer _serializer;

    public CommandPoller(
        ISaveVaultApi api,
        ClientConfigStore configStore,
        SaveFolderRegistry registry,
        SyncEngine engine,
        AgentState state,
        GameSerializer serializer)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _configStore = configStore ?? throw new ArgumentNullException(nameof(configStore));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
    }

    /// <summary>Holt die offenen Befehle und arbeitet sie ab. Nicht eingerichtet → kein Aufruf.</summary>
    public async Task PollOnceAsync(CancellationToken ct = default)
    {
        var config = _configStore.Load();
        if (!config.IsConfigured)
            return;

        CommandListResponse commands;
        try
        {
            commands = await _api.GetCommandsAsync(config.DeviceId!, ct).ConfigureAwait(false);
            _state.MarkServerReachable(DateTime.UtcNow);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (SaveVaultApiException ex)
        {
            _state.MarkServerUnreachable(ex.Message);
            return;
        }
        catch (HttpRequestException ex)
        {
            _state.MarkServerUnreachable(ex.Message);
            return;
        }

        foreach (var command in commands.Commands)
        {
            ct.ThrowIfCancellationRequested();
            bool handled;
            try
            {
                handled = await HandleAsync(command, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Ein defekter Befehl darf die Warteschlange nicht blockieren – Fehler vermerken,
                // Befehl offen lassen (kein Ack), weiter mit dem nächsten. Der Status muss unter dem
                // KANONISCHEN Spiel-Schlüssel landen (wie in HandleAsync), nicht unter dem effektiven
                // Bucket-Schlüssel des Befehls – sonst sähe die Oberfläche den Fehler nie.
                _state.SetStatus(BucketKey.Original(command.Game), SyncStatus.Error, action: "Befehl fehlgeschlagen: " + ex.Message);
                continue;
            }

            if (handled)
            {
                try
                {
                    await _api.AckCommandAsync(command.Id, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception)
                {
                    // Ack fehlgeschlagen: der Befehl wird beim nächsten Poll erneut geliefert;
                    // ApplyRevision ist idempotent (schreibt denselben Inhalt), also unkritisch.
                }
            }
        }
    }

    private async Task<bool> HandleAsync(Command command, CancellationToken ct)
    {
        // Server-Befehle tragen den EFFEKTIVEN Bucket-Schlüssel (privat: dev|owner|…). Lokal wird
        // aber nach dem Originalschlüssel gebucht (Registry/Sync-State), und der Client synct sein
        // eigenes Spiel per Default-Scope „privat" gegen genau diesen Bucket – deshalb hier auf den
        // Originalschlüssel zurückführen und mit ihm arbeiten.
        var game = BucketKey.Original(command.Game);
        // Der Scope muss der des Befehls-Buckets sein (privat/geteilt), nicht der Default – sonst
        // würde ein Befehl zu einem geteilten Bucket gegen den privaten Bucket angewandt (falscher
        // Stand). Er steckt im effektiven Bucket-Schlüssel, den der Befehl trägt.
        var scope = BucketKey.ScopeOf(command.Game.Value);

        var entry = _registry.TryGet(game);
        if (entry is null)
        {
            // Kein lokaler Ordner → nicht anwendbar, Befehl offen lassen (kein Ack).
            _state.SetStatus(game, SyncStatus.Error, action: "Befehl ignoriert: kein lokaler Ordner zugeordnet.");
            return false;
        }

        switch (command.Type)
        {
            case CommandType.Restore:
            {
                if (command.TargetRevision is not long target)
                    return false;
                var revision = await _api.GetRevisionAsync(game, target, scope, ct).ConfigureAwait(false);
                // Exklusiv pro Spiel (B1): kein gleichzeitiger Sync-Zyklus, der einen halb
                // geschriebenen Restore-Ordner als „Änderung" hochladen könnte.
                await _serializer.RunExclusiveAsync(game,
                    c => _engine.ApplyRevisionAsync(game, entry.Roots, revision.Manifest, revision.Number, scope, c), ct)
                    .ConfigureAwait(false);
                _state.SetStatus(game, SyncStatus.Synced,
                    action: $"Wiederhergestellt ← Revision {revision.Number}", folder: entry.PrimaryFolder, baseRevision: revision.Number);
                return true;
            }

            case CommandType.ApplyResolution:
            {
                // Gewinner = aktueller Head des Spiels nach der serverseitigen Lösung.
                var head = await _api.GetHeadAsync(game, scope, ct).ConfigureAwait(false);
                if (head.CurrentRevision <= 0)
                    return false;
                var revision = await _api.GetRevisionAsync(game, head.CurrentRevision, scope, ct).ConfigureAwait(false);
                // Exklusiv pro Spiel (B1), gleiches Gate wie der Sync-Zyklus.
                await _serializer.RunExclusiveAsync(game,
                    c => _engine.ApplyRevisionAsync(game, entry.Roots, revision.Manifest, revision.Number, scope, c), ct)
                    .ConfigureAwait(false);
                _state.SetStatus(game, SyncStatus.Synced,
                    action: $"Konflikt gelöst ← Revision {revision.Number}", folder: entry.PrimaryFolder, baseRevision: revision.Number);
                return true;
            }

            default:
                return false;
        }
    }
}
