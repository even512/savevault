using System.IO;
using SaveVault.Core.Models;

namespace SaveVault.Client.Services;

/// <summary>Eine Zuordnung Spiel → lokaler Save-Ordner. <see cref="Manual"/> markiert einen
/// vom Nutzer hinzugefügten Ordner (überschreibt eine spätere ludusavi-Erkennung nicht).</summary>
public sealed record SaveFolderEntry(GameKey Game, string FolderPath, bool Manual);

/// <summary>
/// Hält die Zuordnung <c>GameKey → lokaler Ordnerpfad</c> – gespeist aus der
/// ludusavi-Erkennung (<see cref="SetDiscovered"/>) und aus manuell hinzugefügten
/// Ordnern (<see cref="AddManual"/>, persistiert). Manuell gesetzte Ordner haben Vorrang
/// und werden von der Erkennung nicht überschrieben. Thread-safe; jede Änderung wird
/// atomar persistiert.
/// </summary>
public sealed class SaveFolderRegistry
{
    private sealed class RegistryData
    {
        public List<SaveFolderEntry> Entries { get; set; } = new();
    }

    private readonly AppPaths _paths;
    private readonly object _lock = new();
    private readonly Dictionary<string, SaveFolderEntry> _byGame = new(StringComparer.Ordinal);

    public SaveFolderRegistry(AppPaths paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        var data = JsonFileStore.Read<RegistryData>(_paths.FolderRegistryFile);
        if (data is not null)
        {
            foreach (var e in data.Entries)
            {
                if (e?.Game is not null && !string.IsNullOrWhiteSpace(e.FolderPath))
                    _byGame[e.Game.Value] = e;
            }
        }
    }

    /// <summary>Alle aktuell bekannten Zuordnungen (Momentaufnahme).</summary>
    public IReadOnlyList<SaveFolderEntry> GetAll()
    {
        lock (_lock)
            return _byGame.Values.ToList();
    }

    /// <summary>Die Zuordnung eines Spiels oder <c>null</c>.</summary>
    public SaveFolderEntry? TryGet(GameKey game)
    {
        ArgumentNullException.ThrowIfNull(game);
        lock (_lock)
            return _byGame.TryGetValue(game.Value, out var e) ? e : null;
    }

    /// <summary>
    /// Fügt einen manuell gewählten Ordner hinzu. Validiert, dass der Pfad existiert und
    /// ein Verzeichnis ist; sonst <see cref="ArgumentException"/> (die GUI meldet das dem
    /// Nutzer). Ein manueller Ordner hat Vorrang vor der Erkennung.
    /// </summary>
    public void AddManual(GameKey game, string folderPath)
    {
        ArgumentNullException.ThrowIfNull(game);
        if (string.IsNullOrWhiteSpace(folderPath))
            throw new ArgumentException("Es wurde kein Ordner angegeben.", nameof(folderPath));

        string full;
        try
        {
            full = Path.GetFullPath(folderPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ArgumentException("Der Ordnerpfad ist ungültig.", nameof(folderPath), ex);
        }

        if (!Directory.Exists(full))
            throw new ArgumentException($"Der Ordner existiert nicht: {full}", nameof(folderPath));

        lock (_lock)
        {
            _byGame[game.Value] = new SaveFolderEntry(game, full, Manual: true);
            Persist();
        }
    }

    /// <summary>Entfernt die Zuordnung eines Spiels (falls vorhanden).</summary>
    public bool Remove(GameKey game)
    {
        ArgumentNullException.ThrowIfNull(game);
        lock (_lock)
        {
            if (_byGame.Remove(game.Value))
            {
                Persist();
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Übernimmt die Ergebnisse der ludusavi-Erkennung. Manuell gesetzte Ordner bleiben
    /// unangetastet; für alle anderen Spiele wird der erkannte Ordner gesetzt/aktualisiert.
    /// </summary>
    public void SetDiscovered(IEnumerable<(GameKey Game, string FolderPath)> discovered)
    {
        ArgumentNullException.ThrowIfNull(discovered);
        lock (_lock)
        {
            var changed = false;
            foreach (var (game, folderPath) in discovered)
            {
                if (game is null || string.IsNullOrWhiteSpace(folderPath))
                    continue;

                // Manuelle Ordner haben Vorrang und werden nicht überschrieben.
                if (_byGame.TryGetValue(game.Value, out var existing) && existing.Manual)
                    continue;

                var entry = new SaveFolderEntry(game, folderPath, Manual: false);
                if (existing is null || !string.Equals(existing.FolderPath, folderPath, StringComparison.Ordinal))
                {
                    _byGame[game.Value] = entry;
                    changed = true;
                }
            }

            if (changed)
                Persist();
        }
    }

    private void Persist()
    {
        // Aufruf immer unter _lock.
        var data = new RegistryData { Entries = _byGame.Values.ToList() };
        JsonFileStore.Write(_paths.FolderRegistryFile, data);
    }
}
