using System.IO;
using SaveVault.Core.Models;
using SaveVault.Core.Storage;

namespace SaveVault.Client.Services;

/// <summary>Ein einzelner persistierter Save-Ordner eines Spiels. <see cref="Manual"/> markiert
/// einen vom Nutzer hinzugefügten Ordner; <see cref="RootKey"/> ist sein stabiles, geräte-
/// übergreifendes Kennzeichen (bei Altdaten leer → wird beim Laden abgeleitet).</summary>
public sealed record SaveFolderEntry(GameKey Game, string FolderPath, bool Manual, string? RootKey = null);

/// <summary>
/// Die Save-Wurzeln EINES Spiels (Mehr-Ordner-Erkennung): eine oder mehrere. <see cref="Manual"/>
/// ist true, wenn der Nutzer den Ordner selbst zugeordnet hat (Vorrang vor der Erkennung).
/// </summary>
public sealed record GameRoots(GameKey Game, IReadOnlyList<SaveRoot> Roots, bool Manual)
{
    /// <summary>Der primäre (erste) lokale Ordner – für „Ordner öffnen" und Anzeige.</summary>
    public string? PrimaryFolder => Roots.Count > 0 ? Roots[0].Folder : null;

    /// <summary>Anzahl der Save-Wurzeln dieses Spiels.</summary>
    public int Count => Roots.Count;
}

/// <summary>
/// Hält die Zuordnung <c>GameKey → eine oder mehrere lokale Save-Wurzeln</c> – gespeist aus der
/// ludusavi-Erkennung (<see cref="SetDiscovered"/>, Mehr-Ordner-Gruppierung) und aus manuell
/// hinzugefügten Ordnern (<see cref="AddManual"/>). Manuell gesetzte Ordner haben Vorrang; die
/// Erkennung ersetzt eine manuelle Zuordnung nur dann, wenn sie den manuellen Ordner sicher mit
/// abdeckt. Thread-safe; jede Änderung wird atomar persistiert.
///
/// <para><b>Persistenz + Migration:</b> gespeichert wird eine flache Liste von
/// <see cref="SaveFolderEntry"/> (je Wurzel eine); intern nach Spiel gruppiert. Altdaten (ein
/// Ordner je Spiel, ohne <see cref="SaveFolderEntry.RootKey"/>) bleiben lesbar – der fehlende Key
/// wird beim Laden über <see cref="SaveRootKey"/> abgeleitet (idempotent, kein Datenverlust).</para>
/// </summary>
public sealed class SaveFolderRegistry
{
    private sealed class RegistryData
    {
        public List<SaveFolderEntry> Entries { get; set; } = new();
    }

    private readonly AppPaths _paths;
    private readonly object _lock = new();
    // Je Spiel eine Liste von Wurzeln (in Einfüge-/Ladereihenfolge; der erste gilt als primär).
    private readonly Dictionary<string, List<SaveFolderEntry>> _byGame = new(StringComparer.Ordinal);

    public SaveFolderRegistry(AppPaths paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        var data = JsonFileStore.Read<RegistryData>(_paths.FolderRegistryFile);
        var changed = false;
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
                    changed = true;
                    continue;
                }

                // Migration: fehlenden Root-Key ableiten (Altformat: ein Ordner je Spiel ohne Key).
                var normalized = e;
                if (string.IsNullOrEmpty(e.RootKey))
                {
                    normalized = e with { RootKey = SaveRootKey.Derive(e.FolderPath) };
                    changed = true;
                }

