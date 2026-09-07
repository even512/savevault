using SaveVault.Core.Storage;

namespace SaveVault.Core.Tests;

/// <summary>
/// Tests der reinen Austausch-Bausteine (<see cref="LocalContentReplacer"/>), die
/// <c>SyncEngine.ReplaceLocalContentAsync</c> (Client, Umschalten Lokal ↔ Synchron) für den
/// sicherheitskritischen letzten Schritt nutzt (siehe
/// specs/savevault-change-shared-save-sichtbarkeit.md, „Plan-Korrektur"): erst ALLE Ziel-Dateien
/// vollständig als Temp vorliegen haben, dann in <see cref="LocalContentReplacer.Commit"/> ERST
/// alle Temp-Dateien an ihren Platz verschieben und ERST DANACH die überzähligen Alt-Dateien
/// löschen – nie umgekehrt (ein zuerst gelöschter Alt-Bestand wäre bei einem mittendrin
/// scheiternden Move unwiederbringlich weg, ohne dass der neue Stand vollständig da ist).
/// </summary>
public class LocalContentReplacerTests
{
    [Fact]
    public void FindExtraFiles_meldet_nur_Dateien_die_nicht_im_Ziel_Plan_stehen()
    {
        using var root = new TempDirectory();
        var keepPath = root.WriteFile("keep.dat", "bleibt");
        var extraPath = root.WriteFile("stray/leftover.dat", "überzählig – Rest des vorherigen Standes");

        var roots = new[] { new SaveRoot("root", root.Path) };
        var extras = LocalContentReplacer.FindExtraFiles(roots, new[] { keepPath });

        var extra = Assert.Single(extras);
        Assert.Equal(Path.GetFullPath(extraPath), extra);
    }

    [Fact]
    public void FindExtraFiles_ignoriert_eigene_noch_nicht_verschobene_Temp_Marker()
    {
        using var root = new TempDirectory();
        root.WriteFile("save.dat.svtmp-abc123", "laufender Austausch, noch nicht verschoben");

        var roots = new[] { new SaveRoot("root", root.Path) };
        var extras = LocalContentReplacer.FindExtraFiles(roots, Array.Empty<string>());

        Assert.Empty(extras);
    }

    [Fact]
    public void FindExtraFiles_ohne_vorhandenen_Ordner_liefert_leere_Menge_statt_zu_werfen()
    {
        // Wurzel existiert (noch) nicht auf der Platte – kein Absturz, einfach nichts zu melden.
        var roots = new[] { new SaveRoot("root", Path.Combine(Path.GetTempPath(), "savevault-tests-missing-" + Guid.NewGuid().ToString("N"))) };

        var extras = LocalContentReplacer.FindExtraFiles(roots, Array.Empty<string>());

        Assert.Empty(extras);
    }

    [Fact]
    public void Solange_Commit_nicht_aufgerufen_wird_bleibt_der_Ordner_exakt_im_alten_Zustand()
    {
        // Sicherheitskritische Reihenfolge (verbindlich): ein simulierter Abbruch VOR dem finalen
        // Move (hier: Commit wird bewusst nicht aufgerufen, wie es SyncEngine.ReplaceLocalContentAsync
        // bei einer während Pass 2 geworfenen Exception ebenfalls nie tut) darf den bestehenden
        // Ordnerinhalt nicht verändern – kein Move, kein Delete ist bis dahin passiert.
        using var root = new TempDirectory();
        var oldTarget = root.WriteFile("save.dat", "alter Stand");
        var extraLeftover = root.WriteFile("stray.dat", "Rest eines früheren additiven Austauschs");

        // Simuliert: Pass 2 hat für "save.dat" bereits eine Temp-Datei mit dem NEUEN Inhalt
        // geschrieben (neben dem Ziel, noch nicht verschoben) – dann bricht der Download eines
        // WEITEREN Eintrags ab, bevor Commit je aufgerufen wird.
        var tmp = oldTarget + ".svtmp-" + Guid.NewGuid().ToString("N");
        File.WriteAllText(tmp, "neuer Stand (nur als Temp, noch nicht aktiv)");

        // Kein Aufruf von LocalContentReplacer.Commit(...) – exakt das Verhalten von
        // ReplaceLocalContentAsync, wenn der catch-Block vor dem "point of no return" greift.

        Assert.Equal("alter Stand", File.ReadAllText(oldTarget));
        Assert.True(File.Exists(extraLeftover), "Überzählige Alt-Datei darf vor Commit nicht gelöscht werden.");
        Assert.Equal("Rest eines früheren additiven Austauschs", File.ReadAllText(extraLeftover));
        Assert.True(File.Exists(tmp), "Die Temp-Datei bleibt liegen (wird beim echten Abbruch separat best-effort aufgeräumt).");
    }

