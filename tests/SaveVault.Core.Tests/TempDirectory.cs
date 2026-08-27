using System;
using System.IO;

namespace SaveVault.Core.Tests;

/// <summary>
/// Ein eindeutiger, sich selbst aufräumender Temp-Ordner für Datei-IO-Tests.
/// Liegt unter %TEMP%\savevault-tests\&lt;guid&gt; und wird beim Dispose rekursiv
/// gelöscht (fehler-tolerant, damit ein Aufräum-Problem keinen Test rot färbt).
/// </summary>
public sealed class TempDirectory : IDisposable
{
    public string Path { get; }

    public TempDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "savevault-tests",
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
        catch (IOException) { /* Aufräumen ist best-effort. */ }
        catch (UnauthorizedAccessException) { /* dito */ }
    }
}
