using SaveVault.Core.Storage;

namespace SaveVault.Core.Tests;

/// <summary>
/// Tests der Mehr-Root-Manifest-Konvention (<see cref="SaveRootLayout"/>): Präfix nur bei mehreren
/// Wurzeln (Einfach-Root bleibt unpräfixiert = kein Reseed), und die Rück-Abbildung eines
/// Manifest-Pfads auf die richtige lokale Wurzel inkl. korrekter Behandlung unbekannter Keys.
/// Reine String-Logik.
/// </summary>
public class SaveRootLayoutTests
{
    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    public void UsesPrefix_nur_bei_mehreren_Wurzeln(int count, bool expected)
        => Assert.Equal(expected, SaveRootLayout.UsesPrefix(count));

    [Fact]
    public void TryResolve_eine_Wurzel_nimmt_den_Pfad_unveraendert()
    {
        var roots = new[] { new SaveRoot("Documents/My Game", @"C:\Users\tim\Documents\My Game") };

        Assert.True(SaveRootLayout.TryResolve(roots, "slot1/save.dat", out var folder, out var sub));
        Assert.Equal(@"C:\Users\tim\Documents\My Game", folder);
        Assert.Equal("slot1/save.dat", sub);
    }

    [Fact]
    public void TryResolve_mehrere_Wurzeln_trifft_die_richtige_ueber_den_Key()
    {
        var roots = new[]
        {
            new SaveRoot("SteamCommon/C/Street Fighter 6", @"C:\Steam\steamapps\common\Street Fighter 6"),
            new SaveRoot("Steam/userdata/56296790/1364780/remote/win64_save", @"C:\Steam\userdata\56296790\1364780\remote\win64_save"),
        };

        Assert.True(SaveRootLayout.TryResolve(roots,
            "Steam/userdata/56296790/1364780/remote/win64_save/slot0", out var folder, out var sub));
        Assert.Equal(@"C:\Steam\userdata\56296790\1364780\remote\win64_save", folder);
        Assert.Equal("slot0", sub);

        Assert.True(SaveRootLayout.TryResolve(roots,
            "SteamCommon/C/Street Fighter 6/cfg.ini", out folder, out sub));
        Assert.Equal(@"C:\Steam\steamapps\common\Street Fighter 6", folder);
        Assert.Equal("cfg.ini", sub);
    }

    [Fact]
    public void TryResolve_unbekannter_Key_wird_abgelehnt()
    {
        var roots = new[]
        {
            new SaveRoot("SteamCommon/E/Spiel", @"E:\SteamLibrary\steamapps\common\Spiel"),
            new SaveRoot("Documents/Spiel", @"C:\Users\tim\Documents\Spiel"),
        };

        // Manifest kommt von einem Gerät, wo das Spiel auf C: installiert war → Key hier unbekannt.
        Assert.False(SaveRootLayout.TryResolve(roots, "SteamCommon/C/Spiel/cfg.ini", out _, out _));
    }

    [Fact]
    public void TryResolve_Praefix_ohne_Trenner_passt_nicht_faelschlich()
    {
        var roots = new[]
        {
            new SaveRoot("Foo", @"C:\a"),
            new SaveRoot("FooBar", @"C:\b"),
        };

        // "FooBar/x" darf NICHT zum Key "Foo" passen, sondern nur zu "FooBar".
        Assert.True(SaveRootLayout.TryResolve(roots, "FooBar/x", out var folder, out var sub));
        Assert.Equal(@"C:\b", folder);
        Assert.Equal("x", sub);
    }

    [Fact]
    public void TryResolve_leeres_Rootset_oder_leerer_Pfad_scheitert()
    {
        Assert.False(SaveRootLayout.TryResolve(Array.Empty<SaveRoot>(), "x", out _, out _));
        Assert.False(SaveRootLayout.TryResolve(new[] { new SaveRoot("K", @"C:\a") }, "", out _, out _));
    }
}