                Add(normalized);
            }
        }

        // Fiel etwas weg oder wurde migriert, die bereinigte Registry einmalig neu schreiben.
        // Im Konstruktor gibt es noch keine Nebenläufigkeit → direkter Schreibaufruf ohne Lock.
        if (changed)
            WriteData();
    }

    /// <summary>Alle Spiele mit ihren Save-Wurzeln (Momentaufnahme, pro Spiel gruppiert).</summary>
    public IReadOnlyList<GameRoots> GetGames()
    {
        lock (_lock)
            return _byGame.Values.Where(l => l.Count > 0).Select(ToGameRoots).ToList();
    }

    /// <summary>Die Wurzeln eines Spiels oder <c>null</c>.</summary>
    public GameRoots? TryGet(GameKey game)
    {
        ArgumentNullException.ThrowIfNull(game);
        lock (_lock)
            return _byGame.TryGetValue(game.Value, out var list) && list.Count > 0 ? ToGameRoots(list) : null;
    }

    /// <summary>
    /// Jede einzelne Save-Wurzel über alle Spiele (für die Watcher: <b>ein Watcher je Ordner</b>).
    /// </summary>
    public IReadOnlyList<(GameKey Game, SaveRoot Root)> EnumerateRoots()
    {
        lock (_lock)
            return _byGame.Values
                .SelectMany(list => list.Select(e => (e.Game, new SaveRoot(e.RootKey ?? SaveRootKey.Derive(e.FolderPath), e.FolderPath))))
                .ToList();
    }

    private static GameRoots ToGameRoots(List<SaveFolderEntry> list)
    {
        var game = list[0].Game;
        var manual = list.Any(e => e.Manual);
        var roots = list.Select(e => new SaveRoot(e.RootKey ?? SaveRootKey.Derive(e.FolderPath), e.FolderPath)).ToList();
        return new GameRoots(game, roots, manual);
    }

    /// <summary>Fügt einen Eintrag der In-Memory-Gruppe seines Spiels hinzu (ohne Persistenz).</summary>
    private void Add(SaveFolderEntry entry)
    {
        if (!_byGame.TryGetValue(entry.Game.Value, out var list))
            _byGame[entry.Game.Value] = list = new List<SaveFolderEntry>();
        list.Add(entry);
    }

    /// <summary>
    /// Fügt einen manuell gewählten Ordner für ein Spiel hinzu und macht ihn zur <b>alleinigen</b>,
    /// manuellen Wurzel dieses Spiels (Vorrang vor der Erkennung). Validiert, dass der Pfad existiert,
    /// ein Verzeichnis und nicht zu breit ist; sonst <see cref="ArgumentException"/> (die GUI meldet das).
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
            _byGame[game.Value] = new List<SaveFolderEntry>
            {
                new(game, full, Manual: true, RootKey: SaveRootKey.Derive(full)),
            };
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
    /// Gleicht die nicht-manuellen Einträge mit dem Erkennungsergebnis ab. Je Spiel liefert die
    /// Erkennung jetzt eine <b>Liste</b> von Wurzeln (Mehr-Ordner-Gruppierung). Ablauf unter
    /// <see cref="_lock"/>:
    /// <list type="bullet">
    /// <item>Zu breite Wurzeln werden nie gesetzt. Bleibt für ein Spiel keine gültige Wurzel übrig,
    /// gilt es als nicht geliefert.</item>
    /// <item><b>Manuelle Zuordnung ersetzen, wo sicher:</b> ist ein Spiel bisher manuell und deckt
    /// das Erkennungsergebnis den manuellen Ordner ab (er gleicht einer erkannten Wurzel oder liegt
    /// darin), übernimmt die Erkennung (Manual=false). Ein manueller Ordner, der zu <b>nichts</b>
    /// aus der Erkennung passt (echter Nischen-Override), bleibt unangetastet.</item>
    /// <item>Anschließend werden alle nicht-manuellen Spiele entfernt, die die Erkennung nicht mehr
    /// liefert (Selbstheilung, z. B. jetzt zu große Save-Sets).</item>
    /// </list>
    /// Persistiert wird nur bei echter Änderung. Der Aufrufer ruft dies nur bei mindestens einem
    /// erkannten Spiel auf – ein leeres/transientes Ergebnis darf die Registry nicht leeren.
    /// </summary>
    public void SetDiscovered(IEnumerable<(GameKey Game, IReadOnlyList<SaveRoot> Roots)> discovered)
    {
        ArgumentNullException.ThrowIfNull(discovered);
        lock (_lock)
        {
            var changed = false;
            var discoveredKeys = new HashSet<string>(StringComparer.Ordinal);

            foreach (var (game, roots) in discovered)
            {
                if (game is null || roots is null)
                    continue;

                // Zu breite Wurzeln aussortieren; ohne gültige Wurzel gilt das Spiel als nicht geliefert.
                var usable = roots.Where(r => r is not null && !string.IsNullOrWhiteSpace(r.Folder)
                                              && !SaveFolderSafety.IsTooBroad(r.Folder)).ToList();
                if (usable.Count == 0)
                    continue;

                discoveredKeys.Add(game.Value);

                _byGame.TryGetValue(game.Value, out var existing);
                var existingIsManual = existing is not null && existing.Any(e => e.Manual);

                // Manueller Eintrag: nur ersetzen, wenn die Erkennung den manuellen Ordner abdeckt.
                if (existingIsManual && !DiscoveryCoversManual(existing!, usable))
                    continue;

                var newEntries = usable
                    .Select(r => new SaveFolderEntry(game, Path.GetFullPath(r.Folder), Manual: false,
                        RootKey: string.IsNullOrEmpty(r.Key) ? SaveRootKey.Derive(r.Folder) : r.Key))
                    .ToList();

                if (existing is null || !SameRoots(existing, newEntries))
                {
                    _byGame[game.Value] = newEntries;
                    changed = true;
                }
            }

            // Abgleich: nicht-manuelle Spiele entfernen, die die Erkennung nicht (mehr) liefert.
            var toRemove = _byGame
                .Where(kv => !kv.Value.Any(e => e.Manual) && !discoveredKeys.Contains(kv.Key))
                .Select(kv => kv.Key)
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

    /// <summary>
    /// <c>true</c>, wenn JEDER manuelle Ordner des Spiels von mindestens einer erkannten Wurzel
    /// abgedeckt wird (gleicht ihr oder liegt darin) – dann darf die Erkennung übernehmen.
    /// </summary>
    private static bool DiscoveryCoversManual(List<SaveFolderEntry> existing, List<SaveRoot> discovered)
    {
        foreach (var m in existing.Where(e => e.Manual))
        {
            var covered = discovered.Any(d => IsWithinOrEqual(m.FolderPath, d.Folder));
            if (!covered)
                return false;
        }
        return true;
    }

    /// <summary><c>true</c>, wenn <paramref name="child"/> gleich <paramref name="parent"/> ist
    /// oder darin liegt (normalisierter, case-insensitiver Pfadvergleich).</summary>
    private static bool IsWithinOrEqual(string child, string parent)
    {
        string c, p;
        try
        {
            c = Path.GetFullPath(child).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            p = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
        if (string.Equals(c, p, StringComparison.OrdinalIgnoreCase))
            return true;
        return c.StartsWith(p + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Ob zwei Wurzel-Listen (Ordner-Mengen) übereinstimmen – Reihenfolge-unabhängig.</summary>
    private static bool SameRoots(List<SaveFolderEntry> a, List<SaveFolderEntry> b)
    {
        if (a.Count != b.Count)
            return false;
        var setA = a.Select(e => e.FolderPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return b.All(e => setA.Contains(e.FolderPath));
    }

    private void Persist() => WriteData(); // Aufruf immer unter _lock (Laufzeit) oder im Ctor.

    /// <summary>
    /// Serialisiert den aktuellen Stand (flache Liste über alle Wurzeln) auf die Platte. Selbst
    /// nicht sperrend – der Aufrufer hält <see cref="_lock"/> (Laufzeit) oder ist der Konstruktor.
    /// </summary>
    private void WriteData()
    {
        var data = new RegistryData { Entries = _byGame.Values.SelectMany(l => l).ToList() };
        JsonFileStore.Write(_paths.FolderRegistryFile, data);
    }
}
