using System.IO;
using SaveVault.Core.Ludusavi;
using SaveVault.Core.Models;
using SaveVault.Core.Storage;

namespace SaveVault.Client.Services;

/// <summary>Ein von ludusavi erkanntes Spiel samt abgeleitetem lokalem Save-Ordner.</summary>
public sealed record DiscoveredGame(GameKey Game, string SaveFolder, int FileCount, long TotalBytes);

/// <summary>Warum ein erkanntes Spiel NICHT automatisch übernommen wurde.</summary>
public enum SkipReason
{
    /// <summary>Der abgeleitete Save-Ordner war zu breit gefasst / mehrdeutig (Laufwerks-/
    /// Systemwurzel oder auf eine breite Ahnen-Wurzel kollabiert, z. B. Steam-Root).</summary>
    AmbiguousFolder,

    /// <summary>Das Save-Set war zu groß (zu viele Dateien / zu viele Bytes) für den Auto-Sync.</summary>
    TooLarge,
}

/// <summary>
/// Ein bei der Erkennung übersprungenes Spiel: sein Anzeigename, der Grund und – optional –
/// eine Detailangabe (z. B. Datei-/Größenangabe bei <see cref="SkipReason.TooLarge"/>).
/// </summary>
public sealed record SkippedGame(string Name, SkipReason Reason, string? Detail = null);

/// <summary>
/// Ergebnis einer Erkennung. <see cref="LudusaviAvailable"/> = false bedeutet: die
/// mitgelieferte Binary fehlt (sauberer „nicht eingerichtet"-Pfad, kein Fehler).
/// <see cref="Error"/> trägt eine Meldung, wenn der Aufruf zwar möglich war, aber scheiterte.
/// <see cref="Skipped"/> nennt die Spiele, die erkannt, aber NICHT automatisch übernommen
/// wurden (mehrdeutiger Ordner oder zu großes Save-Set) – damit die GUI sie dauerhaft mit dem
/// Hinweis „bitte manuell zuordnen" anzeigen kann.
/// </summary>
public sealed record DiscoveryResult(
    bool LudusaviAvailable,
    IReadOnlyList<DiscoveredGame> Games,
    string? Error,
    IReadOnlyList<SkippedGame>? Skipped = null)
{
    /// <summary>Übersprungene Spiele (nie <c>null</c>).</summary>
    public IReadOnlyList<SkippedGame> Skipped { get; init; } = Skipped ?? Array.Empty<SkippedGame>();

    /// <summary>Anzeige-Helfer: Namen der mehrdeutig übersprungenen Spiele (für den Hinweis-Dialog).</summary>
    public IReadOnlyList<string> SkippedAmbiguous
        => Skipped.Where(s => s.Reason == SkipReason.AmbiguousFolder).Select(s => s.Name).ToList();

    /// <summary>Anzeige-Helfer: zu große Spiele mit Größenangabe (für den Hinweis-Dialog).</summary>
    public IReadOnlyList<string> SkippedTooLarge
        => Skipped.Where(s => s.Reason == SkipReason.TooLarge)
                  .Select(s => s.Detail is null ? s.Name : $"{s.Name} ({s.Detail})").ToList();
}

/// <summary>
/// Dünner Wrapper um <see cref="LudusaviClient"/>: ruft <c>backup --preview</c> auf,
/// leitet je erkanntem Spiel den gemeinsamen Save-Ordner aus den Dateipfaden ab und
/// bildet daraus <see cref="DiscoveredGame"/>-Einträge. Fehlt die Binary oder scheitert
/// der Aufruf, wird das als Ergebnis gemeldet – <b>nie</b> als unbehandelte Exception.
/// </summary>
public sealed class GameDiscovery
{
    private readonly LudusaviClient _ludusavi;

    public GameDiscovery(LudusaviClient ludusavi)
        => _ludusavi = ludusavi ?? throw new ArgumentNullException(nameof(ludusavi));

    /// <summary>Ob die ludusavi-Binary vorhanden ist.</summary>
    public bool IsAvailable => _ludusavi.IsAvailable;

