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

    // ---------------------------------------------------------------------------
    //  Container-Wurzel-Erkennung
    // ---------------------------------------------------------------------------

    /// <summary>
    /// <c>true</c>, wenn <paramref name="path"/> eine bekannte <b>Container-Wurzel</b> ist:
    /// ein Sammelordner, dessen Kinder je Spiel/Konto getrennt liegen und den man daher
    /// <b>eine Ebene tiefer</b> betreten muss, statt ihn selbst als Save-Ordner zu nehmen
    /// (z. B. die Steam-Installationswurzel, <c>steamapps\common</c> oder Steams
    /// <c>userdata\&lt;steamid&gt;</c>). Solche Ordner sind formal nicht „zu breit"
    /// (<see cref="IsTooBroad(string?)"/>), würden aber beim Scannen die ganze
    /// Bibliothek/alle Konten umfassen. Wird von der Mehr-Ordner-Gruppierung genutzt, um
    /// beim Ableiten der Save-Wurzeln <b>durch</b> Container hindurch weiter zu splitten.
    ///
    /// <para><b>Rein lexikalisch</b> – kein Netz, kein IO, kein <see cref="Environment"/>.
    /// Der Vergleich läuft case-insensitiv auf dem normalisierten Pfad; <c>\</c> und <c>/</c>
    /// werden gleich behandelt. Ein Ordner, der bloß zufällig „steam" heißt (z. B.
    /// <c>…\Documents\Battlefield 6\settings\steam</c>) ist KEIN Container – deshalb sind die
    /// Steam-Muster an Laufwerk bzw. <c>Program Files</c> verankert, nicht an „endet auf steam".</para>
    ///
    /// <para><b>Gepflegte Musterliste</b> (deterministisch):</para>
    /// <list type="bullet">
    ///   <item>Steam-Install: <c>X:\Steam</c> (direkt auf einem Laufwerk),
    ///     <c>…\Program Files\Steam</c>, <c>…\Program Files (x86)\Steam</c>, <c>…\SteamLibrary</c>.</item>
    ///   <item><c>…\steamapps</c>, <c>…\steamapps\common</c>.</item>
    ///   <item><c>…\Steam\userdata</c>, <c>…\userdata</c>, <c>…\userdata\&lt;steamid&gt;</c> (nur Ziffern).</item>
    ///   <item><c>…\Ubisoft Game Launcher</c>, <c>…\Ubisoft Game Launcher\savegames</c>,
    ///     <c>…\savegames\&lt;accountGuid&gt;</c> (voller GUID – ein Unreal-<c>Saved\SaveGames</c>
    ///     ist damit KEIN Container).</item>
    ///   <item><c>…\GOG Games</c>, <c>…\Epic Games</c>, <c>…\EA Games</c>, <c>…\Origin Games</c>.</item>
    /// </list>
    /// </summary>
    public static bool IsContainerRoot(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        // Rein lexikalische Normalisierung: '\' → '/', abschließende Trenner weg, klein.
        var low = path.Replace('\\', '/').TrimEnd('/').ToLowerInvariant();
        var segments = low.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            return false;

        var last = segments[^1];
        var prev = segments.Length >= 2 ? segments[^2] : string.Empty;

        // --- Steam-Install-Wurzel (an Laufwerk/Program Files verankert) ---
        // X:\Steam – genau „<Laufwerk>\Steam".
        if (segments.Length == 2 && IsDriveSegment(segments[0]) && last == "steam")
            return true;
        // …\Program Files\Steam bzw. …\Program Files (x86)\Steam.
        if (last == "steam" && prev is "program files" or "program files (x86)")
            return true;
        // …\SteamLibrary (zusätzliche Steam-Bibliothek, auch auf anderem Laufwerk).
        if (last == "steamlibrary")
            return true;

        // --- steamapps / steamapps\common ---
        if (last == "steamapps")
            return true;
        if (last == "common" && prev == "steamapps")
            return true;

        // --- Steam userdata ---
        // …\Steam\userdata bzw. bloßes …\userdata (Sammelordner über alle Konten).
        if (last == "userdata")
            return true;
        // …\userdata\<steamid> (nur Ziffern) – je Konto getrennt.
        if (prev == "userdata" && IsAllDigits(last))
            return true;

        // --- Ubisoft Game Launcher ---
        if (last == "ubisoft game launcher")
            return true;
        if (last == "savegames" && prev == "ubisoft game launcher")
            return true;
        // …\savegames\<accountGuid> (voller GUID) – je Ubisoft-Konto getrennt.
        if (prev == "savegames" && IsGuidSegment(last))
            return true;

        // --- Launcher-Spielebibliotheken ---
        if (last is "gog games" or "epic games" or "ea games" or "origin games")
            return true;

        return false;
    }

    /// <summary>
    /// <c>true</c>, wenn <paramref name="path"/> eine der <b>universellen Windows-Sammelwurzeln</b>
    /// ist – rein <b>lexikalisch</b>, unabhängig von Laufwerk und Benutzernamen: das
    /// Benutzerprofil selbst (<c>&lt;Laufwerk&gt;\Users\&lt;name&gt;</c>), der <c>AppData</c>-Ordner
    /// sowie seine drei Sammel-Unterordner <c>AppData\Local</c>, <c>AppData\LocalLow</c> und
    /// <c>AppData\Roaming</c>. Diese liegen strukturell fest und sind daher IMMER zu breit für einen
    /// Save-Ordner – auch dann, wenn der Pfad nicht das Profil des gerade laufenden Nutzers
    /// betrifft (Zweitprofil, andere Maschine). Ergänzt die laufzeit-ermittelten Sammelwurzeln aus
    /// <see cref="IsTooBroad(string?)"/> um genau diese maschinenunabhängige Basis; die
    /// Mehr-Ordner-Gruppierung steigt dadurch verlässlich durch sie hindurch.
    ///
    /// <para><b>Bewusst NICHT hier:</b> ein konkreter Vendor-/Spielordner darunter
    /// (<c>AppData\Roaming\HelloGames</c>, <c>AppData\LocalLow\Vendor\Spiel</c>) bleibt akzeptabel –
    /// nur die Sammelebene selbst zählt. <c>Documents</c> gilt weiterhin NICHT als breit (Spiele
    /// speichern direkt dort).</para>
    /// </summary>
    public static bool IsBroadUserStructure(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var low = path.Replace('\\', '/').TrimEnd('/').ToLowerInvariant();
        var segments = low.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            return false;

        var last = segments[^1];
        var prev = segments.Length >= 2 ? segments[^2] : string.Empty;

        // …\AppData (Sammelebene über Local/LocalLow/Roaming).
        if (last == "appdata")
            return true;
        // …\AppData\Local | \LocalLow | \Roaming (je Sammelebene).
        if (prev == "appdata" && last is "local" or "locallow" or "roaming")
            return true;
        // Benutzerprofil selbst: genau <Laufwerk>\Users\<name>.
        if (segments.Length == 3 && IsDriveSegment(segments[0]) && segments[1] == "users")
            return true;

        return false;
    }

    /// <summary><c>true</c>, wenn das Segment ein reines Laufwerk ist (<c>c:</c>, <c>d:</c>).</summary>
    private static bool IsDriveSegment(string segment)
        => segment.Length == 2 && segment[1] == ':' && char.IsLetter(segment[0]);

    /// <summary><c>true</c>, wenn das (bereits nicht-leere) Segment nur aus Ziffern besteht.</summary>
    private static bool IsAllDigits(string segment)
    {
        foreach (var ch in segment)
            if (!char.IsDigit(ch))
                return false;
        return segment.Length > 0;
    }

    /// <summary>
    /// <c>true</c>, wenn das Segment einem vollständigen GUID entspricht
    /// (<c>8-4-4-4-12</c> Hex mit Bindestrichen). Streng genug, damit gewöhnliche
    /// Save-Namen (z. B. <c>TownToCityCampaignSave</c>) NICHT als Konto-GUID gelten.
    /// </summary>
    private static bool IsGuidSegment(string segment)
        => Guid.TryParseExact(segment, "D", out _);

    /// <summary>Obergrenze für die Dateizahl eines Save-Sets, bevor es als „zu groß" gilt.</summary>
    public const int MaxFileCount = 5000;

    /// <summary>Obergrenze für die Gesamtgröße eines Save-Sets in Bytes (2 GiB).</summary>
    public const long MaxTotalBytes = 2L * 1024 * 1024 * 1024;

    /// <summary>
    /// <c>true</c>, wenn ein Save-Set ZU GROSS ist: mehr als <see cref="MaxFileCount"/> Dateien
    /// ODER mehr als <see cref="MaxTotalBytes"/> Bytes. Solche Sets werden bewusst NICHT
    /// automatisch synchronisiert: Ordner wie der von Project Zomboid legen zehntausende
    /// Karten-Chunk-Dateien, Logs und Mods (mehrere GB) an, die beim ersten Sync das Hashen
    /// und Hochladen über Stunden blockieren würden – und damit (weil der Rescan sequenziell
    /// läuft) alle nachfolgenden Spiele ausbremsen. Standard ist deshalb: überspringen und
    /// melden; der Nutzer trägt bei Bedarf einen konkreten, kleineren Unterordner manuell nach.
    /// Rein rechnend – kein Netz, kein IO. Grenzen sind exklusiv (<c>&gt;</c>, nicht <c>&gt;=</c>).
    /// </summary>
    public static bool IsSaveSetTooLarge(int fileCount, long totalBytes)
        => fileCount > MaxFileCount || totalBytes > MaxTotalBytes;
}
