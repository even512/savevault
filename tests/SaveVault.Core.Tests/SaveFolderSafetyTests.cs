using System.IO;
using SaveVault.Core.Storage;

namespace SaveVault.Core.Tests;

/// <summary>
/// Tests des <see cref="SaveFolderSafety"/>-Helfers – der Abwehr gegen zu breite
/// Save-Ordner (Laufwerks-/Systemwurzeln), die den Client beim Scannen/Überwachen
/// über die ganze Platte blockieren würden. <see cref="SaveFolderSafety.IsDriveRootOrEmpty"/>
/// erkennt Wurzeln/leere Eingaben; die reine <c>IsTooBroad(path, broadRoots)</c>-Überladung
/// prüft zusätzlich die Segment-Tiefe und die bekannten Sammelwurzeln – deterministisch.
/// Die Pfade sind im Windows-Format, da die Tests auf Windows laufen.
/// </summary>
public class SaveFolderSafetyTests
{
    // --- IsDriveRootOrEmpty --------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("C:\\")]
    [InlineData("C:/")]
    [InlineData("D:\\")]
    public void IsDriveRootOrEmpty_true_fuer_leere_und_Laufwerkswurzeln(string? path)
    {
        Assert.True(SaveFolderSafety.IsDriveRootOrEmpty(path));
    }

    [Fact]
    public void IsDriveRootOrEmpty_false_fuer_konkreten_tiefen_Ordner()
    {
        Assert.False(SaveFolderSafety.IsDriveRootOrEmpty("C:\\Users\\tim\\AppData\\Local\\Game\\Saved"));
    }

    // --- IsTooBroad(path, broadRoots) – reine, deterministische Überladung ----

    private static readonly IReadOnlyCollection<string> BroadRoots = new[]
    {
        "C:\\Users",
        "C:\\Users\\tim",
        "C:\\Users\\tim\\AppData\\Local",
        "C:\\Windows",
        "C:\\Program Files",
    };

    [Theory]
    [InlineData("C:\\")]
    [InlineData("D:\\")]
    public void IsTooBroad_true_fuer_Laufwerkswurzel(string path)
    {
        Assert.True(SaveFolderSafety.IsTooBroad(path, BroadRoots));
    }

    [Theory]
    [InlineData("C:\\Users\\tim")]                     // = broadRoot (UserProfile), 2 Segmente
    [InlineData("C:\\Users\\tim\\AppData\\Local")]     // = broadRoot (LocalAppData)
    [InlineData("C:\\Windows")]                        // = broadRoot (1 Segment)
    public void IsTooBroad_true_fuer_bekannte_Sammelwurzel(string path)
    {
        Assert.True(SaveFolderSafety.IsTooBroad(path, BroadRoots));
    }

    [Theory]
    [InlineData("C:\\Users")]        // 1 Segment unter der Wurzel → zu flach
    [InlineData("C:\\ProgramData")]  // 1 Segment unter der Wurzel → zu flach
    public void IsTooBroad_true_fuer_zu_flachen_Pfad(string path)
    {
        Assert.True(SaveFolderSafety.IsTooBroad(path, BroadRoots));
    }

    [Theory]
    [InlineData("C:\\Users\\tim\\AppData\\Local\\Game\\Saved")] // ≥2 Segmente, nicht in broadRoots
    [InlineData("D:\\Games\\Cool Game\\Saves")]
    public void IsTooBroad_false_fuer_konkreten_tiefen_Save_Ordner(string path)
    {
        Assert.False(SaveFolderSafety.IsTooBroad(path, BroadRoots));
    }

    // --- IsContainerRoot – Sammelordner, die eine Ebene tiefer betreten werden ----