    /// <summary>Erkennt installierte Spiele und ihre Save-Ordner.</summary>
    public async Task<DiscoveryResult> DiscoverAsync(CancellationToken ct = default)
    {
        if (!_ludusavi.IsAvailable)
            return new DiscoveryResult(false, Array.Empty<DiscoveredGame>(),
                $"ludusavi-Binary nicht gefunden ({_ludusavi.ExecutablePath}).");

        LudusaviBackupPreview preview;
        try
        {
            preview = await _ludusavi.BackupPreviewAsync(game: null, ct: ct).ConfigureAwait(false);
        }
        catch (LudusaviNotAvailableException ex)
        {
            return new DiscoveryResult(false, Array.Empty<DiscoveredGame>(), ex.Message);
        }
        catch (LudusaviException ex)
        {
            return new DiscoveryResult(true, Array.Empty<DiscoveredGame>(), ex.Message);
        }

        var games = new List<DiscoveredGame>();
        var skipped = new List<SkippedGame>();
        foreach (var (name, backup) in preview.Games)
        {
            ct.ThrowIfCancellationRequested();
            if (backup?.Files is null || backup.Files.Count == 0)
                continue;

            var filePaths = backup.Files.Keys;
            var folder = CommonDirectory(filePaths);
            if (folder is null)
                continue;

            // Zu breite Ordner (Laufwerks-/Systemwurzel) NICHT übernehmen – sie würden beim
            // Scannen/Überwachen die ganze Platte umfassen und den Client blockieren.
            if (SaveFolderSafety.IsTooBroad(folder))
            {
                skipped.Add(new SkippedGame(name, SkipReason.AmbiguousFolder));
                continue;
            }

            var fileCount = backup.Files.Count;
            var totalBytes = backup.Files.Values.Sum(f => f.Bytes);

            // Zu große Save-Sets (z. B. Project Zomboid: zehntausende Chunk-Dateien, mehrere GB)
            // NICHT automatisch übernehmen – sie würden das Hashen/Uploaden über Stunden
            // blockieren und (weil der Rescan sequenziell läuft) alle weiteren Spiele ausbremsen.
            if (SaveFolderSafety.IsSaveSetTooLarge(fileCount, totalBytes))
            {
                skipped.Add(new SkippedGame(name, SkipReason.TooLarge,
                    $"{FormatFileCount(fileCount)} Dateien, {FormatBytes(totalBytes)}"));
                continue;
            }

            // Ordner-Kollaps (z. B. Street Fighter 6): streuen die Savegame-Dateien über mehrere
            // Unterbäume (etwa Steam userdata + steamapps), kollabiert der gemeinsame Nenner auf
            // eine breite Ahnen-Wurzel wie die Steam-Installationswurzel. Diese ist nicht formal
            // „zu breit" und laut ludusavi klein, würde beim Scannen aber die ganze Steam-Bibliothek
            // umfassen. Erkennung: enthält der abgeleitete Ordner VIEL mehr Dateien als das Spiel
            // laut ludusavi besitzt, ist er zu weit gefasst → überspringen (als mehrdeutig melden).
            if (FolderMuchLargerThanSaves(folder, fileCount, ct))
            {
                skipped.Add(new SkippedGame(name, SkipReason.AmbiguousFolder));
                continue;
            }

            games.Add(new DiscoveredGame(GameKey.FromName(name), folder, fileCount, totalBytes));
        }

        return new DiscoveryResult(true, games, null, skipped);
    }

    /// <summary>Dateizahl mit Tausenderpunkten (de-DE), z. B. <c>12.480</c>.</summary>
    private static string FormatFileCount(int count)
        => count.ToString("#,0", System.Globalization.CultureInfo.GetCultureInfo("de-DE"));

    /// <summary>
    /// Menschenlesbare Byte-Größe (GB/MB/KB) mit einer Nachkommastelle und de-DE-Komma.
    /// Bewusst simpel und lokal, damit dieser Client-Service ohne UI-Abhängigkeiten auskommt.
    /// </summary>
    private static string FormatBytes(long bytes)
    {
        var de = System.Globalization.CultureInfo.GetCultureInfo("de-DE");
        const double kb = 1024d, mb = kb * 1024d, gb = mb * 1024d;
        if (bytes >= gb)
            return (bytes / gb).ToString("0.0", de) + " GB";
        if (bytes >= mb)
            return (bytes / mb).ToString("0.0", de) + " MB";
        if (bytes >= kb)
            return (bytes / kb).ToString("0.0", de) + " KB";
        return bytes.ToString(de) + " B";
    }

