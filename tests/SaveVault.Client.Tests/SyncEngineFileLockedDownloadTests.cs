using System.IO;
using System.Threading.Tasks;
using SaveVault.Client.Services;
using SaveVault.Core.Api;
using SaveVault.Core.Hashing;
using SaveVault.Core.Models;
using SaveVault.Core.Storage;
using SaveVault.Core.Sync;

namespace SaveVault.Client.Tests;

/// <summary>
/// Regressionstest für <c>specs/savevault-change-sync-anzeige-fixes.md</c>, Nachtrag 2+3, Fix 2:
/// Ist die lokale Zieldatei eines Downloads gerade exklusiv gesperrt (z. B. vom laufenden Spiel),
/// darf <see cref="SyncEngine.RunCycleAsync"/> das nicht als roh-unklassifizierten Fehler ins
/// Diagnose-Log schreiben und nicht als harten "Sync-Fehler"-Status an <see cref="AgentState"/>
/// melden - beides würde bei jedem folgenden Zyklus erneut auftreten, bis das Spiel geschlossen
/// wird. Stattdessen: eine klar als "Datei gesperrt" erkennbare Diagnose-Zeile und ein
/// unterscheidbarer Warte-Status.
/// </summary>
public class SyncEngineFileLockedDownloadTests
{
    [Fact]
    public async Task RunCycleAsync_gesperrte_Zieldatei_beim_Download_erzeugt_Datei_gesperrt_Outcome_statt_roher_Exception()
    {
        using var appRoot = new TempDirectory();
        using var saveDir = new TempDirectory();

        var game = GameKey.FromName("Testspiel");
        var roots = new[] { new SaveRoot("primary", saveDir.Path) };

        // Lokaler Stand: "save.dat" existiert bereits und ist identisch zur Basis (kein
        // "localChanged") - so entscheidet der SyncDecider eindeutig auf "Download".
        var localPath = saveDir.WriteFile("save.dat", "alter, lokaler Stand");
        var manifestBuilder = new ManifestBuilder();
        var baseManifest = manifestBuilder.Build(saveDir.Path);

        var paths = new AppPaths(appRoot.Path);
        var stateStore = new SyncStateStore(paths);
        stateStore.Save(new SyncState(game, BaseRevision: 1, BaseManifest: baseManifest));

        var api = new FakeSaveVaultApi
        {
            Head = new RevisionHead(game, 2),
            Revision = new RevisionDownload(
                Number: 2,
                Game: game,
                DeviceId: "anderes-geraet",
                TimestampUtc: DateTime.UtcNow,
                Manifest: FileManifest.Create(new[]
                {
                    new FileEntry("save.dat", FileHasher.HashBytes("neuer Server-Stand"u8), 19, DateTime.UtcNow),
                })),
        };

        var state = new AgentState();
        var diagnostics = new SyncDiagnosticsLog(paths);

        var engine = new SyncEngine(
            api,
            stateStore,
            state,
            deviceInfo: () => new DeviceInfo("dev1", "Testgeraet", "windows", "1.0.0", DateTime.UtcNow),
            manifestBuilder: manifestBuilder,
            diagnosticsLog: diagnostics,
            // Kurze Backoffs, damit der Test nicht auf die echten (Produktions-)Wartezeiten warten muss.
            fileWriteRetryDelays: new[] { TimeSpan.FromMilliseconds(5), TimeSpan.FromMilliseconds(5) });

        // "Spiel" haelt die lokale Zieldatei waehrend des gesamten Downloadversuchs exklusiv offen.
        using var locked = new FileStream(localPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var result = await engine.RunCycleAsync(game, roots);

        // Kein Absturz, kein harter Fehler-Status - ein unterscheidbarer Warte-Status.
        Assert.Equal(SyncStatus.Pending, result.Status);
        Assert.DoesNotContain("Exception", result.Message, StringComparison.Ordinal);

        var logContent = File.ReadAllText(paths.SyncLogFile);
        Assert.Contains("Datei gesperrt", logContent);
    }
}