    [Theory]
    // Steam-Install-Wurzeln (an Laufwerk / Program Files verankert)
    [InlineData("D:\\Steam")]
    [InlineData("C:\\Program Files (x86)\\Steam")]
    [InlineData("C:\\Program Files\\Steam")]
    [InlineData("E:\\SteamLibrary")]
    // steamapps / common
    [InlineData("D:\\SteamLibrary\\steamapps")]
    [InlineData("C:\\Program Files (x86)\\Steam\\steamapps\\common")]
    // Steam userdata (Sammelordner über alle Konten + je Konto)
    [InlineData("C:\\Program Files (x86)\\Steam\\userdata")]
    [InlineData("C:\\Program Files (x86)\\Steam\\userdata\\56296790")]
    // Ubisoft
    [InlineData("C:\\Program Files (x86)\\Ubisoft\\Ubisoft Game Launcher")]
    [InlineData("C:\\Program Files (x86)\\Ubisoft\\Ubisoft Game Launcher\\savegames")]
    [InlineData("C:\\Program Files (x86)\\Ubisoft\\Ubisoft Game Launcher\\savegames\\db47b069-c627-4678-b277-316c8a9cf11d")]
    // Launcher-Bibliotheken
    [InlineData("C:\\GOG Games")]
    [InlineData("D:\\Epic Games")]
    public void IsContainerRoot_true_fuer_bekannte_Sammelwurzeln(string path)
    {
        Assert.True(SaveFolderSafety.IsContainerRoot(path));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    // Enge, spielspezifische Ordner UNTER einem Container sind KEIN Container mehr:
    [InlineData("C:\\Program Files (x86)\\Steam\\userdata\\56296790\\730")]      // …\userdata\<id>\<appid>
    [InlineData("C:\\Program Files (x86)\\Steam\\steamapps\\common\\No Man's Sky")]
    [InlineData("C:\\Program Files (x86)\\Ubisoft\\Ubisoft Game Launcher\\savegames\\db47b069-c627-4678-b277-316c8a9cf11d\\6100")]
    // Bloß der Name „steam" tief in Nutzerdaten ist KEIN Container (nicht verankert):
    [InlineData("C:\\Users\\tim\\Documents\\Battlefield 6\\settings\\steam")]
    // Ein Unreal-„SaveGames\steam\<steamid>" ist kein Ubisoft-savegames-Container:
    [InlineData("C:\\Users\\tim\\AppData\\Local\\WB Games\\LEGO\\SaveGames\\steam\\76561197960285355")]
    // Gewöhnlicher Save-Ordner:
    [InlineData("C:\\Users\\tim\\Saved Games\\CD Projekt Red\\Cyberpunk 2077")]
    public void IsContainerRoot_false_fuer_enge_oder_ungueltige_Pfade(string? path)
    {
        Assert.False(SaveFolderSafety.IsContainerRoot(path));
    }

    // --- IsBroadUserStructure – lexikalische, maschinenunabhängige Sammelwurzeln ----

    [Theory]
    [InlineData("C:\\Users\\beliebig")]                          // Benutzerprofil (beliebiger Name)
    [InlineData("D:\\Users\\zweitprofil")]
    [InlineData("C:\\Users\\tim\\AppData")]                      // AppData-Sammelebene
    [InlineData("C:\\Users\\tim\\AppData\\Local")]
    [InlineData("C:\\Users\\tim\\AppData\\LocalLow")]
    [InlineData("C:\\Users\\tim\\AppData\\Roaming")]
    public void IsBroadUserStructure_true_fuer_universelle_Sammelwurzeln(string path)
    {
        Assert.True(SaveFolderSafety.IsBroadUserStructure(path));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("C:\\Users\\tim\\Documents")]                    // Documents ist NICHT breit
    [InlineData("C:\\Users\\tim\\Documents\\My Game")]
    [InlineData("C:\\Users\\tim\\AppData\\Roaming\\HelloGames")] // Vendor unter Roaming ist eng
    [InlineData("C:\\Users\\tim\\AppData\\LocalLow\\Vendor\\Spiel")]
    [InlineData("C:\\Users\\tim\\Saved Games\\Vendor")]
    public void IsBroadUserStructure_false_fuer_enge_oder_ungueltige_Pfade(string? path)
    {
        Assert.False(SaveFolderSafety.IsBroadUserStructure(path));
    }
}
