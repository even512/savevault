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

    /// <summary>Erzeugt einen langen, zufälligen Session-Token (wie ein Geräte-Token).</summary>
    public static string NewSessionToken() => NewDeviceToken();

    // --- Passwort-Hashing (PBKDF2/SHA-256) -----------------------------------------
    // Format: pbkdf2$<iterationen>$<salt-base64>$<hash-base64>. Das Klartext-Passwort wird
    // NIE gespeichert; verglichen wird konstant-zeitig über die abgeleiteten Bytes.

    private const int Pbkdf2Iterations = 100_000;
    private const int Pbkdf2SaltBytes = 16;
    private const int Pbkdf2HashBytes = 32;

    /// <summary>Leitet aus einem Passwort einen speicherbaren PBKDF2-Hash (mit Zufalls-Salt) ab.</summary>
    public static string HashPassword(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        var salt = RandomNumberGenerator.GetBytes(Pbkdf2SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, Pbkdf2HashBytes);
        return $"pbkdf2${Pbkdf2Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    /// <summary>Prüft ein Passwort gegen einen mit <see cref="HashPassword"/> erzeugten Hash (konstant-zeitig).</summary>
    public static bool VerifyPassword(string password, string stored)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(stored))
            return false;
        var parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != "pbkdf2")
            return false;
        if (!int.TryParse(parts[1], out var iterations) || iterations < 1)
            return false;
        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }
        var actual = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

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
