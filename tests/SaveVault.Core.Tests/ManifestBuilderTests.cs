using System.Linq;
using SaveVault.Core.Hashing;
using SaveVault.Core.Models;

namespace SaveVault.Core.Tests;

/// <summary>
/// Tests für <see cref="ManifestBuilder"/>, <see cref="FileHasher"/> und
/// <see cref="ManifestBuilder.Diff"/> gegen echte Temp-Ordner. Jeder Test räumt seinen
/// Ordner über <see cref="TempDirectory"/> (IDisposable) auf. Keine Zeit-/Netz-Abhängigkeit:
/// der Manifest-Hash geht laut Produktivcode nur über Pfad+Hash+Größe, nicht über mtime.
/// </summary>
public class ManifestBuilderTests
{
    private readonly ManifestBuilder _builder = new();

    // --- FileHasher ---------------------------------------------------------

    [Fact]
    public void FileHasher_HashBytes_ist_deterministisch_und_kleinhex()
    {
        var a = FileHasher.HashBytes("hallo welt"u8);
        var b = FileHasher.HashBytes("hallo welt"u8);

        Assert.Equal(a, b);
        Assert.Equal(64, a.Length); // SHA-256 hex
        Assert.Equal(a, a.ToLowerInvariant());
    }

    [Fact]
    public void FileHasher_HashBytes_unterscheidet_verschiedene_Eingaben()
    {
        Assert.NotEqual(FileHasher.HashBytes("a"u8), FileHasher.HashBytes("b"u8));
    }

    [Fact]
    public void FileHasher_HashFile_stimmt_mit_HashBytes_ueberein()
    {
        using var dir = new TempDirectory();
        var path = dir.WriteFile("f.txt", "Inhalt");

        var fileHash = FileHasher.HashFile(path);
        var bytesHash = FileHasher.HashBytes("Inhalt"u8);

        Assert.Equal(bytesHash, fileHash);
    }

    // --- ManifestBuilder.Build ---------------------------------------------

    [Fact]
    public void Build_leerer_oder_fehlender_Ordner_liefert_leeres_Manifest()
    {
        var manifest = _builder.Build(System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "savevault-tests", System.Guid.NewGuid().ToString("N")));

