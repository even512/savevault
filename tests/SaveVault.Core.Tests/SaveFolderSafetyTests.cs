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
}
