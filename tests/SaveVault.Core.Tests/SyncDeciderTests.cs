using System.Collections.Generic;
using SaveVault.Core.Models;
using SaveVault.Core.Sync;

namespace SaveVault.Core.Tests;

/// <summary>
/// Tests der reinen Sync-Entscheidungslogik (<see cref="SyncDecider"/>): die vier
/// Fälle aus der Spec, ihre Ränder, <see cref="SyncDecider.LocalChanged"/> und die
/// Konsistenz von <see cref="SyncDecider.IsConflict"/> zu <see cref="SyncDecider.Decide"/>.
/// Alle Manifeste werden über den echten Builder <see cref="FileManifest.Create"/>
/// erzeugt (keine künstlich gesetzten Felder), damit die Manifest-Hashes echt sind.
/// </summary>
public class SyncDeciderTests
{
    private static readonly GameKey Game = GameKey.FromName("Test Game");

    /// <summary>Ein nicht-leeres Manifest mit einem bestimmten Datei-Hash.</summary>
    private static FileManifest ManifestWith(string relativePath, string sha, long size = 10)
        => FileManifest.Create(new[]
        {
            new FileEntry(relativePath, sha, size, DateTime.UnixEpoch),
        });

    private static SyncState StateWith(long baseRevision, FileManifest? baseManifest)
        => new(Game, baseRevision, baseManifest);

    // --- Die vier Fälle -----------------------------------------------------

    [Fact]
    public void Decide_Upload_wenn_lokal_geaendert_und_Server_auf_base()
    {
        var baseManifest = ManifestWith("save.dat", "aaaa");
        var local = ManifestWith("save.dat", "bbbb"); // anderer Hash → geändert
        var state = StateWith(baseRevision: 5, baseManifest);

        var decision = SyncDecider.Decide(local, state, serverRevision: 5);

        Assert.Equal(SyncAction.Upload, decision.Action);
    }

    [Fact]
    public void Decide_Upload_beim_Erstupload_ohne_baseManifest_und_serverRevision_null()
    {
        var local = ManifestWith("save.dat", "bbbb");
        var state = SyncState.Initial(Game); // BaseManifest null, BaseRevision 0

        var decision = SyncDecider.Decide(local, state, serverRevision: 0);

        Assert.Equal(SyncAction.Upload, decision.Action);
    }

    [Fact]
    public void Decide_Download_wenn_unveraendert_und_Server_neuer()
    {
        var shared = ManifestWith("save.dat", "aaaa");
        var local = ManifestWith("save.dat", "aaaa"); // gleicher Hash → unverändert
        var state = StateWith(baseRevision: 3, shared);

        var decision = SyncDecider.Decide(local, state, serverRevision: 7);

        Assert.Equal(SyncAction.Download, decision.Action);
    }

    [Fact]
    public void Decide_Conflict_wenn_lokal_geaendert_und_Server_neuer()
    {
        var baseManifest = ManifestWith("save.dat", "aaaa");
        var local = ManifestWith("save.dat", "bbbb"); // geändert
        var state = StateWith(baseRevision: 3, baseManifest);

        var decision = SyncDecider.Decide(local, state, serverRevision: 9);

        Assert.Equal(SyncAction.Conflict, decision.Action);
    }

    [Fact]
    public void Decide_NoOp_wenn_unveraendert_und_Server_auf_base()
    {
        var shared = ManifestWith("save.dat", "aaaa");
        var local = ManifestWith("save.dat", "aaaa");
        var state = StateWith(baseRevision: 4, shared);

        var decision = SyncDecider.Decide(local, state, serverRevision: 4);

        Assert.Equal(SyncAction.NoOp, decision.Action);
    }

    [Fact]
    public void Decide_NoOp_liefert_Grundtext()
    {
        var shared = ManifestWith("save.dat", "aaaa");
        var state = StateWith(baseRevision: 0, shared);

        var decision = SyncDecider.Decide(shared, state, serverRevision: 0);

        Assert.Equal(SyncAction.NoOp, decision.Action);
        Assert.False(string.IsNullOrWhiteSpace(decision.Reason));
    }

    // --- LocalChanged -------------------------------------------------------

    [Fact]
    public void LocalChanged_true_wenn_baseManifest_null_und_lokal_nicht_leer()
    {
        var local = ManifestWith("save.dat", "aaaa");

        Assert.True(SyncDecider.LocalChanged(local, baseManifest: null));
    }

    [Fact]
    public void LocalChanged_false_wenn_baseManifest_null_und_lokal_leer()
    {
        Assert.False(SyncDecider.LocalChanged(FileManifest.Empty, baseManifest: null));
    }

    [Fact]
    public void LocalChanged_false_bei_gleichem_ManifestHash()
    {
        var a = ManifestWith("save.dat", "aaaa");
        var b = ManifestWith("save.dat", "aaaa");

        Assert.Equal(a.ManifestHash, b.ManifestHash); // Vorbedingung
        Assert.False(SyncDecider.LocalChanged(a, b));
    }

    [Fact]
    public void LocalChanged_true_bei_unterschiedlichem_ManifestHash()
    {
        var a = ManifestWith("save.dat", "aaaa");
        var b = ManifestWith("save.dat", "bbbb");

        Assert.NotEqual(a.ManifestHash, b.ManifestHash); // Vorbedingung
        Assert.True(SyncDecider.LocalChanged(a, b));
    }

    // --- IsConflict konsistent zu Decide ------------------------------------

    [Fact]
    public void IsConflict_true_genau_im_Conflict_Fall()
    {
        var baseManifest = ManifestWith("save.dat", "aaaa");
        var local = ManifestWith("save.dat", "bbbb");
        var state = StateWith(baseRevision: 3, baseManifest);

        Assert.True(SyncDecider.IsConflict(local, state, serverRevision: 9));
        Assert.Equal(SyncAction.Conflict, SyncDecider.Decide(local, state, 9).Action);
    }

    [Theory]
    [InlineData(true, 5, 5, SyncAction.Upload)]   // geändert, Server == base
    [InlineData(false, 3, 7, SyncAction.Download)] // unverändert, Server > base
    [InlineData(true, 3, 9, SyncAction.Conflict)] // geändert, Server > base
    [InlineData(false, 4, 4, SyncAction.NoOp)]    // unverändert, Server == base
    public void IsConflict_stimmt_mit_Decide_Conflict_ueberein(
        bool changed, long baseRevision, long serverRevision, SyncAction expected)
    {
        var baseManifest = ManifestWith("save.dat", "aaaa");
        var local = changed ? ManifestWith("save.dat", "bbbb") : ManifestWith("save.dat", "aaaa");
        var state = StateWith(baseRevision, baseManifest);

        var decision = SyncDecider.Decide(local, state, serverRevision);
        var isConflict = SyncDecider.IsConflict(local, state, serverRevision);

        Assert.Equal(expected, decision.Action);
        Assert.Equal(decision.Action == SyncAction.Conflict, isConflict);
    }

    // --- Argument-Prüfungen -------------------------------------------------

    [Fact]
    public void Decide_wirft_bei_null_Manifest()
    {
        var state = SyncState.Initial(Game);
        Assert.Throws<ArgumentNullException>(() => SyncDecider.Decide(null!, state, 0));
    }

    [Fact]
    public void Decide_wirft_bei_null_State()
    {
        var local = ManifestWith("save.dat", "aaaa");
        Assert.Throws<ArgumentNullException>(() => SyncDecider.Decide(local, null!, 0));
    }
}
