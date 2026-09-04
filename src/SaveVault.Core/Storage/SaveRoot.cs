namespace SaveVault.Core.Storage;

/// <summary>
/// Eine einzelne Save-Wurzel eines Spiels: der lokale <see cref="Folder"/> und sein stabiles,
/// geräteübergreifendes <see cref="Key"/> (siehe <see cref="SaveRootKey"/>). Ein Spiel hat eine
/// oder mehrere davon.
/// </summary>
public sealed record SaveRoot(string Key, string Folder);

/// <summary>
/// Bildet die Konvention ab, mit der die Dateien mehrerer Save-Wurzeln eines Spiels in EIN
/// Server-Manifest gelegt werden – ohne das Server-/Manifest-Format zu ändern.
///
/// <para><b>Präfix-Regel (kein Churn):</b> Hat ein Spiel nur <b>eine</b> Wurzel, bleiben die
/// relativen Pfade <b>unverändert</b> (kein Präfix) → bit-identische Manifeste wie bisher, kein
/// Reseed. Nur bei <b>mehreren</b> Wurzeln wird jedem Pfad der Root-Key als Präfix vorangestellt
/// (<c>"&lt;key&gt;/&lt;unterpfad&gt;"</c>), damit ein Restore die Datei später der richtigen
/// lokalen Wurzel zuordnen kann.</para>
/// </summary>
public static class SaveRootLayout
{
    /// <summary>
    /// <c>true</c>, wenn die Pfade dieses Root-Sets im Manifest mit dem Root-Key präfixiert werden
    /// (mehr als eine Wurzel). Bei genau einer Wurzel bleibt alles unpräfixiert (Rückwärtskompat.).
    /// </summary>
    public static bool UsesPrefix(int rootCount) => rootCount > 1;

    /// <summary>Setzt einen präfixierten Manifest-Pfad zusammen: <c>"&lt;key&gt;/&lt;sub&gt;"</c>.</summary>
    public static string Combine(string key, string relativeWithinRoot)
        => $"{key}/{relativeWithinRoot}";

    /// <summary>
    /// Bildet einen Manifest-Pfad auf die <b>lokale Wurzel</b> ab, in die er geschrieben werden soll,
    /// und liefert den Rest-Pfad <b>innerhalb</b> dieser Wurzel zurück. Reine String-Logik – die
    /// Traversal-Abwehr (kein <c>..</c>, kein Ausbrechen) macht der Aufrufer danach über
    /// <c>PathSanitizer.TryResolveWithin(folder, subPath)</c>.
    ///
    /// <list type="bullet">
    ///   <item><b>Eine Wurzel:</b> der Pfad ist unpräfixiert → er gehört komplett in die einzige
    ///     Wurzel (<paramref name="subPath"/> == <paramref name="relativePath"/>).</item>
    ///   <item><b>Mehrere Wurzeln:</b> die Wurzel mit dem längsten passenden <see cref="SaveRoot.Key"/>
    ///     gewinnt; <paramref name="subPath"/> ist der Rest hinter <c>"&lt;key&gt;/"</c>.</item>
    /// </list>
    ///
    /// Gibt <c>false</c> zurück, wenn kein Root-Key passt (<b>unbekannter/nicht abbildbarer Key</b> →
    /// der Aufrufer schreibt diesen Eintrag NICHT) oder der Rest-Pfad leer wäre.
    /// </summary>
    public static bool TryResolve(IReadOnlyList<SaveRoot> roots, string relativePath,
        out string folder, out string subPath)
    {
        folder = string.Empty;
        subPath = string.Empty;

        if (roots is null || roots.Count == 0 || string.IsNullOrEmpty(relativePath))
            return false;

        // Eine Wurzel → unpräfixiert, alles gehört dort hinein.
        if (roots.Count == 1)
        {
            folder = roots[0].Folder;
            subPath = relativePath;
            return true;
        }

        // Mehrere Wurzeln → längsten passenden Key-Präfix wählen.
        SaveRoot? best = null;
        foreach (var r in roots)
        {
            if (IsKeyPrefix(relativePath, r.Key) && (best is null || r.Key.Length > best.Key.Length))
                best = r;
        }
        if (best is null)
            return false;

        folder = best.Folder;
        subPath = relativePath[(best.Key.Length + 1)..]; // hinter "<key>/"
        return subPath.Length > 0;
    }

    /// <summary>
    /// <c>true</c>, wenn <paramref name="relativePath"/> mit <c>"&lt;key&gt;/"</c> beginnt
    /// (case-insensitiv). Die nachfolgende Trenner-Prüfung verhindert, dass <c>"FooBar/…"</c>
    /// fälschlich zum Key <c>"Foo"</c> passt.
    /// </summary>
    private static bool IsKeyPrefix(string relativePath, string key)
        => !string.IsNullOrEmpty(key)
           && relativePath.Length > key.Length
           && relativePath[key.Length] == '/'
           && relativePath.AsSpan(0, key.Length).Equals(key, StringComparison.OrdinalIgnoreCase);
}
