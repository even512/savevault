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
/// übersprungen wurden – für eine spätere Anzeige. Der Parameter ist optional, damit
/// bestehende Aufrufer unverändert bleiben; <c>null</c> wird als „keine" behandelt.
/// </summary>
public sealed record DiscoveryResult(
    bool LudusaviAvailable,
    IReadOnlyList<DiscoveredGame> Games,
    string? Error,
    IReadOnlyList<string>? SkippedAmbiguous = null)
{
    /// <summary>Übersprungene, mehrdeutige Spiele (nie <c>null</c>).</summary>
    public IReadOnlyList<string> SkippedAmbiguous { get; init; }
        = SkippedAmbiguous ?? Array.Empty<string>();
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

            var totalBytes = backup.Files.Values.Sum(f => f.Bytes);
            games.Add(new DiscoveredGame(GameKey.FromName(name), folder, backup.Files.Count, totalBytes));
        }

        return new DiscoveryResult(true, games, null, skipped);
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
