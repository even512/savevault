using System.Security.Cryptography;
using System.Text;

namespace SaveVault.Server.Security;

/// <summary>
/// Kryptografische Helfer rund um Tokens und Pairing-Codes. Alle Zufallswerte kommen aus
/// <see cref="RandomNumberGenerator"/> (kryptografisch sicher). Token werden nur als Hash
/// gespeichert; Vergleiche laufen konstant-zeitig, um Timing-Angriffe zu vermeiden.
/// </summary>
public static class Secrets
{
    // Unmissverständliches Alphabet für Pairing-Codes: keine 0/O/1/I/L-Verwechslungen.
    private const string CodeAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    /// <summary>Erzeugt einen kurzen, gut ablesbaren Pairing-Code der Form <c>7K2-9QX</c>.</summary>
    public static string NewPairingCode()
        => $"{RandomChunk(3)}-{RandomChunk(3)}";

    /// <summary>Erzeugt einen langen, zufälligen Geräte-Token (URL-sicher, base64url).</summary>
    public static string NewDeviceToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    /// <summary>Erzeugt eine zufällige, undurchsichtige ID (z. B. Geräte-/Befehls-ID).</summary>
    public static string NewId()
        => Guid.NewGuid().ToString("N");

    /// <summary>Hex-SHA-256 eines Tokens – so wird nie der rohe Token gespeichert.</summary>
    public static string HashToken(string token)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    /// <summary>Konstant-zeitiger Vergleich zweier Strings (gegen Timing-Angriffe).</summary>
    public static bool FixedTimeEquals(string a, string b)
    {
        var ba = Encoding.UTF8.GetBytes(a);
        var bb = Encoding.UTF8.GetBytes(b);
        return CryptographicOperations.FixedTimeEquals(ba, bb);
    }

    private static string RandomChunk(int length)
    {
        var sb = new StringBuilder(length);
        for (var i = 0; i < length; i++)
            sb.Append(CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)]);
        return sb.ToString();
    }
}
