using SaveVault.Core.Models;
using SaveVault.Core.Storage;

namespace SaveVault.Core.Tests;

/// <summary>
/// Tests der Pfad-Sanitisierung (<see cref="PathSanitizer"/>) — die Traversal-Abwehr
/// des Server-Speichers. Normale relative Pfade werden aufgelöst, alle Ausbruchs-
/// versuche (<c>..</c>, absolute/rooted Pfade, UNC, Präfix-Trick) abgewiesen.
/// <see cref="PathSanitizer.HashKey"/> muss deterministisch und dateinamens-sicher sein.
/// </summary>
public class PathSanitizerTests
{
    // --- TryResolveWithin: erlaubte Pfade -----------------------------------

    [Fact]
    public void TryResolveWithin_akzeptiert_einfachen_relativen_Pfad()
    {
        using var root = new TempDirectory();

        var ok = PathSanitizer.TryResolveWithin(root.Path, "save.dat", out var full);

        Assert.True(ok);
        Assert.True(PathSanitizer.IsWithinRoot(root.Path, full));
    }

    [Fact]
    public void TryResolveWithin_akzeptiert_relativen_Pfad_mit_Unterordnern()
    {
        using var root = new TempDirectory();

        var ok = PathSanitizer.TryResolveWithin(root.Path, "sub/deep/save.dat", out var full);

        Assert.True(ok);
        Assert.True(PathSanitizer.IsWithinRoot(root.Path, full));
        Assert.EndsWith("save.dat", full);
    }

    [Fact]
    public void TryResolveWithin_akzeptiert_Backslash_getrennte_Segmente()
    {
        using var root = new TempDirectory();

        var ok = PathSanitizer.TryResolveWithin(root.Path, "sub\\save.dat", out var full);

        Assert.True(ok);
        Assert.True(PathSanitizer.IsWithinRoot(root.Path, full));
    }

    // --- TryResolveWithin: Traversal-Abwehr ---------------------------------

    [Theory]
    [InlineData("../x")]
    [InlineData("a/../../x")]
    [InlineData("..\\x")]
    [InlineData("sub/../../escape")]
    public void TryResolveWithin_lehnt_DotDot_Ausbruch_ab(string relative)
    {
        using var root = new TempDirectory();

        var ok = PathSanitizer.TryResolveWithin(root.Path, relative, out var full);

        Assert.False(ok);
        Assert.Equal(string.Empty, full);
    }

    [Theory]
    [InlineData("C:\\Windows\\system32")]
    [InlineData("/etc/passwd")]
    [InlineData("\\\\server\\share\\x")] // UNC
    public void TryResolveWithin_lehnt_absolute_und_rooted_Pfade_ab(string relative)
    {
        using var root = new TempDirectory();

        var ok = PathSanitizer.TryResolveWithin(root.Path, relative, out var full);

        Assert.False(ok);
        Assert.Equal(string.Empty, full);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void TryResolveWithin_lehnt_leere_Eingabe_ab(string relative)
    {
        using var root = new TempDirectory();

        Assert.False(PathSanitizer.TryResolveWithin(root.Path, relative, out _));
    }

    // --- IsWithinRoot: Präfix-Trick -----------------------------------------

    [Fact]
    public void IsWithinRoot_true_fuer_Pfad_unter_root()
    {
        var root = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sv-root"));
        var candidate = System.IO.Path.Combine(root, "sub", "file.dat");

        Assert.True(PathSanitizer.IsWithinRoot(root, candidate));
    }

    [Fact]
    public void IsWithinRoot_true_fuer_root_selbst()
    {
        var root = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sv-root"));

        Assert.True(PathSanitizer.IsWithinRoot(root, root));
    }

    [Fact]
    public void IsWithinRoot_false_beim_Praefix_Trick()
    {
        // root ".../save" darf NICHT ".../save-evil" als "innerhalb" durchgehen lassen.
        var baseDir = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sv-prefix"));
        var root = System.IO.Path.Combine(baseDir, "save");
        var evil = System.IO.Path.Combine(baseDir, "save-evil", "loot.dat");

        Assert.False(PathSanitizer.IsWithinRoot(root, evil));
    }

    [Fact]
    public void IsWithinRoot_false_fuer_Elternverzeichnis()
    {
        var baseDir = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sv-parent"));
        var root = System.IO.Path.Combine(baseDir, "save");
        var parentFile = System.IO.Path.Combine(baseDir, "secret.dat");

        Assert.False(PathSanitizer.IsWithinRoot(root, parentFile));
    }

    // --- HashKey ------------------------------------------------------------

    [Fact]
    public void HashKey_ist_deterministisch()
    {
        Assert.Equal(PathSanitizer.HashKey("steam:12345"), PathSanitizer.HashKey("steam:12345"));
    }

    [Fact]
    public void HashKey_verschiedene_Keys_verschiedene_Hashes()
    {
        Assert.NotEqual(PathSanitizer.HashKey("steam:1"), PathSanitizer.HashKey("steam:2"));
    }

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("C:\\Windows")]
    [InlineData("a/b\\c")]
    [InlineData("normaler name")]
    public void HashKey_ist_ein_sicherer_Dateiname(string key)
    {
        var hash = PathSanitizer.HashKey(key);

        Assert.Equal(64, hash.Length);
        Assert.DoesNotContain('/', hash);
        Assert.DoesNotContain('\\', hash);
        Assert.DoesNotContain("..", hash);
        Assert.All(hash, ch => Assert.True(Uri.IsHexDigit(ch)));
    }

    [Fact]
    public void HashKey_wirft_bei_null()
    {
        Assert.Throws<ArgumentNullException>(() => PathSanitizer.HashKey(null!));
    }

    // --- SafeGameFolder / SanitizeSegment (öffentliche Ablage-Bausteine) ----

    [Fact]
    public void SafeGameFolder_entspricht_dem_gehashten_Schluessel()
    {
        var game = GameKey.FromName("Half-Life");

        Assert.Equal(PathSanitizer.HashKey(game.Value), PathSanitizer.SafeGameFolder(game));
    }

    [Theory]
    [InlineData("..", "_")]
    [InlineData(".", "_")]
    [InlineData("", "_")]
    [InlineData("   ", "_")]
    public void SanitizeSegment_neutralisiert_Traversal_und_leere_Namen(string input, string expected)
    {
        Assert.Equal(expected, PathSanitizer.SanitizeSegment(input));
    }

    [Fact]
    public void SanitizeSegment_ersetzt_Pfadtrenner_durch_Unterstrich()
    {
        var result = PathSanitizer.SanitizeSegment("a/b\\c");

        Assert.DoesNotContain('/', result);
        Assert.DoesNotContain('\\', result);
    }
}
