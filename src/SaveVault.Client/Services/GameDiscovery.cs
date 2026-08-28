using System.IO;
using SaveVault.Core.Ludusavi;
using SaveVault.Core.Models;
using SaveVault.Core.Storage;

namespace SaveVault.Client.Services;

/// <summary>Ein von ludusavi erkanntes Spiel samt abgeleitetem lokalem Save-Ordner.</summary>
public sealed record DiscoveredGame(GameKey Game, string SaveFolder, int FileCount, long TotalBytes);

/// <summary>
/// Ergebnis einer Erkennung. <see cref="LudusaviAvailable"/> = false bedeutet: die
/// mitgelieferte Binary fehlt (sauberer „nicht eingerichtet"-Pfad, kein Fehler).
/// <see cref="Error"/> trägt eine Meldung, wenn der Aufruf zwar möglich war, aber
/// scheiterte. <see cref="SkippedAmbiguous"/> nennt die Anzeigenamen der Spiele, deren
/// abgeleiteter Save-Ordner zu breit war (Laufwerks-/Systemwurzel) und die deshalb
/// übersprungen wurden – für eine spätere Anzeige. <see cref="SkippedTooLarge"/> nennt die
/// Spiele, deren Save-Set zu groß war (zu viele Dateien / zu viele Bytes) und die deshalb
/// nicht automatisch synchronisiert werden – jeweils mit Größenangabe. Beide Parameter sind
/// optional, damit bestehende Aufrufer unverändert bleiben; <c>null</c> wird als „keine" behandelt.
/// </summary>
public sealed record DiscoveryResult(
    bool LudusaviAvailable,
    IReadOnlyList<DiscoveredGame> Games,
    string? Error,
    IReadOnlyList<string>? SkippedAmbiguous = null,
    IReadOnlyList<string>? SkippedTooLarge = null)
{
    /// <summary>Übersprungene, mehrdeutige Spiele (nie <c>null</c>).</summary>
    public IReadOnlyList<string> SkippedAmbiguous { get; init; }
        = SkippedAmbiguous ?? Array.Empty<string>();

    /// <summary>Übersprungene, zu große Spiele mit Größenangabe (nie <c>null</c>).</summary>
    public IReadOnlyList<string> SkippedTooLarge { get; init; }
        = SkippedTooLarge ?? Array.Empty<string>();
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
        var skipped = new List<string>();
        var skippedTooLarge = new List<string>();
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
                skipped.Add(name);
                continue;
            }

            var fileCount = backup.Files.Count;
            var totalBytes = backup.Files.Values.Sum(f => f.Bytes);

            // Zu große Save-Sets (z. B. Project Zomboid: zehntausende Chunk-Dateien, mehrere GB)
            // NICHT automatisch übernehmen – sie würden das Hashen/Uploaden über Stunden
            // blockieren und (weil der Rescan sequenziell läuft) alle weiteren Spiele ausbremsen.
            if (SaveFolderSafety.IsSaveSetTooLarge(fileCount, totalBytes))
            {
                skippedTooLarge.Add($"{name} ({FormatFileCount(fileCount)} Dateien, {FormatBytes(totalBytes)})");
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
                skipped.Add(name);
                continue;
            }

            games.Add(new DiscoveredGame(GameKey.FromName(name), folder, fileCount, totalBytes));
        }

        return new DiscoveryResult(true, games, null, skipped, skippedTooLarge);
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
