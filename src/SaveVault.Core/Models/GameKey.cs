using System.Text;
using System.Text.Json.Serialization;

namespace SaveVault.Core.Models;

/// <summary>
/// Kanonische Spielidentität. Der <see cref="Value"/> ist der geräteübergreifend
/// stabile Schlüssel, unter dem ein Spiel serverseitig „gebucket" wird – abgeleitet
/// aus dem Ludusavi-Fund (Store + Store-ID, sonst normalisierter Spielname).
/// Gleichheit richtet sich ausschließlich nach <see cref="Value"/>, damit dasselbe
/// Spiel auf verschiedenen Geräten dieselbe Identität bekommt.
/// </summary>
public sealed class GameKey : IEquatable<GameKey>
{
    /// <summary>Kanonischer, normalisierter Schlüssel (Server-Bucket-Identität).</summary>
    public string Value { get; }

    /// <summary>Menschenlesbarer Anzeigename (unnormalisiert, aus dem Ludusavi-Fund).</summary>
    public string DisplayName { get; }

    /// <summary>Store-Kennung (z. B. "steam"), falls bekannt.</summary>
    public string? Store { get; }

    /// <summary>Store-spezifische ID, falls bekannt.</summary>
    public string? StoreId { get; }

    [JsonConstructor]
    public GameKey(string value, string displayName, string? store = null, string? storeId = null)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("GameKey.Value darf nicht leer sein.", nameof(value));
        Value = value;
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? value : displayName;
        Store = store;
        StoreId = storeId;
    }

    /// <summary>Schlüssel aus einem reinen Spielnamen (normalisiert).</summary>
    public static GameKey FromName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Spielname darf nicht leer sein.", nameof(name));
        return new GameKey(Normalize(name), name.Trim());
    }

    /// <summary>Schlüssel aus Store + Store-ID (stärkste Identität).</summary>
    public static GameKey FromStore(string store, string storeId, string? displayName = null)
    {
        if (string.IsNullOrWhiteSpace(store))
            throw new ArgumentException("Store darf nicht leer sein.", nameof(store));
        if (string.IsNullOrWhiteSpace(storeId))
            throw new ArgumentException("Store-ID darf nicht leer sein.", nameof(storeId));
        var value = $"{Normalize(store)}:{Normalize(storeId)}";
        return new GameKey(value, displayName?.Trim() ?? $"{store.Trim()}:{storeId.Trim()}", store.Trim(), storeId.Trim());
    }

    /// <summary>
    /// Bequemer Einstieg aus einem Ludusavi-Fund: Store + ID, wenn vorhanden,
    /// sonst der normalisierte Name.
    /// </summary>
    public static GameKey FromLudusavi(string gameName, string? store = null, string? storeId = null)
        => (!string.IsNullOrWhiteSpace(store) && !string.IsNullOrWhiteSpace(storeId))
            ? FromStore(store!, storeId!, gameName)
            : FromName(gameName);

    /// <summary>
    /// Normalisierung: klein, getrimmt, nur Buchstaben/Ziffern; Trennzeichen werden
    /// zu einem einzelnen Leerzeichen zusammengefasst. Rein deterministisch.
    /// </summary>
    private static string Normalize(string s)
    {
        var sb = new StringBuilder(s.Length);
        var lastSpace = false;
        foreach (var ch in s.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
                lastSpace = false;
            }
            else if (!lastSpace && sb.Length > 0)
            {
                sb.Append(' ');
                lastSpace = true;
            }
        }
        var result = sb.ToString().TrimEnd();
        return result.Length == 0 ? s.Trim().ToLowerInvariant() : result;
    }

    public bool Equals(GameKey? other)
        => other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as GameKey);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString() => Value;
}
