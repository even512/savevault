using System.IO;
using System.Text;

namespace SaveVault.Core.Storage;

/// <summary>
/// Leitet für einen lokalen Save-Ordner ein <b>stabiles Root-Kennzeichen</b> ab: einen kurzen,
/// deterministischen Schlüssel, unter dem ein Download/Restore die Dateien auf einem <i>anderen</i>
/// Gerät wieder in den richtigen lokalen Ordner schreibt. Der Schlüssel ist ein <b>semantischer
/// Anker</b> plus der app-definierte Unterpfad darunter.
///
/// <para><b>Warum lexikalisch (kein <see cref="Environment"/>, kein IO):</b> so ist die Ableitung
/// maschinenunabhängig testbar und – wichtiger – über die Geräte eines Nutzers hinweg konsistent.
/// Der maschinenabhängige Präfix (Laufwerk, Profilpfad wie <c>C:\Users\&lt;name&gt;</c>) wird durch
/// den Anker abstrahiert; die konto-spezifischen Segmente (Steam-ID, Ubisoft-GUID) sind für einen
/// Nutzer über seine Geräte hinweg konstant und bleiben Teil des Schlüssels.</para>
///
/// <para><b>Konto-verankerte Orte</b> (geräteübergreifend stabil, ohne Laufwerk):
/// Steam <c>userdata\&lt;id&gt;</c>, Ubisoft <c>savegames\&lt;guid&gt;</c>, <c>AppData\Local</c>/
/// <c>LocalLow</c>/<c>Roaming</c>, <c>Saved Games</c>, <c>Documents</c>.</para>
///
/// <para><b>Installations-Orte</b> (<c>steamapps\common</c>, <c>GOG Games</c>, <c>Epic Games</c>)
/// tragen das <b>Laufwerk</b> im Schlüssel: ein Spiel kann parallel auf zwei Laufwerken liegen
/// (echt gemessen: Resident Evil Village mit identischem <c>config.ini</c> auf C: und D:), und ohne
/// Laufwerk kollidierten beide Wurzeln im Manifest. Solche Orte enthalten ohnehin nur Configs, keine
/// kontogebundenen Cloud-Saves; auf einem Gerät ohne dasselbe Laufwerk wird der Eintrag beim Restore
/// schlicht nicht abgebildet (kein Blindschreiben).</para>
///
/// <para>Ergebnis nutzt <c>/</c> als Trenner (wie die Manifest-Pfade). Nur für <b>Mehr-Root</b>-Spiele
/// relevant – Einfach-Root-Spiele bekommen im Manifest keinen Präfix (bit-identisch zu heute).</para>
/// </summary>
public static class SaveRootKey
{
    /// <summary>
    /// Leitet den Root-Key für <paramref name="folder"/> ab. Leerer/ungültiger Pfad ⇒ leerer String
    /// (der Aufrufer behandelt das als „kein Key ableitbar").
    /// </summary>
    public static string Derive(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
            return string.Empty;

        string full;
        try
        {
            full = Path.GetFullPath(folder.Replace('\\', '/'));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return string.Empty;
        }

        var segs = full.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        if (segs.Length == 0)
            return string.Empty;

        var low = new string[segs.Length];
        for (var i = 0; i < segs.Length; i++)
            low[i] = segs[i].ToLowerInvariant();

        // 1. Steam userdata\<id>\… (konto-verankert; Laufwerk/Install-Präfix abstrahiert).
        for (var i = 0; i + 1 < segs.Length; i++)
        {
            if (low[i] == "userdata" && IsAllDigits(segs[i + 1]))
                return Join("Steam/userdata", segs, i + 1);
        }

        // 2. Ubisoft …\Ubisoft Game Launcher\savegames\<guid>\… (konto-verankert).
        for (var i = 1; i < segs.Length; i++)
        {
            if (low[i] == "savegames" && low[i - 1] == "ubisoft game launcher")
                return Join("Ubisoft/savegames", segs, i + 1);
        }

        // 3. …\steamapps\common\<Spiel>\… (Installations-Ort → Laufwerk im Key).
        for (var i = 1; i < segs.Length; i++)
        {
            if (low[i] == "common" && low[i - 1] == "steamapps")
                return Join($"SteamCommon/{Drive(segs[0])}", segs, i + 1);
        }

        // 4. Launcher-Bibliotheken (Installations-Orte → Laufwerk im Key).
        for (var i = 0; i < segs.Length; i++)
        {
            if (low[i] == "gog games")
                return Join($"GogGames/{Drive(segs[0])}", segs, i + 1);
            if (low[i] == "epic games")
                return Join($"EpicGames/{Drive(segs[0])}", segs, i + 1);
        }

        // 5. Profil-Unterbaum <Laufwerk>\Users\<name>\… (konto-verankert; Profilpräfix abstrahiert).
        if (segs.Length >= 4 && IsDrive(segs[0]) && low[1] == "users")
        {
            var s3 = low[3];
            if (s3 == "saved games")
                return Join("SavedGames", segs, 4);
            if (s3 == "documents")
                return Join("Documents", segs, 4);
            if (s3 == "appdata" && segs.Length >= 5 && low[4] is "local" or "locallow" or "roaming")
                return Join($"AppData/{Capitalize(low[4])}", segs, 5);
        }

        // 6. Auffanglösung: Laufwerk + voller Unterpfad (gerätespezifisch, aber kollisionsfrei).
        return Join($"Drive/{Drive(segs[0])}", segs, 1);
    }

    /// <summary>
    /// Setzt <paramref name="tag"/> und die Original-Segmente ab <paramref name="fromIndex"/> mit
    /// <c>/</c> zusammen. Fehlen Rest-Segmente, ist der Schlüssel der Anker allein.
    /// </summary>
    private static string Join(string tag, string[] segments, int fromIndex)
    {
        var sb = new StringBuilder(tag);
        for (var i = fromIndex; i < segments.Length; i++)
        {
            sb.Append('/');
            sb.Append(segments[i]);
        }
        return sb.ToString();
    }

    /// <summary>Laufwerksbuchstabe in Großschreibung (<c>c:</c> → <c>C</c>); sonst der Rohwert.</summary>
    private static string Drive(string segment)
        => IsDrive(segment) ? char.ToUpperInvariant(segment[0]).ToString() : segment;

    private static bool IsDrive(string segment)
        => segment.Length == 2 && segment[1] == ':' && char.IsLetter(segment[0]);

    private static bool IsAllDigits(string segment)
    {
        if (segment.Length == 0)
            return false;
        foreach (var ch in segment)
            if (!char.IsDigit(ch))
                return false;
        return true;
    }

    private static string Capitalize(string lower)
        => lower switch
        {
            "local" => "Local",
            "locallow" => "LocalLow",
            "roaming" => "Roaming",
            _ => lower,
        };
}
