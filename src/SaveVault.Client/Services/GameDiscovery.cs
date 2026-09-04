using SaveVault.Core.Ludusavi;
using SaveVault.Core.Models;
using SaveVault.Core.Storage;

namespace SaveVault.Client.Services;

/// <summary>
/// Ein von ludusavi erkanntes Spiel samt abgeleiteten lokalen Save-Wurzeln. Ein Spiel kann
/// <b>mehrere</b> Wurzeln haben (Mehr-Ordner-Erkennung); <see cref="FileCount"/>/<see cref="TotalBytes"/>
/// gelten fürs ganze Spiel (Summe über alle Wurzeln).
/// </summary>
public sealed record DiscoveredGame(GameKey Game, IReadOnlyList<SaveRoot> Roots, int FileCount, long TotalBytes)
{
    /// <summary>Der primäre (erste) Ordner – für Anzeige/„Ordner öffnen".</summary>
    public string? PrimaryFolder => Roots.Count > 0 ? Roots[0].Folder : null;
}

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

            // Mehr-Ordner-Gruppierung: ludusavis Dateipfade in ihre natürlichen, engen Save-Wurzeln
            // zerlegen (steigt durch Container-/Systemwurzeln hindurch). Bleibt ein Teil zu breit/
            // unauflösbar oder gibt es gar keine Wurzel → als mehrdeutig überspringen (manuell zuordnen).
            var grouping = SaveRootGrouping.Group(filePaths);
            if (grouping.Roots.Count == 0 || !grouping.FullyResolved)
            {
                skipped.Add(new SkippedGame(name, SkipReason.AmbiguousFolder));
                continue;
            }

            var fileCount = backup.Files.Count;
            var totalBytes = backup.Files.Values.Sum(f => f.Bytes);

            // Zu große Save-Sets (z. B. Project Zomboid: zehntausende Chunk-Dateien, mehrere GB)
            // NICHT automatisch übernehmen – sie würden das Hashen/Uploaden über Stunden blockieren
            // und (weil der Rescan sequenziell läuft) alle weiteren Spiele ausbremsen. Der Schutz
            // gilt PRO SPIEL (Summe über alle Wurzeln), wie von ludusavi gemeldet.
            if (SaveFolderSafety.IsSaveSetTooLarge(fileCount, totalBytes))
            {
                skipped.Add(new SkippedGame(name, SkipReason.TooLarge,
                    $"{FormatFileCount(fileCount)} Dateien, {FormatBytes(totalBytes)}"));
                continue;
            }

            var roots = grouping.Roots
                .Select(folder => new SaveRoot(SaveRootKey.Derive(folder), folder))
                .ToList();
            games.Add(new DiscoveredGame(GameKey.FromName(name), roots, fileCount, totalBytes));
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
}
