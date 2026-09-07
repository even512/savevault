namespace SaveVault.Core.Storage;

/// <summary>
/// Reine, IO-arme Bausteine für den <b>exakten</b> Austausch eines Save-Ordners gegen ein
/// Ziel-Manifest (Umschalten Lokal ↔ Synchron, siehe
/// <c>specs/savevault-change-shared-save-sichtbarkeit.md</c>, Abschnitt „Plan-Korrektur"):
/// anders als ein additives Schreiben (nur schreiben, nie löschen) muss der Ordner nach einem
/// bewussten Nutzer-Wechsel zwischen zwei vollständigen Ständen exakt dem Ziel-Manifest
/// entsprechen – sonst interpretiert der reguläre <c>SyncDecider</c> eine stehen gebliebene
/// Restdatei später fälschlich als echte lokale Änderung (und im schlimmsten Fall als Konflikt).
///
/// <para><b>Sichere Reihenfolge (verbindlich, kein Ermessen des Aufrufers):</b> Der Aufrufer lädt
/// zuerst <b>alle</b> Ziel-Dateien vollständig herunter und schreibt sie als Temp-Dateien neben
/// ihrem Zielpfad. Erst wenn das für <b>jede</b> Ziel-Datei geglückt ist, ruft er
/// <see cref="Commit"/> auf – den „point of no return", der überzählige Alt-Dateien löscht und
/// danach die Temp-Dateien an ihren Platz verschiebt. Wird <see cref="Commit"/> nicht erreicht
/// (Download bricht vorher ab), bleibt der Ordner garantiert im alten Zustand: diese Klasse fasst
/// nie einen bestehenden Zielpfad an, bevor der Aufrufer <see cref="Commit"/> aufruft.</para>
/// </summary>
public static class LocalContentReplacer
{
    /// <summary>
    /// Alle Dateien, die aktuell physisch in den bekannten (bereits aufgelösten) Save-Wurzeln
    /// liegen, deren voller Pfad aber NICHT in <paramref name="keepFullPaths"/> (den Ziel-Pfaden
    /// des validierten Austausch-Plans) enthalten ist – die Menge, die beim exakten Austausch
    /// gelöscht werden muss, damit der Ordner hinterher bit-genau dem Ziel-Manifest entspricht.
    ///
    /// <para>Reine Dateisystem-Enumeration innerhalb bereits sicher aufgelöster Wurzel-Ordner –
    /// kein Pfad-Vertrauen auf Fremd-Eingabe nötig, die Wurzeln kommen aus der lokalen
    /// Geräte-Registry, nie vom Server. Eigene, noch nicht verschobene Temp-Marker
    /// (<c>*.svtmp-…</c>) eines laufenden Austauschs werden nie als „überzählig" gemeldet.</para>
    /// </summary>
    public static IReadOnlyList<string> FindExtraFiles(
        IReadOnlyList<SaveRoot> resolvedRoots,
        IReadOnlyCollection<string> keepFullPaths)
    {
        ArgumentNullException.ThrowIfNull(resolvedRoots);
        ArgumentNullException.ThrowIfNull(keepFullPaths);

        var keep = new HashSet<string>(keepFullPaths, StringComparer.OrdinalIgnoreCase);
        var extras = new List<string>();
        foreach (var root in resolvedRoots)
        {
            if (string.IsNullOrWhiteSpace(root.Folder) || !Directory.Exists(root.Folder))
                continue;

            foreach (var file in Directory.EnumerateFiles(root.Folder, "*", SearchOption.AllDirectories))
            {
                if (file.Contains(".svtmp-", StringComparison.Ordinal))
                    continue; // eigener, noch nicht verschobener Temp-Marker – nie „überzählig".

                var full = Path.GetFullPath(file);
                if (!keep.Contains(full))
                    extras.Add(full);
            }
        }
        return extras;
    }

    /// <summary>
    /// Der „point of no return" des exakten Austauschs: löscht zuerst die überzähligen
    /// Alt-Dateien, verschiebt DANACH alle Temp-Dateien an ihren Zielplatz
    /// (<see cref="File.Move(string, string, bool)"/> mit <c>overwrite: true</c>). Vor diesem
    /// Aufruf darf der Ordner nicht angefasst worden sein (siehe Klassendoku). Ein einzelner
    /// Lösch-Fehler (Datei gerade gesperrt o. ä.) wird best-effort geschluckt, damit ein
    /// Ausreißer den bereits vollständig validierten und heruntergeladenen Austausch nicht
    /// insgesamt verhindert; ein Fehler beim finalen Verschieben wird weitergereicht (ein echter
    /// IO-Fehler an dieser letzten, kurzen Stelle ist ein reales Problem, kein normaler Fall).
    /// </summary>
    public static void Commit(
        IReadOnlyList<string> extraFilesToDelete,
        IReadOnlyDictionary<string, string> tempByTarget)
    {
        ArgumentNullException.ThrowIfNull(extraFilesToDelete);
        ArgumentNullException.ThrowIfNull(tempByTarget);

        foreach (var extra in extraFilesToDelete)
        {
            try { File.Delete(extra); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* best effort */ }
        }

        foreach (var (target, tmp) in tempByTarget)
        {
            File.Move(tmp, target, overwrite: true);
        }
    }
}
