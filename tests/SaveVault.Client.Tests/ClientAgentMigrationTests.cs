using System.IO;
using System.Threading.Tasks;
using SaveVault.Client.Services;
using SaveVault.Core.Models;
using SaveVault.Core.Sync;

namespace SaveVault.Client.Tests;

/// <summary>
/// Regressionstest für den Migrations-Bug (CHECKPOINT.md, „Unabhängiger Vorfall während
/// der Diagnose“, 2026-09-24): Die einmalige Migration auf geräte-eigene Buckets
/// (<see cref="ClientAgent.StartAsync"/>) war am Config-Flag <c>PerDeviceBucketsMigrated</c>
/// gehängt — eine verlorene/zerstörte <c>config.json</c> hat dadurch den destruktiven
/// <c>ResetAllState()</c> erneut ausgelöst (54 falsche „Konflikt“-Meldungen). Die Migration
/// darf jetzt nur laufen, wenn weder die persistente Marker-Datei (unabhängig von
/// <c>config.json</c>) noch das Legacy-Config-Flag vorliegen: Nach einer abgeschlossenen
/// Migration darf ein verlorener Config niemals mehr States löschen, während ein ECHTER
/// Erstlauf (Alt-States vorhanden, keine Anzeichen) die Migration weiterhin ausführt — und
/// ein späterer Neustart sie idempotent überspringt.
/// </summary>
public class ClientAgentMigrationTests
{
    [Fact]
    public async Task StartAsync_verlorene_config_nach_abgeschlossener_Migration_loescht_keine_States()
    {
        using var appRoot = new TempDirectory();
        var paths = new AppPaths(appRoot.Path);

        // Simuliere ein bereits migriertes Gerät: Sync-State + Konflikt-Marke vorhanden,
        // Migrations-Marker vorhanden, config.json FEHLT (genauer Vorfall 2026-09-24).
        var stateStore = new SyncStateStore(paths);
        var game = GameKey.FromName("Testspiel");
        stateStore.Save(new SyncState(game, BaseRevision: 3, BaseManifest: null));
        stateStore.SaveConflictHash(game, "alter-konflikt-hash");
        File.WriteAllText(paths.PerDeviceBucketsMigrationMarker, "per-device-buckets: migrated\n");

        var agent = new ClientAgent(paths);
        await agent.StartAsync();

        // Der destruktive Reset darf NICHT feuern: State und Konflikt-Marke bleiben erhalten.
        Assert.Equal(3, stateStore.Load(game).BaseRevision);
        Assert.Equal("alter-konflikt-hash", stateStore.LoadConflictHash(game));
        await agent.StopAsync();
    }

    [Fact]
    public async Task StartAsync_erster_Lauf_mit_Alts_States_fuehrt_Migration_durch_und_setzt_Marker()
    {
        using var appRoot = new TempDirectory();
        var paths = new AppPaths(appRoot.Path);

        // Simuliere den echten ersten Start der neuen Version: Alt-States vorhanden,
        // KEIN Marker (und keine config.json mit Flag) → Migration muss laufen.
        var stateStore = new SyncStateStore(paths);
        var game = GameKey.FromName("Testspiel");
        stateStore.Save(new SyncState(game, BaseRevision: 7, BaseManifest: null));
        stateStore.SaveConflictHash(game, "alter-konflikt-hash");

        var agent = new ClientAgent(paths);
        await agent.StartAsync();

        // Migration lief: States und Konflikt-Marken verworfen, Marker persistiert.
        Assert.Equal(0, stateStore.Load(game).BaseRevision);
        Assert.Null(stateStore.LoadConflictHash(game));
        Assert.True(File.Exists(paths.PerDeviceBucketsMigrationMarker));
        await agent.StopAsync();
    }

    [Fact]
    public async Task StartAsync_erster_Lauf_neuer_Version_mit_Legacy_Flag_loescht_keine_States_und_adoptiert_Marker()
    {
        using var appRoot = new TempDirectory();
        var paths = new AppPaths(appRoot.Path);

        // Bestehendes Gerät: Migration lief bereits unter der alten Version
        // (config.json mit PerDeviceBucketsMigrated=true), aber noch kein Marker.
        var stateStore = new SyncStateStore(paths);
        var game = GameKey.FromName("Testspiel");
        stateStore.Save(new SyncState(game, BaseRevision: 5, BaseManifest: null));
        new ClientConfigStore(paths).Save(new ClientConfig { PerDeviceBucketsMigrated = true });

        var agent = new ClientAgent(paths);
        await agent.StartAsync();

        // KEINE Re-Migration (sonst wäre der State weg) — und der Marker wird adoptiert,
        // damit ein späterer config.json-Verlust sicher ist.
        Assert.Equal(5, stateStore.Load(game).BaseRevision);
        Assert.True(File.Exists(paths.PerDeviceBucketsMigrationMarker));
        await agent.StopAsync();
    }

    [Fact]
    public async Task StartAsync_zweiter_Lauf_nach_Migration_veraendert_nichts()
    {
        using var appRoot = new TempDirectory();
        var paths = new AppPaths(appRoot.Path);
        var stateStore = new SyncStateStore(paths);
        var game = GameKey.FromName("Testspiel");

        // Erster Start: Migration läuft (kein State, kein Marker), Marker wird gesetzt.
        var first = new ClientAgent(paths);
        await first.StartAsync();
        await first.StopAsync();

        // Danach normaler Sync-Fortschritt (State entsteht neu).
        stateStore.Save(new SyncState(game, BaseRevision: 4, BaseManifest: null));

        // Neustart: die Migration darf NICHT erneut feuern — State bleibt erhalten.
        var second = new ClientAgent(paths);
        await second.StartAsync();
        Assert.Equal(4, stateStore.Load(game).BaseRevision);
        await second.StopAsync();
    }
}