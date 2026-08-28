using System.Globalization;
using System.IO.Compression;
using System.Text;
using SaveVault.Core.Api;
using SaveVault.Core.Storage;

namespace SaveVault.Server.Storage;

/// <summary>
/// Packt eine Revision aus dem inhaltsadressierten Speicher wieder in ihre ORIGINAL-
/// Ordnerstruktur und schreibt sie als ZIP in einen Ausgabestream (Dashboard-Download).
/// Die Blob-Bytes werden über den übergebenen <c>openContent</c>-Zugriff gelesen; die
/// Zuordnung „welcher Hash gehört auf welchen relativen Pfad" liefert das Manifest.
///
/// Sicherheit: Jeder Eintragsname stammt aus fremd geliefertem Manifest-Inhalt und wird
/// über <see cref="PathSanitizer.SafeZipEntryName"/> entschärft – nie <c>..</c>, nie
/// absolut, jedes Segment hart saniert. So kann ein präpariertes Manifest beim Entpacken
/// nicht aus dem Zielordner ausbrechen. Namenskollisionen nach dem Sanieren werden
/// durch einen Zähler-Suffix aufgelöst, damit kein Eintrag einen anderen verdeckt.
/// </summary>
public static class RevisionExporter
{
    /// <summary>Schlägt einen Dateinamen für den Download vor (saniert, immer .zip).</summary>
    public static string SuggestFileName(RevisionDownload rev)
    {
        var name = PathSanitizer.SanitizeSegment(rev.Game.DisplayName);
        return $"{name}-rev{rev.Number}.zip";
    }

    /// <summary>
    /// Schreibt die Revision als ZIP nach <paramref name="output"/>. <paramref name="openContent"/>
    /// liefert zu einem SHA-256 den Blob-Stream (oder null, wenn er fehlt); fehlende Blobs werden
    /// übersprungen, nicht abgebrochen. Der Ausgabestream wird nicht geschlossen (leaveOpen).
    /// </summary>
    public static async Task WriteZipAsync(
        RevisionDownload rev,
        string deviceName,
        Func<string, Stream?> openContent,
        Stream output,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(rev);
        ArgumentNullException.ThrowIfNull(openContent);
        ArgumentNullException.ThrowIfNull(output);

        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);

        // Info-Datei mit Standard-Save-Pfad und Herkunft.
        var infoEntry = zip.CreateEntry("SaveVault-Info.txt", CompressionLevel.Optimal);
        await using (var infoStream = infoEntry.Open())
        await using (var writer = new StreamWriter(infoStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
        {
            await writer.WriteAsync(BuildInfoText(rev, deviceName)).ConfigureAwait(false);
        }

        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in rev.Manifest.Entries)
        {
            ct.ThrowIfCancellationRequested();

            using var content = openContent(entry.Sha256);
            if (content is null)
                continue; // Blob nicht vorhanden (sollte nicht vorkommen) → auslassen statt abbrechen

            var name = EnsureUnique(PathSanitizer.SafeZipEntryName(entry.RelativePath), usedNames);
            var zipEntry = zip.CreateEntry(name, CompressionLevel.Optimal);

            // Zeitstempel nur setzen, wenn er ein gültiges ZIP-Datum (>= 1980) ergibt.
            if (entry.LastWriteUtc.Year >= 1980)
                zipEntry.LastWriteTime = new DateTimeOffset(DateTime.SpecifyKind(entry.LastWriteUtc, DateTimeKind.Utc));

            await using var target = zipEntry.Open();
            await content.CopyToAsync(target, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Sorgt für eindeutige Eintragsnamen (case-insensitiv), Kollision → " (2)"-Suffix.</summary>
    private static string EnsureUnique(string name, HashSet<string> used)
    {
        if (used.Add(name))
            return name;

        var dot = name.LastIndexOf('.');
        var stem = dot > 0 ? name[..dot] : name;
        var ext = dot > 0 ? name[dot..] : string.Empty;
        for (var i = 2; ; i++)
        {
            var candidate = $"{stem} ({i}){ext}";
            if (used.Add(candidate))
                return candidate;
        }
    }

    private static string BuildInfoText(RevisionDownload rev, string deviceName)
    {
        var de = CultureInfo.GetCultureInfo("de-DE");
        var sb = new StringBuilder();
        sb.AppendLine("SaveVault-Export");
        sb.AppendLine("================");
        sb.AppendLine($"Spiel:             {rev.Game.DisplayName}");
        sb.AppendLine($"Server-Schlüssel:  {rev.Game.Value}");
        if (!string.IsNullOrWhiteSpace(rev.Game.Store))
            sb.AppendLine($"Store:             {rev.Game.Store}");
        sb.AppendLine($"Revision:          {rev.Number}");
        sb.AppendLine($"Erstellt (UTC):    {rev.TimestampUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}");
        sb.AppendLine($"Quell-Gerät:       {deviceName} ({rev.DeviceId})");
        sb.AppendLine($"Dateien:           {rev.Manifest.FileCount.ToString("#,0", de)}");
        sb.AppendLine($"Größe:             {rev.Manifest.TotalBytes.ToString("#,0", de)} Bytes");
        sb.AppendLine();
        sb.AppendLine("Standard-Save-Pfad des Spiels (auf dem Quell-Gerät):");
        sb.AppendLine(string.IsNullOrWhiteSpace(rev.SaveRoot)
            ? "  unbekannt (ältere Revision ohne Pfadangabe)"
            : "  " + rev.SaveRoot);
        sb.AppendLine();
        sb.AppendLine("Dieser Ordner gibt die Originalstruktur der Savegames wieder. Zum manuellen");
        sb.AppendLine("Wiederherstellen den Inhalt (ohne diese Info-Datei) in den obigen Standard-Pfad");
        sb.AppendLine("kopieren – oder im SaveVault-Client die Funktion „Wiederherstellen“ nutzen.");
        return sb.ToString();
    }
}
