using System.IO;

namespace SaveVault.Core.Storage;

/// <summary>
/// Ergebnis der Mehr-Ordner-Gruppierung: die abgeleiteten <b>engen, spielspezifischen</b>
/// Save-Wurzeln eines Spiels und – falls vorhanden – die Reste, die sich NICHT auf einen
/// akzeptablen Ordner auflösen ließen.
/// </summary>
/// <param name="Roots">
/// Die akzeptablen Save-Wurzeln (jede weder „zu breit" noch eine Container-Wurzel), in
/// stabiler, aufsteigend sortierter Reihenfolge. Kann leer sein, wenn gar nichts auflösbar war.
/// </param>
/// <param name="Unresolved">
/// Gruppen, deren gemeinsamer Ordner zu breit/ein Container blieb und die sich nicht weiter
/// aufsplitten ließen (in der Praxis nur, wenn eine Save-Datei direkt in einer System-/
/// Sammelwurzel liegt). Ist diese Liste nicht leer, gilt das Spiel als <b>mehrdeutig</b> und
/// wird – wie heute – für die manuelle Zuordnung zurückgestellt.
/// </param>
public sealed record SaveRootGroupingResult(
    IReadOnlyList<string> Roots,
    IReadOnlyList<string> Unresolved)
{
    /// <summary><c>true</c>, wenn jede Save-Datei einer engen, akzeptablen Wurzel zugeordnet
    /// wurde (kein zu breiter/Container-Rest übrig).</summary>
    public bool FullyResolved => Unresolved.Count == 0;
}

