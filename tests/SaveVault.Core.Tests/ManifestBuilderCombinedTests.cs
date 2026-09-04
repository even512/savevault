using SaveVault.Core.Hashing;
using SaveVault.Core.Storage;

namespace SaveVault.Core.Tests;

/// <summary>
/// Tests des kombinierten Manifest-Baus über mehrere Save-Wurzeln
/// (<see cref="ManifestBuilder.BuildCombined"/>): eine Wurzel bleibt bit-identisch zum
/// Einzel-<see cref="ManifestBuilder.Build"/> (kein Präfix, kein Reseed); mehrere Wurzeln bekommen
/// je Datei den Root-Key als Präfix, sodass jede Datei später ihrer Wurzel zugeordnet werden kann.
/// </summary>
public class ManifestBuilderCombinedTests
{
    [Fact]
    public void Eine_Wurzel_ist_bit_identisch_zum_Einzel_Build()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("Config/Windows/Game.ini", "a");
        dir.WriteFile("SaveGames/slot1.sav", "b");

        var builder = new ManifestBuilder();
        var single = builder.Build(dir.Path);
        var combined = builder.BuildCombined(new[] { new SaveRoot("AppData/Local/Game/Saved", dir.Path) });

        Assert.Equal(single.ManifestHash, combined.ManifestHash);
        Assert.Equal(
            single.Entries.Select(e => e.RelativePath).OrderBy(x => x),
            combined.Entries.Select(e => e.RelativePath).OrderBy(x => x));
        // Kein Präfix bei Einzel-Wurzel.
        Assert.Contains(combined.Entries, e => e.RelativePath == "SaveGames/slot1.sav");
    }

    [Fact]
    public void Mehrere_Wurzeln_praefixieren_je_Datei_mit_dem_Root_Key()
    {
        using var a = new TempDirectory();
        using var b = new TempDirectory();
        a.WriteFile("cfg.ini", "x");
        b.WriteFile("win64_save/slot0", "y");

        var builder = new ManifestBuilder();
        var manifest = builder.BuildCombined(new[]
        {
            new SaveRoot("SteamCommon/C/Street Fighter 6", a.Path),
            new SaveRoot("Steam/userdata/56296790/1364780/remote", b.Path),
        });

        var paths = manifest.Entries.Select(e => e.RelativePath).OrderBy(x => x).ToArray();
        Assert.Equal(new[]
        {
            "Steam/userdata/56296790/1364780/remote/win64_save/slot0",
            "SteamCommon/C/Street Fighter 6/cfg.ini",
        }, paths);

        // Jeder präfixierte Pfad lässt sich wieder der richtigen Wurzel zuordnen.
        var roots = new[]
        {
            new SaveRoot("SteamCommon/C/Street Fighter 6", a.Path),
            new SaveRoot("Steam/userdata/56296790/1364780/remote", b.Path),
        };
        foreach (var e in manifest.Entries)
            Assert.True(SaveRootLayout.TryResolve(roots, e.RelativePath, out _, out _));
    }

    [Fact]
    public void Leeres_Rootset_ergibt_leeres_Manifest()
    {
        var manifest = new ManifestBuilder().BuildCombined(Array.Empty<SaveRoot>());
        Assert.Empty(manifest.Entries);
    }
}
