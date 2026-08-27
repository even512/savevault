namespace SaveVault.Core.Models;

/// <summary>
/// Ein einzelner Datei-Eintrag eines Manifests. <see cref="RelativePath"/> ist immer
/// mit '/' normalisiert und relativ zum Save-Wurzelverzeichnis. <see cref="Sha256"/>
/// ist der hex-kodierte SHA-256-Hash des Datei-Inhalts.
/// </summary>
public sealed record FileEntry(
    string RelativePath,
    string Sha256,
    long Size,
    DateTime LastWriteUtc);
