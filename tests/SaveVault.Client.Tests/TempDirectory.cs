using System;
using System.IO;

namespace SaveVault.Client.Tests;

/// <summary>
/// Wegwerf-Verzeichnis für Tests gegen echte Datei-IO (analog zu
/// <c>SaveVault.Core.Tests.TempDirectory</c>, hier eigenständig, da beide Testprojekte
/// unabhängig voneinander bleiben sollen).
/// </summary>
public sealed class TempDirectory : IDisposable
{
    public string Path { get; }

    public TempDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "savevault-client-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    /// <summary>Schreibt eine Datei (Unterordner werden bei Bedarf angelegt).</summary>
    public string WriteFile(string relativePath, string content)
    {
        var full = System.IO.Path.Combine(Path, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
        var dir = System.IO.Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(full, content);
        return full;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
        catch (IOException) { /* best effort */ }
        catch (UnauthorizedAccessException) { /* best effort */ }
    }
}
