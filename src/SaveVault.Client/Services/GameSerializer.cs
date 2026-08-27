using System.Collections.Concurrent;
using System.Threading;
using SaveVault.Core.Models;

namespace SaveVault.Client.Services;

/// <summary>
/// Serialisiert alle Operationen, die denselben Save-Ordner anfassen, <b>pro Spiel</b>:
/// ein Sync-Zyklus (Watcher-Ereignis oder Rescan) und die Anwendung eines Server-Befehls
/// (Restore / Konfliktlösung) desselben Spiels laufen nie gleichzeitig. So kann ein
/// Rescan nie einen halb geschriebenen Restore-Ordner als „lokale Änderung" hochladen.
///
/// Wichtig gegen Deadlocks: Das Gate wird ausschließlich an der <b>äußersten</b> Ebene
/// genommen (Sync-Zyklus bzw. Befehls-Anwendung). Innere Schreibhelfer wie
/// <see cref="SyncEngine.ApplyRevisionAsync"/> nehmen es NICHT selbst – sonst würde der
/// Download-Fall (der intern denselben Schreibpfad nutzt) das Gate rekursiv anfordern.
/// </summary>
public sealed class GameSerializer : IDisposable
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);
    private bool _disposed;

    /// <summary>Führt <paramref name="action"/> exklusiv für <paramref name="game"/> aus.</summary>
    public async Task RunExclusiveAsync(GameKey game, Func<CancellationToken, Task> action, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(action);

        var gate = _locks.GetOrAdd(game.Value, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await action(ct).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        foreach (var gate in _locks.Values)
            gate.Dispose();
        _locks.Clear();
    }
}
