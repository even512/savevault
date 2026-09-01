using SaveVault.Core.Models;

namespace SaveVault.Client.Services;

/// <summary>
/// Persistente Menge der vom Sync <b>ausgeschlossenen</b> Spiele (Schlüssel =
/// <see cref="GameKey.Value"/>). Der Nutzer nimmt ein Spiel dauerhaft vom Sync aus
/// („Sync pausieren"); der Ausschluss überlebt einen Neustart. Backing ist ein kleines
/// JSON-File analog zur <see cref="SaveFolderRegistry"/> (atomar geschrieben, tolerant
/// gelesen). Thread-safe; jede Änderung wird sofort persistiert.
///
/// <para>Additiv: ist die Datei nicht vorhanden (Altstand vor diesem Feature), startet die
/// Menge leer – kein Migrationsbruch, bestehende Config/Dateien bleiben unberührt.</para>
/// </summary>
public sealed class GameExclusionStore
{
    private sealed class ExclusionData
    {
        public List<string> ExcludedKeys { get; set; } = new();
    }

    private readonly AppPaths _paths;
    private readonly object _lock = new();
    private readonly HashSet<string> _excluded = new(StringComparer.Ordinal);

    public GameExclusionStore(AppPaths paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        var data = JsonFileStore.Read<ExclusionData>(_paths.ExclusionsFile);
        if (data is not null)
        {
            foreach (var key in data.ExcludedKeys)
                if (!string.IsNullOrWhiteSpace(key))
                    _excluded.Add(key);
        }
    }

    /// <summary>Ob das Spiel aktuell vom Sync ausgeschlossen ist.</summary>
    public bool IsExcluded(GameKey game)
    {
        ArgumentNullException.ThrowIfNull(game);
        lock (_lock)
            return _excluded.Contains(game.Value);
    }

    /// <summary>Alle aktuell ausgeschlossenen Schlüssel (Momentaufnahme).</summary>
    public IReadOnlyCollection<string> GetAll()
    {
        lock (_lock)
            return _excluded.ToList();
    }

    /// <summary>Schließt ein Spiel aus. <c>true</c>, wenn sich dadurch etwas geändert hat.</summary>
    public bool Add(GameKey game)
    {
        ArgumentNullException.ThrowIfNull(game);
        lock (_lock)
        {
            if (_excluded.Add(game.Value))
            {
                Persist();
                return true;
            }
            return false;
        }
    }

    /// <summary>Hebt den Ausschluss eines Spiels auf. <c>true</c>, wenn es ausgeschlossen war.</summary>
    public bool Remove(GameKey game)
    {
        ArgumentNullException.ThrowIfNull(game);
        lock (_lock)
        {
            if (_excluded.Remove(game.Value))
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
        var data = new ExclusionData { ExcludedKeys = _excluded.ToList() };
        JsonFileStore.Write(_paths.ExclusionsFile, data);
    }
}