    /// <summary>
    /// <c>true</c>, wenn der abgeleitete Ordner <paramref name="folder"/> auf der Platte
    /// DEUTLICH mehr Dateien enthält, als das Spiel laut ludusavi besitzt
    /// (<paramref name="saveFileCount"/>) – ein Zeichen dafür, dass der gemeinsame Nenner der
    /// Save-Pfade auf eine zu breite Ahnen-Wurzel (z. B. die Steam-Installationswurzel bei
    /// Street&#160;Fighter&#160;6) kollabiert ist. Solche Ordner sind formal nicht „zu breit",
    /// würden beim Scannen/Überwachen aber die ganze Steam-Bibliothek umfassen.
    ///
    /// <para>Wichtig: Die Zählung ist <b>beschränkt</b> und bricht ab, sobald das Limit
    /// überschritten ist – sie enumeriert also NIE einen riesigen Baum vollständig und wird
    /// selbst nie zum Show-Stopper. Unlesbare Unterordner werden übersprungen
    /// (<see cref="EnumerationOptions.IgnoreInaccessible"/>); Reparse-Points (Symlinks/
    /// Junctions) werden nicht verfolgt. Ist der Ordner gar nicht lesbar, gilt er im Zweifel
    /// als zu weit gefasst (sicherer Default → überspringen).</para>
    /// </summary>
    private static bool FolderMuchLargerThanSaves(string folder, int saveFileCount, CancellationToken ct)
    {
        // Wie viele Dateien darf der abgeleitete Ordner höchstens enthalten, bevor er als
        // „viel zu weit gefasst" gilt? Großzügiger Spielraum über die von ludusavi gemeldete
        // Save-Dateizahl hinaus (Begleitdateien im selben Ordner sind normal), aber weit
        // unterhalb einer ganzen Steam-Bibliothek. Zusätzlicher Sockel für sehr kleine Sets.
        var limit = Math.Max(saveFileCount * 4, saveFileCount + 100);

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint, // Symlinks/Junctions nicht folgen
        };

        try
        {
            var count = 0;
            foreach (var _ in Directory.EnumerateFiles(folder, "*", options))
            {
                ct.ThrowIfCancellationRequested();
                if (++count > limit)
                    return true; // deutlich mehr Dateien als das Spiel besitzt → zu weit gefasst
            }
            return false;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Ordner nicht (mehr) lesbar: im Zweifel als zu weit gefasst behandeln.
            return true;
        }
    }

    /// <summary>
    /// Bestimmt das gemeinsame Wurzelverzeichnis einer Menge von Dateipfaden (der
    /// Save-Ordner). Pfade werden erst auf Vollpfade normalisiert, dann segmentweise
    /// der gemeinsame Präfix gebildet. Liegt kein gemeinsames Verzeichnis vor, <c>null</c>.
    /// </summary>
    internal static string? CommonDirectory(IEnumerable<string> filePaths)
    {
        var dirs = new List<string[]>();
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
            dirs.Add(dir.Split(Path.DirectorySeparatorChar));
        }

        if (dirs.Count == 0)
            return null;

        var prefix = dirs[0];
        var commonLen = prefix.Length;
        for (var i = 1; i < dirs.Count; i++)
        {
            var current = dirs[i];
            var len = Math.Min(commonLen, current.Length);
            var j = 0;
            while (j < len && string.Equals(prefix[j], current[j], StringComparison.OrdinalIgnoreCase))
                j++;
            commonLen = j;
            if (commonLen == 0)
                return null;
        }

        var segments = prefix.Take(commonLen).ToArray();
        var result = string.Join(Path.DirectorySeparatorChar, segments);

        // Windows-Laufwerk („C:") allein ist kein gültiges Verzeichnis → Separator anhängen.
        if (result.EndsWith(':'))
            result += Path.DirectorySeparatorChar;

        return string.IsNullOrEmpty(result) ? null : result;
    }
}
