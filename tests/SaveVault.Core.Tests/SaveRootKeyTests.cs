using SaveVault.Core.Storage;

namespace SaveVault.Core.Tests;

/// <summary>
/// Tests der Root-Key-Ableitung (<see cref="SaveRootKey"/>). Der Schlüssel abstrahiert den
/// maschinenabhängigen Präfix (Laufwerk, Profilpfad) über einen semantischen Anker, hält aber die
/// konto-stabilen Segmente (Steam-ID, Ubisoft-GUID) – so trifft ein Restore auf einem anderen Gerät
/// den richtigen Ordner. Installations-Orte (<c>steamapps\common</c>, Launcher-Bibliotheken) tragen
/// das Laufwerk, damit ein Spiel auf zwei Laufwerken nicht kollidiert. Alle Muster stammen aus Tims
/// echter ludusavi-Ausgabe. Deterministisch, rein lexikalisch (kein <see cref="System.Environment"/>).
/// </summary>
public class SaveRootKeyTests
{
    [Theory]
    // Steam userdata: Laufwerk/Install-Präfix fällt weg, ID + AppID bleiben (konto-stabil).
    [InlineData(@"C:\Program Files (x86)\Steam\userdata\56296790\1364780\remote\win64_save",
                "Steam/userdata/56296790/1364780/remote/win64_save")]
    // Ubisoft savegames: GUID + Spiel-ID bleiben.
    [InlineData(@"C:\Program Files (x86)\Ubisoft\Ubisoft Game Launcher\savegames\db47b069-c627-4678-b277-316c8a9cf11d\6100",
                "Ubisoft/savegames/db47b069-c627-4678-b277-316c8a9cf11d/6100")]
    // Profil-Anker: Präfix C:\Users\timse\ fällt weg.
    [InlineData(@"C:\Users\timse\Saved Games\CD Projekt Red\Cyberpunk 2077",
                "SavedGames/CD Projekt Red/Cyberpunk 2077")]
    [InlineData(@"C:\Users\timse\AppData\Local\CD Projekt Red\Cyberpunk 2077",
                "AppData/Local/CD Projekt Red/Cyberpunk 2077")]
    [InlineData(@"C:\Users\timse\AppData\Roaming\HelloGames\NMS\st_76561198016562518",
                "AppData/Roaming/HelloGames/NMS/st_76561198016562518")]
    [InlineData(@"C:\Users\timse\Documents\My Games\Outlaws",
                "Documents/My Games/Outlaws")]
    // Installations-Orte: Laufwerk bleibt im Key.
    [InlineData(@"C:\Program Files (x86)\Steam\steamapps\common\Street Fighter 6",
                "SteamCommon/C/Street Fighter 6")]
    [InlineData(@"D:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive\game\csgo\cfg",
                "SteamCommon/D/Counter-Strike Global Offensive/game/csgo/cfg")]
    [InlineData(@"C:\GOG Games\Being a DIK\game\saves",
                "GogGames/C/Being a DIK/game/saves")]
    public void Derive_bildet_den_erwarteten_Schluessel(string folder, string expected)
    {
        Assert.Equal(expected, SaveRootKey.Derive(folder));
    }

    [Fact]
    public void Steam_userdata_ist_unabhaengig_vom_Install_Laufwerk_geraeteuebergreifend_stabil()
    {
        // Gleiches Konto, Steam einmal auf C:, einmal auf D: → identischer Schlüssel.
        var a = SaveRootKey.Derive(@"C:\Program Files (x86)\Steam\userdata\56296790\730");
        var b = SaveRootKey.Derive(@"D:\SteamLibrary\Steam\userdata\56296790\730");
        Assert.Equal("Steam/userdata/56296790/730", a);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Profil_Anker_ist_unabhaengig_vom_Benutzernamen_geraeteuebergreifend_stabil()
    {
        var a = SaveRootKey.Derive(@"C:\Users\timse\Documents\My Games\Outlaws");
        var b = SaveRootKey.Derive(@"D:\Users\anders\Documents\My Games\Outlaws");
        Assert.Equal(a, b);
    }

    [Fact]
    public void Zwei_steamapps_Ordner_auf_verschiedenen_Laufwerken_kollidieren_NICHT()
    {
        // Realer Fall Resident Evil Village: identischer Unterpfad, aber C: und D:.
        var c = SaveRootKey.Derive(@"C:\Program Files (x86)\Steam\steamapps\common\Resident Evil Village BIOHAZARD VILLAGE");
        var d = SaveRootKey.Derive(@"D:\SteamLibrary\steamapps\common\Resident Evil Village BIOHAZARD VILLAGE");
        Assert.NotEqual(c, d);
        Assert.Equal("SteamCommon/C/Resident Evil Village BIOHAZARD VILLAGE", c);
        Assert.Equal("SteamCommon/D/Resident Evil Village BIOHAZARD VILLAGE", d);
    }

    [Fact]
    public void Unbekannter_Ort_faellt_auf_Laufwerk_plus_Unterpfad_zurueck()
    {
        Assert.Equal("Drive/D/Games/Cool Game/Saves",
            SaveRootKey.Derive(@"D:\Games\Cool Game\Saves"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Derive_leerer_Pfad_ergibt_leeren_Schluessel(string? folder)
    {
        Assert.Equal(string.Empty, SaveRootKey.Derive(folder!));
    }
}
