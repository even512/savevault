using System.Security.Cryptography;
using System.Text;
using SaveVault.Core.Models;

namespace SaveVault.Core.Storage;

/// <summary>
/// Wandelt fremd gelieferte Spielschlüssel und Namen in SICHERE Ablage-Bausteine um
/// und prüft, ob ein Zielpfad garantiert unterhalb eines Wurzelverzeichnisses liegt.
/// Nichts von außen wird je roh als Pfad verwendet: Spielschlüssel werden gehasht,
/// Namenssegmente hart saniert, und Traversal (<c>..</c>, absolute Pfade,
/// Laufwerksangaben) wird abgewiesen. Von Server und Client nutzbar.
/// </summary>
public static class PathSanitizer
{
    /// <summary>Deterministischer, traversal-freier Ordnername aus einem beliebigen String (SHA-256 hex).</summary>
    public static string HashKey(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
    }

    /// <summary>Sicherer Ordnername für ein Spiel: gehashter kanonischer Schlüssel.</summary>
    public static string SafeGameFolder(GameKey game)
    {
        ArgumentNullException.ThrowIfNull(game);
        return HashKey(game.Value);
    }

    /// <summary>
    /// Hart saniertes Einzel-Segment (ein Ordner-/Dateiname, KEIN Pfad): nur
    /// Buchstaben/Ziffern/'-'/'_'/'.', alles andere wird zu '_'. Leere,
    /// reine-Punkt- und Traversal-Namen werden zu "_". Länge begrenzt.
    /// </summary>
    public static string SanitizeSegment(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "_";

        var sb = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            if (char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.')
                sb.Append(ch);
            else
                sb.Append('_');
        }

        var s = sb.ToString().Trim('.', ' ');
        if (s.Length == 0 || s is "." or "..")
            return "_";

        return s.Length > 120 ? s[..120] : s;
    }

    /// <summary>
    /// Wandelt einen fremd gelieferten RELATIVEN Pfad (aus einem Manifest) in einen
    /// sicheren, strukturerhaltenden ZIP-Eintragsnamen um: '\' wird zu '/', ein etwaiges
    /// Laufwerk/Root wird entfernt, jedes Segment wird hart saniert (<see cref="SanitizeSegment"/>),
    /// und Traversal-Segmente (<c>.</c>, <c>..</c>, leer) fallen weg. Ergebnis ist immer
    /// relativ, enthält nie <c>..</c> und kann beim Entpacken nicht aus dem Zielordner
    /// ausbrechen. Entartet der Pfad zu nichts, wird <c>"_"</c> geliefert.
    /// </summary>
    public static string SafeZipEntryName(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return "_";

        var normalized = relativePath.Replace('\\', '/');
        var safeSegments = new List<string>();
        foreach (var segment in normalized.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment is "." or "..")
                continue; // Traversal/aktuelles Verzeichnis nie übernehmen
            // Laufwerks-/UNC-Reste (z. B. "C:") werden von SanitizeSegment entschärft.
            safeSegments.Add(SanitizeSegment(segment));
        }

        return safeSegments.Count == 0 ? "_" : string.Join('/', safeSegments);
    }

    /// <summary>
    /// Liegt <paramref name="candidate"/> garantiert unterhalb (oder auf) von
    /// <paramref name="root"/>? Beide Pfade werden zu absoluten Vollpfaden aufgelöst,
    /// bevor verglichen wird (fängt <c>..</c>-Auflösungen ab).
    /// </summary>
    public static bool IsWithinRoot(string root, string candidate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(candidate);

        var fullRoot = Path.GetFullPath(root);
        var fullCandidate = Path.GetFullPath(candidate);

        if (string.Equals(fullCandidate, fullRoot, StringComparison.OrdinalIgnoreCase))
            return true;

        var rootWithSep = fullRoot.EndsWith(Path.DirectorySeparatorChar)
            ? fullRoot
            : fullRoot + Path.DirectorySeparatorChar;

        return fullCandidate.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Löst einen fremd gelieferten RELATIVEN Pfad sicher unter <paramref name="root"/>
    /// auf. Gibt false zurück (statt zu werfen), wenn der Eingabepfad absolut/rooted
    /// ist, ein <c>..</c>-Segment enthält, auf das Wurzelverzeichnis <b>selbst</b> kollabiert
    /// (z. B. <c>"."</c> / <c>"./"</c> – kein gültiger Datei-Zielpfad) oder das Ergebnis aus dem
    /// Wurzelverzeichnis herausführen würde. Nur bei true ist <paramref name="fullPath"/> gesetzt
    /// und zeigt garantiert auf einen Ort <b>echt unterhalb</b> von <paramref name="root"/>.
    /// </summary>
    public static bool TryResolveWithin(string root, string untrustedRelative, out string fullPath)
    {
        fullPath = string.Empty;

        if (string.IsNullOrWhiteSpace(root)) return false;
        if (string.IsNullOrWhiteSpace(untrustedRelative)) return false;

        // Absolute Pfade und Laufwerks-/UNC-Angaben abweisen.
        if (Path.IsPathRooted(untrustedRelative)) return false;

        var normalized = untrustedRelative.Replace('\\', '/');

        // Segmentweise: kein einziges '..'-Segment zulassen.
        foreach (var segment in normalized.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment is "..")
                return false;
        }

        // Pfad-Auflösung kann bei entarteten Eingaben werfen (ungültige Zeichen, zu lang);
        // vertragsgemäß gibt diese Methode dann false zurück, statt zu werfen.
        string fullRoot;
        string combined;
        try
        {
            fullRoot = Path.GetFullPath(root);
            combined = Path.GetFullPath(Path.Combine(fullRoot, normalized));
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            return false;
        }

        // Ein relativer Pfad, der auf das Wurzelverzeichnis SELBST kollabiert (z. B. "." oder
        // "./."), ist kein gültiges Datei-Ziel: sonst könnte ein Aufrufer, der an das Ergebnis
        // ein Suffix hängt (z. B. eine Temp-Endung), OBERHALB der Wurzel schreiben. Ablehnen.
        var normalizedRoot = fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedCombined = combined.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(normalizedCombined, normalizedRoot, StringComparison.OrdinalIgnoreCase))
            return false;

        // Doppelte Absicherung: auch nach der Auflösung muss es unter root liegen.
        if (!IsWithinRoot(fullRoot, combined))
            return false;

        fullPath = combined;
        return true;
    }
}
