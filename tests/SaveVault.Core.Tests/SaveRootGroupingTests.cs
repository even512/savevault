using SaveVault.Core.Storage;

namespace SaveVault.Core.Tests;

/// <summary>
/// Tests der Mehr-Ordner-Gruppierung (<see cref="SaveRootGrouping"/>) – des Kerns der
/// „Mehr-Ordner-Erkennung". Geprüft wird an realen Pfadmustern aus Tims ludusavi-Ausgabe:
/// Ein Spiel mit einem klaren Ordner ergibt <b>genau eine</b> Wurzel (bit-identisch zum alten
/// Verhalten, kein Reseed), ein Spiel mit über Container/Systemwurzeln gestreuten Saves ergibt
/// <b>mehrere enge</b> Wurzeln, und keine Wurzel bleibt ein Container oder eine zu breite
/// Ahnen-Wurzel. Deterministisch, ohne IO. Pfade im Windows-Format (die Tests laufen auf Windows).
/// </summary>
public class SaveRootGroupingTests
{
    // --- Einfach-Root: unverändertes Verhalten -------------------------------

    [Fact]
    public void Ein_klarer_Ordner_ergibt_genau_eine_Wurzel()
    {
        var files = new[]
        {
            @"C:\Users\tim\Saved Games\CD Projekt Red\Cyberpunk 2077\save1\sav.dat",
            @"C:\Users\tim\Saved Games\CD Projekt Red\Cyberpunk 2077\save2\sav.dat",
            @"C:\Users\tim\Saved Games\CD Projekt Red\Cyberpunk 2077\settings.json",
        };

        var result = SaveRootGrouping.Group(files);

        Assert.True(result.FullyResolved);
        Assert.Equal(new[] { @"C:\Users\tim\Saved Games\CD Projekt Red\Cyberpunk 2077" }, result.Roots);
    }

    [Fact]
    public void Streuung_unter_EINEM_engen_Ordner_wird_NICHT_uebersplittet()
    {
        // Config- und SaveGames-Unterbäume unter demselben engen Spielordner → eine Wurzel.
        var files = new[]
        {
            @"C:\Users\tim\AppData\Local\PioneerGame\Saved\Config\Windows\Game.ini",
            @"C:\Users\tim\AppData\Local\PioneerGame\Saved\SaveGames\slot1.sav",
        };

        var result = SaveRootGrouping.Group(files);

        Assert.Equal(new[] { @"C:\Users\tim\AppData\Local\PioneerGame\Saved" }, result.Roots);
    }

    // --- Mehr-Root: Abstieg durch Container / Systemwurzeln ------------------

    [Fact]
    public void Steam_Container_wird_durchstiegen_bis_zum_Spielordner()
    {
        // Street-Fighter-6-Muster: steamapps\common\<Spiel> + userdata\<id>\<appid>\remote.
        // Gemeinsamer Nenner wäre die Steam-Installationswurzel (Container) → aufsplitten.
        var files = new[]
        {
            @"C:\Program Files (x86)\Steam\steamapps\common\Street Fighter 6\cfg.ini",
            @"C:\Program Files (x86)\Steam\userdata\56296790\1364780\remote\win64_save\slot0",
            @"C:\Program Files (x86)\Steam\userdata\56296790\1364780\remote\win64_save\slot1",
        };

        var result = SaveRootGrouping.Group(files);

        Assert.True(result.FullyResolved);
        Assert.Equal(new[]
        {
            @"C:\Program Files (x86)\Steam\steamapps\common\Street Fighter 6",
            @"C:\Program Files (x86)\Steam\userdata\56296790\1364780\remote\win64_save",
        }, result.Roots);
    }