/// <summary>
/// Zerlegt die von ludusavi gemeldeten Save-Dateipfade eines Spiels in ihre natürlichen
/// Save-Wurzeln. Kern der „Mehr-Ordner-Erkennung": Spiele, deren Saves über mehrere getrennte
/// Orte streuen (z. B. Steam <c>userdata</c> + <c>steamapps\common</c>, oder zwei getrennte
/// <c>AppData\Local</c>-Ordner), fallen nicht mehr auf einen zu breiten gemeinsamen Nenner
/// (etwa die Steam-Installations- oder Profilwurzel) zurück, sondern werden in <b>mehrere</b>
/// enge, spielspezifische Ordner aufgelöst.
///
/// <para><b>Verfahren (rein rechnend, kein IO, kein Netz):</b> Bilde den gemeinsamen Ordner der
/// aktuellen Pfadgruppe. Ist er <i>akzeptabel</i> – weder <see cref="SaveFolderSafety.IsTooBroad(string?)"/>
/// noch <see cref="SaveFolderSafety.IsContainerRoot(string?)"/> – ist er eine fertige Wurzel.
/// Sonst wird die Gruppe an der nächsten Pfad-Verzweigung (dem Segment direkt unter dem
/// gemeinsamen Ordner; bei verschiedenen Laufwerken am Laufwerk) aufgeteilt und jede Teilgruppe
/// <b>rekursiv</b> weiterbehandelt. Weil jede Teilgruppe einen echt tieferen gemeinsamen Ordner
/// hat, terminiert die Rekursion. So wird auch <b>durch</b> Container-Wurzeln hindurch bis zum
/// spielspezifischen Ordner abgestiegen.</para>
///
/// <para>Der Größen-Schutz (>5000 Dateien / >2&#160;GB) bleibt Sache des Aufrufers und wirkt
/// weiterhin <b>pro Spiel</b> (Summe über alle Wurzeln) – die Gruppierung hier trifft dazu keine
/// Entscheidung.</para>
/// </summary>
public static class SaveRootGrouping
{
    /// <summary>
    /// Gruppiert die Dateipfade eines Spiels in seine Save-Wurzeln. Leere/ungültige Pfade werden
    /// übersprungen; sind gar keine gültigen Pfade übrig, ist das Ergebnis leer.
    /// </summary>
    public static SaveRootGroupingResult Group(IEnumerable<string> filePaths)
    {
        ArgumentNullException.ThrowIfNull(filePaths);

        // Je Datei den enthaltenden Ordner ermitteln und in Segmente zerlegen – konsistent mit
        // GameDiscovery.CommonDirectory: '\' und '/' vereinheitlichen, dann auf Vollpfad
        // normalisieren und am Plattform-Trenner splitten.
        var dirSegments = new List<string[]>();
        foreach (var raw in filePaths)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;
            string? dir;
            try
            {
                var full = Path.GetFullPath(raw.Replace('\\', '/'));
                dir = Path.GetDirectoryName(full);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                continue;
            }
            if (string.IsNullOrEmpty(dir))
                continue;
            dirSegments.Add(dir.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries));
        }

        var roots = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var unresolved = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        if (dirSegments.Count > 0)
            Resolve(dirSegments, roots, unresolved);

        return new SaveRootGroupingResult(roots.ToArray(), unresolved.ToArray());
    }

    /// <summary>
    /// Rekursiver Kern: nimmt eine nicht-leere Gruppe von Ordner-Segmentlisten, bestimmt den
    /// gemeinsamen Präfix und sammelt entweder eine akzeptable Wurzel ein oder splittet an der
    /// nächsten Verzweigung und steigt weiter ab.
    /// </summary>
    private static void Resolve(List<string[]> group, SortedSet<string> roots, SortedSet<string> unresolved)
    {
        var commonLen = CommonPrefixLength(group);
        var candidate = JoinSegments(group[0], commonLen);

        // Akzeptabel = weder zu breit (laufzeit- ODER strukturell) noch eine Container-Wurzel
        // → fertige, enge Save-Wurzel.
        if (commonLen >= 1
            && !SaveFolderSafety.IsTooBroad(candidate)
            && !SaveFolderSafety.IsBroadUserStructure(candidate)
            && !SaveFolderSafety.IsContainerRoot(candidate))
        {
            roots.Add(candidate);
            return;
        }

        // Nicht akzeptabel → an der nächsten Verzweigung (Segment-Index == commonLen) aufteilen
        // und je Teilgruppe tiefer. Pfade, deren Ordner genau der (zu breite) gemeinsame Ordner
        // IST – die also nicht tiefer gehen –, können nicht weiter aufgelöst werden.
        var buckets = new Dictionary<string, List<string[]>>(StringComparer.OrdinalIgnoreCase);
        var stuck = false;
        foreach (var segs in group)
        {
            if (segs.Length <= commonLen)
            {
                // Ordner liegt direkt auf dem zu breiten gemeinsamen Nenner (z. B. eine Save-Datei
                // unmittelbar im Benutzerprofil) → mehrdeutig, kann nicht enger gefasst werden.
                stuck = true;
                continue;
            }
            var key = segs[commonLen];
            if (!buckets.TryGetValue(key, out var list))
                buckets[key] = list = new List<string[]>();
            list.Add(segs);
        }

        foreach (var bucket in buckets.Values)
            Resolve(bucket, roots, unresolved);

        if (stuck && candidate.Length > 0)
            unresolved.Add(candidate);
    }

    /// <summary>
    /// Länge (in Segmenten) des gemeinsamen Präfixes aller Segmentlisten der Gruppe,
    /// case-insensitiv verglichen. 0 ⇒ kein gemeinsames Segment (z. B. verschiedene Laufwerke).
    /// </summary>
    private static int CommonPrefixLength(List<string[]> group)
    {
        var commonLen = group[0].Length;
        for (var i = 1; i < group.Count; i++)
        {
            var current = group[i];
            var len = Math.Min(commonLen, current.Length);
            var j = 0;
            while (j < len && string.Equals(group[0][j], current[j], StringComparison.OrdinalIgnoreCase))
                j++;
            commonLen = j;
            if (commonLen == 0)
                break;
        }
        return commonLen;
    }

    /// <summary>
    /// Setzt die ersten <paramref name="count"/> Segmente zu einem Pfad zusammen. Ein alleiniges
    /// Windows-Laufwerk („C:") bekommt den Trenner angehängt, damit es ein gültiges Verzeichnis
    /// bezeichnet – konsistent mit <see cref="GameDiscovery"/>s <c>CommonDirectory</c>.
    /// </summary>
    private static string JoinSegments(string[] segments, int count)
    {
        if (count <= 0)
            return string.Empty;
        var result = string.Join(Path.DirectorySeparatorChar, segments.Take(count));
        if (result.EndsWith(':'))
            result += Path.DirectorySeparatorChar;
        return result;
    }
}
