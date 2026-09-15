using System.IO;
using System.Linq;
using SaveVault.Core.Hashing;
using SaveVault.Core.Sync;

namespace SaveVault.Core.Tests;

/// <summary>
/// Regressionstests für den Hauptfund aus <c>specs/savevault-change-sync-anzeige-fixes.md</c>,
/// Nachtrag 3: Eine Datei, die gerade nicht lesbar ist (z. B. weil ein laufendes Spiel sie
/// exklusiv offen hält), darf NICHT kommentarlos aus dem Manifest fallen, wenn für sie bereits
/// ein Stand aus einem vorherigen Scan bekannt ist – sonst sieht das für den SyncDecider wie
/// „Datei gelöscht" aus (Phantom-Upload/-Conflict, im schlimmsten Fall Datenverlust bei einem
/// späteren exakten Restore). Ohne jeden vorherigen Stand bleibt eine gesperrte Datei weiterhin
/// ausgelassen (nichts Sinnvolles zu bewahren).
///
/// <para>Die Sperre wird über einen echten, exklusiven <see cref="FileStream"/>
/// (<see cref="FileShare.None"/>) im selben Prozess simuliert – <see cref="FileHasher.HashFile"/>
/// nutzt <see cref="File.OpenRead(string)"/> (<see cref="FileShare.Read"/>), das dagegen mit
/// einer <see cref="IOException"/> scheitert, exakt wie bei einer echten Fremdsperre.</para>
/// </summary>
public class ManifestBuilderLockedFileTests
{
    private readonly ManifestBuilder _builder = new();

    [Fact]
    public void ScanRootInto_gesperrte_Datei_mit_vorherigem_Eintrag_behaelt_alten_Hash_und_loest_kein_localChanged_aus()
    {
        using var dir = new TempDirectory();
        var path = dir.WriteFile("save.dat", "Stand A");
        var previous = _builder.Build(dir.Path);

        // Datei aendert sich (Groesse+Schreibzeit weichen vom Vorfilter ab, HashFile wuerde also
        // ohne die Sperre neu gehasht), ist aber im Moment des naechsten Scans exklusiv gesperrt -
        // wie waehrend eines laufenden Spiel-Speichervorgangs.
        File.WriteAllText(path, "Stand A, mittendrin vom Spiel weitergeschrieben");
        using var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);

        var current = _builder.Build(dir.Path, previous);

        Assert.Equal(1, current.FileCount);
        var entry = Assert.Single(current.Entries);
        var previousEntry = Assert.Single(previous.Entries);
        Assert.Equal(previousEntry.Sha256, entry.Sha256);
        Assert.Equal(previousEntry.Size, entry.Size);
        Assert.Equal(previousEntry.LastWriteUtc, entry.LastWriteUtc);
        Assert.Equal(previous.ManifestHash, current.ManifestHash);

        // Der eigentliche Zweck des Fixes: der SyncDecider darf das NICHT als lokale Aenderung
        // werten (kein Phantom-Upload/-Conflict wegen einer bloss gesperrten Datei).
        Assert.False(SyncDecider.LocalChanged(current, previous));
    }

    [Fact]
    public void ScanRootInto_gesperrte_Datei_ohne_vorherigen_Eintrag_bleibt_ausgelassen()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("existing.dat", "vorhanden");
        var previous = _builder.Build(dir.Path); // enthaelt nur "existing.dat"

        var newPath = dir.WriteFile("new.dat", "brandneu, nie erfolgreich gescannt");
        using var locked = new FileStream(newPath, FileMode.Open, FileAccess.Read, FileShare.None);

        var current = _builder.Build(dir.Path, previous);

        var paths = current.Entries.Select(e => e.RelativePath).ToHashSet();
        Assert.Contains("existing.dat", paths);
        Assert.DoesNotContain("new.dat", paths);
        Assert.Equal(1, current.FileCount);
    }
}
