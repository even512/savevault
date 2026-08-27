using System.Security.Cryptography;

namespace SaveVault.Core.Hashing;

/// <summary>
/// Berechnet SHA-256-Hashes (hex, klein) über Dateien und Byte-Folgen. Reine BCL-
/// Kryptografie, keine Fremdpakete.
/// </summary>
public static class FileHasher
{
    /// <summary>Hasht eine Datei synchron und gibt den Hash als klein-hex zurück.</summary>
    public static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return Convert.ToHexStringLower(sha.ComputeHash(stream));
    }

    /// <summary>Hasht eine Datei asynchron (streamt, hält keinen ganzen Puffer im Speicher).</summary>
    public static async Task<string> HashFileAsync(string path, CancellationToken ct = default)
    {
        await using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>Hasht einen Byte-Bereich (z. B. den Inhalt einer kleinen Datei im Speicher).</summary>
    public static string HashBytes(ReadOnlySpan<byte> data)
        => Convert.ToHexStringLower(SHA256.HashData(data));

    /// <summary>Hasht einen beliebigen Stream (z. B. beim Empfang eines Uploads serverseitig).</summary>
    public static async Task<string> HashStreamAsync(Stream stream, CancellationToken ct = default)
    {
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexStringLower(hash);
    }
}