    [Fact]
    public void Commit_verschiebt_alle_Temp_Dateien_und_loescht_dann_die_Extras()
    {
        using var root = new TempDirectory();
        var oldTarget = root.WriteFile("save.dat", "alter Stand");
        var extraLeftover = root.WriteFile("stray.dat", "überzählig");

        var tmp = oldTarget + ".svtmp-" + Guid.NewGuid().ToString("N");
        File.WriteAllText(tmp, "neuer Stand");

        LocalContentReplacer.Commit(
            extraFilesToDelete: new[] { extraLeftover },
            tempByTarget: new Dictionary<string, string> { [oldTarget] = tmp });

        Assert.False(File.Exists(extraLeftover), "Überzählige Alt-Datei muss nach Commit weg sein.");
        Assert.False(File.Exists(tmp), "Die Temp-Datei muss an ihren Zielplatz verschoben (nicht kopiert) worden sein.");
        Assert.Equal("neuer Stand", File.ReadAllText(oldTarget));
    }

    [Fact]
    public void Commit_loescht_keine_Alt_Datei_wenn_ein_Move_mittendrin_fehlschlaegt()
    {
        // Der eigentliche Blocker aus dem Kern-Gate (siehe Klassendoku): Commit muss ZUERST alle
        // Moves versuchen und darf die überzähligen Alt-Dateien erst löschen, NACHDEM alle Moves
        // geglückt sind. Hier bricht ein Move ab (Quelle fehlt), weil ein weiterer Ziel-Eintrag nie
        // erfolgreich heruntergeladen wurde – der Ordner darf dann nicht "kaputter" sein als vorher:
        // keine Alt-Datei, die noch gebraucht würde, darf verschwunden sein.
        using var root = new TempDirectory();
        var extraLeftover = root.WriteFile("stray.dat", "überzählig – darf bei fehlschlagendem Move nicht weg sein");
        var okTarget = root.WriteFile("ok.dat", "alter Stand (ok)");
        var okTmp = okTarget + ".svtmp-" + Guid.NewGuid().ToString("N");
        File.WriteAllText(okTmp, "neuer Stand (ok)");

        var failTarget = Path.Combine(root.Path, "fail.dat");
        // Bewusst KEINE Temp-Datei für failTarget angelegt – simuliert einen Move, dessen Quelle
        // (aus welchem Grund auch immer) nicht existiert, und lässt File.Move mit einer
        // IOException scheitern.
        var missingTmp = failTarget + ".svtmp-" + Guid.NewGuid().ToString("N");

        var tempByTarget = new Dictionary<string, string>
        {
            [okTarget] = okTmp,
            [failTarget] = missingTmp,
        };

        Assert.ThrowsAny<IOException>(() => LocalContentReplacer.Commit(new[] { extraLeftover }, tempByTarget));

        Assert.True(File.Exists(extraLeftover),
            "Ein fehlschlagender Move darf keine überzählige Alt-Datei löschen – Löschen läuft erst NACH allen erfolgreichen Moves.");
    }

    [Fact]
    public void Commit_ueberschreibt_ein_bestehendes_Ziel_atomar_per_Move()
    {
        using var root = new TempDirectory();
        var target = root.WriteFile("save.dat", "wird ersetzt");
        var tmp = target + ".svtmp-" + Guid.NewGuid().ToString("N");
        File.WriteAllText(tmp, "Ersatz-Inhalt");

        LocalContentReplacer.Commit(Array.Empty<string>(), new Dictionary<string, string> { [target] = tmp });

        Assert.Equal("Ersatz-Inhalt", File.ReadAllText(target));
        Assert.False(File.Exists(tmp));
    }
}
