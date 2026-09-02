using SaveVault.Core.Models;

namespace SaveVault.Client.Services;

/// <summary>
/// Persistente Menge der <b>geteilten</b> Spiele (Schlüssel = <see cref="GameKey.Value"/>) –
/// die opt-in-Gegenmenge zur Ausschluss-Logik: ein Spiel gilt standardmäßig als <b>Lokal</b>
/// (privater Bucket) und wird erst nach ausdrücklichem Umschalten auf <b>Synchron</b> in dieser
/// Menge geführt und dann gegen den geteilten Bucket synchronisiert (siehe
/// <c>specs/savevault-change-per-device-sync.md</c>, Phase 2).
///
/// <para>Backing ist ein kleines JSON-File analog zur <see cref="GameExclusionStore"/> (atomar
/// geschrieben, tolerant gelesen). Thread-safe; jede Änderung wird sofort persistiert. Fehlt die
/// Datei (Altstand vor diesem Feature), startet die Menge leer – jedes Spiel ist dann „Lokal",
/// kein Migrationsbruch.</para>
/// </summary>
public sealed class GameShareStore
{
    private sealed class ShareData
    {
        public List<string> SharedKeys { get; set; } = new();
    }

    private readonly AppPaths _paths;
    private readonly object _lock = new();
    private readonly HashSet<string> _shared = new(StringComparer.Ordinal);

    public GameShareStore(AppPaths paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        var data = JsonFileStore.Read<ShareData>(_paths.SharedGamesFile);
        if (data is not null)
        {
            foreach (var key in data.SharedKeys)
                if (!string.IsNullOrWhiteSpace(key))
                    _shared.Add(key);
        }
    }

    /// <summary>Ob das Spiel „Synchron" (geteilt) ist. Default (nicht enthalten) = „Lokal".</summary>
    public bool IsShared(GameKey game)
    {
        ArgumentNullException.ThrowIfNull(game);
        lock (_lock)
            return _shared.Contains(game.Value);
    }

    /// <summary>Schaltet ein Spiel auf „Synchron". <c>true</c>, wenn sich dadurch etwas geändert hat.</summary>
    public bool Add(GameKey game)
    {
        ArgumentNullException.ThrowIfNull(game);
        lock (_lock)
        {
            if (_shared.Add(game.Value))
            {
                Persist();
                return true;
            }
            return false;
        }
    }

    /// <summary>Schaltet ein Spiel zurück auf „Lokal". <c>true</c>, wenn es geteilt war.</summary>
    public bool Remove(GameKey game)
    {
        ArgumentNullException.ThrowIfNull(game);
        lock (_lock)
        {
            if (_shared.Remove(game.Value))
            {
                Persist();
                return true;
            }
            return false;
        }
    }

    private void Persist()
    {
        // Aufruf immer unter _lock.
        var data = new ShareData { SharedKeys = _shared.ToList() };
        JsonFileStore.Write(_paths.SharedGamesFile, data);
    }
}
