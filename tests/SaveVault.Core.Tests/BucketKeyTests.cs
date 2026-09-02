using SaveVault.Core.Api;
using SaveVault.Core.Models;
using SaveVault.Core.Storage;

namespace SaveVault.Core.Tests;

/// <summary>
/// Tests der Bucket-Scope-Ableitung (<see cref="BucketKey"/>) – das Kern-Primitiv des Umbaus
/// auf geräte-eigene Buckets. Private/geteilte/Legacy-Schlüssel müssen sich sauber trennen,
/// eindeutig auf verschiedene Ablage-Ordner abbilden, verlustfrei auf den Originalschlüssel
/// zurückführen und den echten Anzeigenamen bewahren.
/// </summary>
public class BucketKeyTests
{
    private static GameKey Sample() => new("elden ring", "Elden Ring", "steam", "1245620");

    // --- Resolve -------------------------------------------------------------

    [Fact]
    public void Resolve_Legacy_laesst_den_Schluessel_unveraendert()
    {
        var game = Sample();

        var legacy = BucketKey.Resolve(game, BucketScope.Legacy, ownerDeviceId: null);

        Assert.Equal(game.Value, legacy.Value);
        Assert.Equal("Elden Ring", legacy.DisplayName);
    }

    [Fact]
    public void Resolve_Private_praefixt_mit_Owner_und_behaelt_Anzeigenamen()
    {
        var priv = BucketKey.Resolve(Sample(), BucketScope.Private, "device-abc");

        Assert.Equal("dev|device-abc|elden ring", priv.Value);
        Assert.Equal("Elden Ring", priv.DisplayName);
        Assert.Equal("steam", priv.Store);
        Assert.Equal("1245620", priv.StoreId);
        Assert.Equal(BucketScope.Private, BucketKey.ScopeOf(priv.Value));
    }

    [Fact]
    public void Resolve_Shared_praefixt_und_behaelt_Anzeigenamen()
    {
        var shared = BucketKey.Resolve(Sample(), BucketScope.Shared, ownerDeviceId: null);

        Assert.Equal("shared|elden ring", shared.Value);
        Assert.Equal("Elden Ring", shared.DisplayName);
        Assert.Equal(BucketScope.Shared, BucketKey.ScopeOf(shared.Value));
    }

    [Fact]
    public void Resolve_Private_ohne_Owner_wirft()
    {
        Assert.Throws<ArgumentException>(
            () => BucketKey.Resolve(Sample(), BucketScope.Private, ownerDeviceId: " "));
    }

    // --- Isolation / Ablage --------------------------------------------------

    [Fact]
    public void Verschiedene_Owner_ergeben_verschiedene_Buckets_und_Ordner()
    {
        var a = BucketKey.Resolve(Sample(), BucketScope.Private, "device-a");
        var b = BucketKey.Resolve(Sample(), BucketScope.Private, "device-b");

        Assert.NotEqual(a.Value, b.Value);
        Assert.NotEqual(PathSanitizer.SafeGameFolder(a), PathSanitizer.SafeGameFolder(b));
    }

    [Fact]
    public void Privat_geteilt_und_legacy_landen_in_verschiedenen_Ordnern()
    {
        var game = Sample();
        var legacy = PathSanitizer.SafeGameFolder(BucketKey.Resolve(game, BucketScope.Legacy, null));
        var priv = PathSanitizer.SafeGameFolder(BucketKey.Resolve(game, BucketScope.Private, "d1"));
        var shared = PathSanitizer.SafeGameFolder(BucketKey.Resolve(game, BucketScope.Shared, null));

        Assert.Equal(3, new HashSet<string> { legacy, priv, shared }.Count);
    }

    // --- Original (Rückführung für Client/Befehle) ---------------------------

    [Fact]
    public void Original_fuehrt_privat_und_geteilt_auf_den_Ausgangsschluessel_zurueck()
    {
        var game = Sample();
        var priv = BucketKey.Resolve(game, BucketScope.Private, "device-abc");
        var shared = BucketKey.Resolve(game, BucketScope.Shared, null);

        Assert.Equal(game.Value, BucketKey.Original(priv).Value);
        Assert.Equal(game.Value, BucketKey.Original(shared).Value);
        Assert.Equal("Elden Ring", BucketKey.Original(priv).DisplayName);
    }

    [Fact]
    public void Original_laesst_einen_Legacy_Schluessel_unveraendert()
    {
        var game = Sample();
        Assert.Equal(game.Value, BucketKey.Original(game).Value);
    }

    [Fact]
    public void Original_trennt_am_letzten_Pipe_auch_bei_Owner_mit_Pipe()
    {
        // Der value-Anteil enthält nie ein '|'; selbst eine (theoretische) Owner-ID mit '|' darf die
        // Rückführung nicht verfälschen – getrennt wird am LETZTEN '|'.
        var bucket = new GameKey("dev|weird|owner|elden ring", "Elden Ring");
        Assert.Equal("elden ring", BucketKey.Original(bucket).Value);
    }

    // --- Keine Fehlklassifikation echter Schlüssel ---------------------------

    [Theory]
    [InlineData("steam:1245620")]   // beginnt mit 's', aber nicht mit "shared|"
    [InlineData("proton game")]     // beginnt mit 'p', aber nicht mit "dev|"
    [InlineData("shared space")]    // Wort „shared" + Leerzeichen, kein '|'
    public void ScopeOf_erkennt_normale_Schluessel_als_Legacy(string value)
    {
        Assert.Equal(BucketScope.Legacy, BucketKey.ScopeOf(value));
    }

    // --- Wire-Format ---------------------------------------------------------

    [Theory]
    [InlineData(BucketScope.Private, "private")]
    [InlineData(BucketScope.Shared, "shared")]
    [InlineData(BucketScope.Legacy, "legacy")]
    public void Wire_Roundtrip(BucketScope scope, string wire)
    {
        Assert.Equal(wire, BucketKey.ToWire(scope));
        Assert.Equal(scope, BucketKey.FromWire(wire, BucketScope.Private));
    }

    [Fact]
    public void FromWire_leer_liefert_den_Fallback_und_unbekannt_wirft()
    {
        Assert.Equal(BucketScope.Legacy, BucketKey.FromWire(null, BucketScope.Legacy));
        Assert.Equal(BucketScope.Private, BucketKey.FromWire("  ", BucketScope.Private));
        Assert.Throws<ArgumentException>(() => BucketKey.FromWire("bogus", BucketScope.Private));
    }
}