        Assert.Equal(0, manifest.FileCount);
        Assert.Equal(0, manifest.TotalBytes);
        Assert.Empty(manifest.Entries);
    }

    [Fact]
    public void Build_wirft_bei_leerem_Wurzelpfad()
    {
        Assert.Throws<ArgumentException>(() => _builder.Build("  "));
    }

    [Fact]
    public void Build_gleicher_Inhalt_ergibt_gleichen_ManifestHash()
    {
        using var a = new TempDirectory();
        using var b = new TempDirectory();
        a.WriteFile("save.dat", "Spielstand-Inhalt");
        b.WriteFile("save.dat", "Spielstand-Inhalt");

        var ma = _builder.Build(a.Path);
        var mb = _builder.Build(b.Path);

        Assert.Equal(ma.ManifestHash, mb.ManifestHash);
        Assert.Equal(ma.Entries.Single().Sha256, mb.Entries.Single().Sha256);
    }

    [Fact]
    public void Build_geaenderter_Inhalt_ergibt_anderen_Hash()
    {
        using var a = new TempDirectory();
        using var b = new TempDirectory();
        a.WriteFile("save.dat", "Version A");
        b.WriteFile("save.dat", "Version B");

        var ma = _builder.Build(a.Path);
        var mb = _builder.Build(b.Path);

        Assert.NotEqual(ma.Entries.Single().Sha256, mb.Entries.Single().Sha256);
        Assert.NotEqual(ma.ManifestHash, mb.ManifestHash);
    }

    [Fact]
    public void Build_erfasst_verschachtelte_Unterordner_mit_normalisiertem_Pfad()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("top.dat", "1");
        dir.WriteFile("sub/inner.dat", "2");
        dir.WriteFile("sub/deep/leaf.dat", "3");

        var manifest = _builder.Build(dir.Path);
        var paths = manifest.Entries.Select(e => e.RelativePath).ToHashSet();

        Assert.Equal(3, manifest.FileCount);
        Assert.Contains("top.dat", paths);
        Assert.Contains("sub/inner.dat", paths);       // '/' normalisiert
        Assert.Contains("sub/deep/leaf.dat", paths);
        Assert.DoesNotContain(paths, p => p.Contains('\\')); // kein Backslash
    }

    [Fact]
    public void Build_setzt_TotalBytes_und_FileCount()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("a.dat", "12345");  // 5 Bytes
        dir.WriteFile("b.dat", "678");    // 3 Bytes

        var manifest = _builder.Build(dir.Path);

        Assert.Equal(2, manifest.FileCount);
        Assert.Equal(8, manifest.TotalBytes);
    }

    [Fact]
    public void Build_mit_Vorfilter_liefert_gleiches_Manifest_wie_ohne()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("save.dat", "unverändert");
        dir.WriteFile("sub/other.dat", "auch unverändert");

        var ohnePrevious = _builder.Build(dir.Path);
        var mitPrevious = _builder.Build(dir.Path, previous: ohnePrevious);

        Assert.Equal(ohnePrevious.ManifestHash, mitPrevious.ManifestHash);
        Assert.Equal(ohnePrevious.FileCount, mitPrevious.FileCount);
        Assert.Equal(ohnePrevious.TotalBytes, mitPrevious.TotalBytes);
    }

    // --- ManifestBuilder.Diff ----------------------------------------------

    [Fact]
    public void Diff_erkennt_hinzugefuegte_Dateien()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("a.dat", "A");
        var old = _builder.Build(dir.Path);

        dir.WriteFile("b.dat", "B");
        var current = _builder.Build(dir.Path);

        var diff = ManifestBuilder.Diff(old, current);

        Assert.Single(diff.Added);
        Assert.Equal("b.dat", diff.Added.Single().RelativePath);
        Assert.Empty(diff.Removed);
        Assert.Empty(diff.Changed);
        Assert.True(diff.HasChanges);
    }

    [Fact]
    public void Diff_erkennt_entfernte_Dateien()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("a.dat", "A");
        dir.WriteFile("b.dat", "B");
        var old = _builder.Build(dir.Path);

        System.IO.File.Delete(System.IO.Path.Combine(dir.Path, "b.dat"));
        var current = _builder.Build(dir.Path);

        var diff = ManifestBuilder.Diff(old, current);

        Assert.Single(diff.Removed);
        Assert.Equal("b.dat", diff.Removed.Single().RelativePath);
        Assert.Empty(diff.Added);
        Assert.Empty(diff.Changed);
    }

    [Fact]
    public void Diff_erkennt_geaenderte_Dateien()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("a.dat", "alt");
        var old = _builder.Build(dir.Path);

        dir.WriteFile("a.dat", "neu und laenger");
        var current = _builder.Build(dir.Path);

        var diff = ManifestBuilder.Diff(old, current);

        Assert.Single(diff.Changed);
        Assert.Equal("a.dat", diff.Changed.Single().RelativePath);
        Assert.Empty(diff.Added);
        Assert.Empty(diff.Removed);
    }

    [Fact]
    public void Diff_ohne_altes_Manifest_zaehlt_alles_als_hinzugefuegt()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("a.dat", "A");
        dir.WriteFile("b.dat", "B");
        var current = _builder.Build(dir.Path);

        var diff = ManifestBuilder.Diff(old: null, current);

        Assert.Equal(2, diff.Added.Count);
        Assert.Empty(diff.Removed);
        Assert.Empty(diff.Changed);
    }

    [Fact]
    public void Diff_gleiche_Manifeste_hat_keine_Aenderungen()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("a.dat", "A");
        var m = _builder.Build(dir.Path);

        var diff = ManifestBuilder.Diff(m, m);

        Assert.False(diff.HasChanges);
    }
}
