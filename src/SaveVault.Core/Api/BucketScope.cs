using SaveVault.Core.Models;

namespace SaveVault.Core.Api;

/// <summary>
/// In welchem Bucket ein Spiel serverseitig geführt wird. Ab dem Umbau auf geräte-eigene
/// Buckets gibt es drei Sorten (siehe <c>specs/savevault-change-per-device-sync.md</c>):
/// <list type="bullet">
///   <item><b>Private</b> – (Gerät, Spiel): das Standard-Backup genau eines Geräts.</item>
///   <item><b>Shared</b> – (Spiel): der geräteübergreifend geteilte Stand.</item>
///   <item><b>Legacy</b> – der alte globale Bucket (vor dem Umbau); eingefroren.</item>
/// </list>
/// </summary>
public enum BucketScope
{
    /// <summary>Privater Bucket eines einzelnen Geräts (Default).</summary>
    Private,

    /// <summary>Geräteübergreifend geteilter Bucket.</summary>
    Shared,

    /// <summary>Alter globaler Bucket vor dem Per-Gerät-Umbau (eingefroren).</summary>
    Legacy,
}

/// <summary>
/// Leitet aus einer kanonischen <see cref="GameKey"/> den <b>effektiven Bucket-Schlüssel</b> je
/// <see cref="BucketScope"/> ab. Der ganze Server (Index-Lookups, Ablagepfade, Konflikte) bleibt
/// unverändert nach <see cref="GameKey.Value"/> verschlüsselt – die Scope-/Owner-Trennung steckt
/// allein im abgeleiteten <c>Value</c>-Präfix:
/// <list type="bullet">
///   <item><c>dev|{owner}|{value}</c> für einen privaten Bucket,</item>
///   <item><c>shared|{value}</c> für den geteilten Bucket,</item>
///   <item>der unveränderte <c>value</c> für Legacy.</item>
/// </list>
/// Das Trennzeichen <c>|</c> kann von der Schlüssel-Normalisierung (<see cref="GameKey"/>) nie
/// erzeugt werden – ein normaler Spielschlüssel wird also nie fälschlich als Präfix erkannt.
/// Der <see cref="GameKey.DisplayName"/> (und Store/StoreId) bleibt der echte Anzeigename.
/// </summary>
public static class BucketKey
{
    private const string PrivateMarker = "dev|";
    private const string SharedMarker = "shared|";

    /// <summary>
    /// Effektiver Bucket-Schlüssel für die gegebene Sicht. <paramref name="ownerDeviceId"/> ist
    /// nur (und zwingend) für <see cref="BucketScope.Private"/> nötig – der Owner wird serverseitig
    /// aus dem authentifizierten Gerät abgeleitet, nie vom Client gewählt (Owner-Isolation).
    /// </summary>
    public static GameKey Resolve(GameKey game, BucketScope scope, string? ownerDeviceId)
    {
        ArgumentNullException.ThrowIfNull(game);
        return scope switch
        {
            BucketScope.Legacy => game,
            BucketScope.Shared => WithValue(game, SharedMarker + game.Value),
            BucketScope.Private => string.IsNullOrWhiteSpace(ownerDeviceId)
                ? throw new ArgumentException("Privater Bucket erfordert eine Owner-Geräte-ID.", nameof(ownerDeviceId))
                : WithValue(game, $"{PrivateMarker}{ownerDeviceId}|{game.Value}"),
            _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unbekannter Bucket-Scope."),
        };
    }

    /// <summary>
    /// Bildet einen (evtl. bereits aufgelösten) Bucket-Schlüssel zurück auf die kanonische
    /// Spiel-Identität, unter der der Client lokal (Registry/Sync-State) bucht. Ein privater
    /// (<c>dev|{owner}|…</c>) oder geteilter (<c>shared|…</c>) Schlüssel wird auf seinen
    /// Originalanteil reduziert; ein Legacy-/Originalschlüssel bleibt unverändert. Nötig, weil
    /// Server-Befehle den effektiven Bucket-Schlüssel tragen, der Client aber nach Originalschlüssel
    /// nachschlägt.
    /// </summary>
    public static GameKey Original(GameKey bucket)
    {
        ArgumentNullException.ThrowIfNull(bucket);
        var v = bucket.Value;
        if (v.StartsWith(PrivateMarker, StringComparison.Ordinal))
        {
            // Format dev|{owner}|{value}. Der value-Anteil (kanonischer Schlüssel) enthält nie ein
            // '|' (die Schlüssel-Normalisierung erzeugt keins) – also am LETZTEN '|' trennen. Damit
            // ist die Rückführung auch dann korrekt, wenn eine Owner-ID selbst ein '|' enthielte.
            var rest = v[PrivateMarker.Length..];
            var sep = rest.LastIndexOf('|');
            if (sep >= 0 && sep + 1 < rest.Length)
                return WithValue(bucket, rest[(sep + 1)..]);
        }
        else if (v.StartsWith(SharedMarker, StringComparison.Ordinal))
        {
            var rest = v[SharedMarker.Length..];
            if (rest.Length > 0)
                return WithValue(bucket, rest);
        }
        return bucket;
    }

    /// <summary>Leitet den Scope aus einem (evtl. bereits aufgelösten) Bucket-Schlüssel ab.</summary>
    public static BucketScope ScopeOf(string bucketValue)
    {
        ArgumentNullException.ThrowIfNull(bucketValue);
        if (bucketValue.StartsWith(PrivateMarker, StringComparison.Ordinal)) return BucketScope.Private;
        if (bucketValue.StartsWith(SharedMarker, StringComparison.Ordinal)) return BucketScope.Shared;
        return BucketScope.Legacy;
    }

    /// <summary>Scope als Wire-Wert (Query-Parameter <c>?scope=</c>).</summary>
    public static string ToWire(BucketScope scope) => scope switch
    {
        BucketScope.Private => "private",
        BucketScope.Shared => "shared",
        BucketScope.Legacy => "legacy",
        _ => "private",
    };

    /// <summary>
    /// Scope aus einem Wire-Wert; <c>null</c>/leer liefert <paramref name="fallback"/>. Ein
    /// unbekannter Wert wirft (der Aufrufer bildet das auf 400 ab).
    /// </summary>
    public static BucketScope FromWire(string? wire, BucketScope fallback)
    {
        if (string.IsNullOrWhiteSpace(wire)) return fallback;
        return wire.Trim().ToLowerInvariant() switch
        {
            "private" => BucketScope.Private,
            "shared" => BucketScope.Shared,
            "legacy" => BucketScope.Legacy,
            _ => throw new ArgumentException($"Unbekannter Bucket-Scope: '{wire}'.", nameof(wire)),
        };
    }

    private static GameKey WithValue(GameKey game, string newValue)
        => new(newValue, game.DisplayName, game.Store, game.StoreId);
}