    [Fact]
    public void Zwei_AppData_Local_Ordner_werden_getrennt()
    {
        // Marvel-Rivals-Muster: zwei getrennte AppData\Local-Ordner desselben Spiels.
        // Gemeinsamer Nenner AppData\Local ist zu breit → in beide engen Ordner splitten.
        var files = new[]
        {
            @"C:\Users\tim\AppData\Local\MarvelRivals_Launcher\config.bin",
            @"C:\Users\tim\AppData\Local\Marvel\Saved\Config\Windows\Game.ini",
            @"C:\Users\tim\AppData\Local\Marvel\Saved\Config\Windows\Input.ini",
        };

        var result = SaveRootGrouping.Group(files);

        Assert.True(result.FullyResolved);
        // Roots sind OrdinalIgnoreCase sortiert ('R' < '\'): MarvelRivals_Launcher zuerst.
        Assert.Equal(new[]
        {
            @"C:\Users\tim\AppData\Local\MarvelRivals_Launcher",
            @"C:\Users\tim\AppData\Local\Marvel\Saved\Config\Windows",
        }, result.Roots);
    }

    [Fact]
    public void Verschiedene_Laufwerke_werden_getrennt()
    {
        // Resident-Evil-Village-Muster: Installation auf C: und D: + Steam-Cloud.
        var files = new[]
        {
            @"C:\Program Files (x86)\Steam\steamapps\common\Resident Evil Village\re8.ini",
            @"C:\Program Files (x86)\Steam\userdata\56296790\1196590\remote\win64_save\data0",
            @"D:\SteamLibrary\steamapps\common\Resident Evil Village\re8.ini",
        };

        var result = SaveRootGrouping.Group(files);

        Assert.True(result.FullyResolved);
        Assert.Equal(3, result.Roots.Count);
        Assert.Contains(@"C:\Program Files (x86)\Steam\steamapps\common\Resident Evil Village", result.Roots);
        Assert.Contains(@"C:\Program Files (x86)\Steam\userdata\56296790\1196590\remote\win64_save", result.Roots);
        Assert.Contains(@"D:\SteamLibrary\steamapps\common\Resident Evil Village", result.Roots);
    }

    [Fact]
    public void Ubisoft_savegames_Guid_Container_wird_durchstiegen()
    {
        // Assassin's-Creed-Muster: Ubisoft savegames\<accountGuid>\<gameId> + Documents.
        var files = new[]
        {
            @"C:\Program Files (x86)\Ubisoft\Ubisoft Game Launcher\savegames\db47b069-c627-4678-b277-316c8a9cf11d\6100\1.save",
            @"C:\Program Files (x86)\Ubisoft\Ubisoft Game Launcher\savegames\db47b069-c627-4678-b277-316c8a9cf11d\6100\2.save",
            @"C:\Users\tim\Documents\Assassin's Creed Mirage\settings.dat",
        };

        var result = SaveRootGrouping.Group(files);

        Assert.True(result.FullyResolved);
        Assert.Equal(new[]
        {
            @"C:\Program Files (x86)\Ubisoft\Ubisoft Game Launcher\savegames\db47b069-c627-4678-b277-316c8a9cf11d\6100",
            @"C:\Users\tim\Documents\Assassin's Creed Mirage",
        }, result.Roots);
    }

    // --- Randfälle -----------------------------------------------------------

    [Fact]
    public void Leere_Eingabe_ergibt_leeres_Ergebnis()
    {
        var result = SaveRootGrouping.Group(Array.Empty<string>());
        Assert.Empty(result.Roots);
        Assert.True(result.FullyResolved);
    }

    [Fact]
    public void Save_direkt_in_der_Systemwurzel_bleibt_ungeloest()
    {
        // Eine Save-Datei unmittelbar im Benutzerprofil kann nicht enger gefasst werden.
        var files = new[] { @"C:\Users\tim\einzeldatei.sav" };

        var result = SaveRootGrouping.Group(files);

        Assert.Empty(result.Roots);
        Assert.False(result.FullyResolved);
    }

    [Fact]
    public void Ungueltige_Pfade_werden_uebersprungen()
    {
        var files = new[] { "   ", "", @"C:\Users\tim\Documents\My Game\slot.sav" };

        var result = SaveRootGrouping.Group(files);

        Assert.Equal(new[] { @"C:\Users\tim\Documents\My Game" }, result.Roots);
    }
}
