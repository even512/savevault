using System.IO;

namespace SaveVault.Core.Storage;

/// <summary>
/// Entscheidet, ob ein Ordner als lokaler Save-Ordner ZU BREIT und damit gefährlich ist:
/// eine Laufwerks-/Pfadwurzel (<c>C:\</c>), eine bekannte System-/Sammelwurzel
/// (Benutzerprofil, <c>AppData</c>, <c>ProgramData</c>, <c>Program Files</c>, <c>Windows</c> …)
/// oder ein zu flacher Pfad. Solche Ordner würden beim Scannen/Überwachen praktisch die
/// ganze Platte umfassen und den Client blockieren. Rein rechnend – kein Netz, kein Prozess.
/// </summary>
public static class SaveFolderSafety
{
    /// <summary>
    /// <c>true</c>, wenn <paramref name="path"/> leer/null ist, eine reine Laufwerks- bzw.
    /// Pfadwurzel darstellt (<c>C:\</c>, <c>X:\</c>, auch <c>C:/</c>) oder wenn der aufgelöste
    /// Vollpfad seiner eigenen Wurzel entspricht. Deterministisch und ohne
    /// <see cref="Environment"/>. Entartete/ungültige Pfade gelten als zu breit (sicherer Default).
    /// </summary>
    public static bool IsDriveRootOrEmpty(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return true;

        try
        {
            var full = Path.GetFullPath(path);
            var root = Path.GetPathRoot(full) ?? string.Empty;
            return string.Equals(TrimEndSeparators(full), TrimEndSeparators(root),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // Ungültiger Pfad → als zu breit behandeln, damit der Aufrufer ihn verwirft.
            return true;
        }
    }

    /// <summary>
    /// Reine, unit-testbare Kernlogik: <c>true</c>, wenn <paramref name="path"/> eine
    /// Laufwerks-/Pfadwurzel ist (<see cref="IsDriveRootOrEmpty"/>), ODER weniger als zwei
    /// Pfadsegmente unterhalb der Wurzel liegt (flacher als <c>Laufwerk\A\B</c>), ODER der
    /// normalisierte Vollpfad (case-insensitive, ohne abschließende Trenner) einem der
    /// <paramref name="broadRoots"/> entspricht.
    /// </summary>
    internal static bool IsTooBroad(string? path, IReadOnlyCollection<string> broadRoots)
    {
        ArgumentNullException.ThrowIfNull(broadRoots);

        if (IsDriveRootOrEmpty(path))
            return true;

        string full;
        string root;
        try
        {
            full = Path.GetFullPath(path!);
            root = Path.GetPathRoot(full) ?? string.Empty;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return true;
        }

        // Segmente unterhalb der Wurzel zählen: weniger als zwei ⇒ zu flach (z. B. C:\Users).
        var remainder = full.Length > root.Length ? full[root.Length..] : string.Empty;
        var segments = remainder.Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2)
            return true;

        // Bekannte System-/Sammelwurzel? (case-insensitiver Vollpfad-Vergleich)
        var normalized = TrimEndSeparators(full);
        foreach (var broad in broadRoots)
        {
            if (string.IsNullOrWhiteSpace(broad))
                continue;
            if (string.Equals(normalized, TrimEndSeparators(broad), StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Gesamturteil für die Laufzeit: nutzt die zur Laufzeit ermittelten bekannten
    /// System-/Sammelwurzeln. <c>true</c> ⇒ der Ordner ist zu breit und darf nicht als
    /// Save-Ordner verwendet werden.
    /// </summary>
    public static bool IsTooBroad(string? path)
        => IsTooBroad(path, RuntimeBroadRoots());

    /// <summary>
    /// Ermittelt die bekannten breiten Wurzeln aus <see cref="Environment.GetFolderPath(Environment.SpecialFolder)"/>.
    /// Jede Angabe wird tolerant behandelt (leere/fehlerhafte übersprungen) und über
    /// <see cref="Path.GetFullPath(string)"/> normalisiert. Bewusst NICHT enthalten:
    /// <c>MyDocuments</c> – damit Spiele mit Saves direkt in „Dokumente" nicht verloren gehen.
    /// </summary>
    private static IReadOnlyCollection<string> RuntimeBroadRoots()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                return;
            try
            {
                set.Add(TrimEndSeparators(Path.GetFullPath(candidate)));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                // Nicht auflösbare Wurzel überspringen.
            }
        }

        string? Folder(Environment.SpecialFolder folder)
        {
            try { return Environment.GetFolderPath(folder); }
            catch { return null; }
        }

        string? Parent(string? child)
        {
            if (string.IsNullOrWhiteSpace(child))
                return null;
            try { return Path.GetDirectoryName(child); }
            catch { return null; }
        }

        var userProfile = Folder(Environment.SpecialFolder.UserProfile);
        var roaming = Folder(Environment.SpecialFolder.ApplicationData);

        Add(userProfile);
        Add(Parent(userProfile));                 // …\Users
        Add(roaming);                             // …\AppData\Roaming
        Add(Parent(roaming));                     // …\AppData
        Add(Folder(Environment.SpecialFolder.LocalApplicationData));
        Add(Folder(Environment.SpecialFolder.CommonApplicationData)); // ProgramData
        Add(Folder(Environment.SpecialFolder.ProgramFiles));
        Add(Folder(Environment.SpecialFolder.ProgramFilesX86));
        Add(Folder(Environment.SpecialFolder.Windows));

        return set;
    }

    private static string TrimEndSeparators(string path)
        => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
