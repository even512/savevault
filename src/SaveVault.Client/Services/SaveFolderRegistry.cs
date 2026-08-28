using System.IO;
using SaveVault.Core.Models;
using SaveVault.Core.Storage;

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
        var dropped = false;
        if (data is not null)
        {
            foreach (var e in data.Entries)
            {
                if (e?.Game is null || string.IsNullOrWhiteSpace(e.FolderPath))
                    continue;

                // Selbstheilung: zu breite Altlasten (z. B. persistiertes „C:\") verwerfen,
                // damit ein bestehender Client nicht weiter die ganze Platte durchsucht.
                if (SaveFolderSafety.IsTooBroad(e.FolderPath))
                {
                    dropped = true;
                    continue;
                }

                _byGame[e.Game.Value] = e;
            }
        }

        // Fiel mindestens ein Eintrag weg, die bereinigte Registry einmalig neu schreiben.
        // Im Konstruktor gibt es noch keine Nebenläufigkeit → direkter Schreibaufruf ohne Lock.
        if (dropped)
            WriteData();
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

        if (SaveFolderSafety.IsTooBroad(full))
            throw new ArgumentException(
                "Dieser Ordner ist zu weit gefasst (Laufwerks-/Systemwurzel) und würde die ganze " +
                "Platte durchsuchen. Bitte einen konkreten Save-Ordner wählen.", nameof(folderPath));

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
    /// Gleicht die nicht-manuellen Einträge mit dem Erkennungsergebnis ab (nicht nur
    /// hinzufügen/aktualisieren). Ablauf unter <see cref="_lock"/>:
    /// <list type="bullet">
    /// <item>Für jedes übergebene (nutzbare) Spiel wird der erkannte Ordner gesetzt bzw.
    /// aktualisiert. Zu breite Ordner werden nie gesetzt; manuelle Einträge
    /// (<see cref="SaveFolderEntry.Manual"/> = <c>true</c>) bleiben IMMER unangetastet.</item>
    /// <item>Anschließend werden ALLE nicht-manuellen Einträge entfernt, deren
    /// <c>GameKey.Value</c> NICHT in der übergebenen Menge enthalten ist. Damit fällt ein
    /// Spiel, das die Erkennung nicht mehr liefert (z. B. Project Zomboid, weil sein Save-Set
    /// jetzt als zu groß übersprungen wird), beim nächsten Lauf automatisch aus der Registry –
    /// Selbstheilung, ohne dass die Registry die Größe selbst kennen muss.</item>
    /// </list>
    /// Persistiert wird nur, wenn sich tatsächlich etwas geändert hat (Hinzufügen,
    /// Aktualisieren ODER Entfernen). Der Aufrufer ruft dies nur bei mindestens einem
    /// erkannten Spiel auf – ein leeres/transientes Ergebnis darf die Registry nicht leeren.
    /// </summary>
    public void SetDiscovered(IEnumerable<(GameKey Game, string FolderPath)> discovered)
    {
        ArgumentNullException.ThrowIfNull(discovered);
        lock (_lock)
        {
            var changed = false;
            var discoveredKeys = new HashSet<string>(StringComparer.Ordinal);

            foreach (var (game, folderPath) in discovered)
            {
                if (game is null || string.IsNullOrWhiteSpace(folderPath))
                    continue;

                // Zu breite Ordner (Laufwerks-/Systemwurzel) nie setzen und nicht als
                // „gesehen" merken (sonst würden sie den Abgleich verwässern).
                if (SaveFolderSafety.IsTooBroad(folderPath))
                    continue;

                discoveredKeys.Add(game.Value);

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

            // Abgleich: nicht-manuelle Einträge entfernen, die die Erkennung nicht (mehr)
            // liefert. Manuelle Einträge bleiben unangetastet.
            var toRemove = _byGame.Values
                .Where(e => !e.Manual && !discoveredKeys.Contains(e.Game.Value))
                .Select(e => e.Game.Value)
                .ToList();
            foreach (var key in toRemove)
            {
                _byGame.Remove(key);
                changed = true;
            }

            if (changed)
                Persist();
        }
    }

    private void Persist()
    {
        // Aufruf immer unter _lock.
        WriteData();
    }

    /// <summary>
    /// Serialisiert den aktuellen Stand auf die Platte. Selbst nicht sperrend – der Aufrufer
    /// hält entweder <see cref="_lock"/> (Laufzeit) oder ist der Konstruktor (keine
    /// Nebenläufigkeit). So entsteht kein Deadlock bei der Selbstheilung im Konstruktor.
    /// </summary>
    private void WriteData()
    {
        var data = new RegistryData { Entries = _byGame.Values.ToList() };
        JsonFileStore.Write(_paths.FolderRegistryFile, data);
    }
}
