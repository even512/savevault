using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SaveVault.Core.Models;

namespace SaveVault.Client.Services;

/// <summary>
/// Lädt die Box-Art eines Spiels <b>auf Anforderung</b> (lazy, ein Spiel je Aufruf) über den
/// vorhandenen Geräte-Token/HttpClient des Agents und cacht das Ergebnis lokal unter dem
/// App-Datenpfad. Die GUI (Schritt Oberfläche) fragt hier ein einzelnes Cover an und nutzt bei
/// <c>null</c> ihren Farbverlauf-Fallback.
///
/// <para><b>Sicherheit:</b> Der Cache-Dateiname wird aus einem SHA-256-<b>Hash</b> des
/// <see cref="GameKey.Value"/> gebildet (nie roh als Pfad → keine Pfad-Injection). Der Fetcher
/// URL-kodiert den gameKey beim Routen-Bau (siehe <c>SaveVaultApiClient.GetCoverAsync</c>). Ziel
/// ist ausschließlich der konfigurierte Server – keine dynamische Fremd-URL.</para>
///
/// <para><b>Robust:</b> Wirft nie. Netz-/Serverfehler → <c>null</c>. Ein 404 („kein Cover")
/// wird kurzlebig negativ gemerkt, damit nicht bei jedem Öffnen erneut angefragt wird.
/// Fremd-Bytes werden als <see cref="BitmapImage"/> dekodiert; Dekodier-Fehler → <c>null</c>,
/// nie ein Absturz.</para>
/// </summary>
public sealed class CoverCache
{
    private readonly AppPaths _paths;
    private readonly Func<GameKey, CancellationToken, Task<byte[]?>> _fetch;
    private readonly TimeSpan _negativeTtl;

    private readonly object _lock = new();
    // In-Memory Positiv-Cache bereits dekodierter (eingefrorener) Bilder: gameKey.Value → Bild.
    private readonly Dictionary<string, ImageSource> _images = new(StringComparer.Ordinal);
    // In-Memory Negativ-Cache: gameKey.Value → Ablaufzeitpunkt (UTC), bis wann „kein Cover" gilt.
    private readonly Dictionary<string, DateTime> _negativeUntil = new(StringComparer.Ordinal);

    /// <param name="paths">Liefert das Cache-Verzeichnis (<see cref="AppPaths.CoverCacheDirectory"/>).</param>
    /// <param name="fetch">Holt die JPEG-Bytes eines Spiels vom Server (oder <c>null</c> bei 404/nicht verbunden).</param>
    /// <param name="negativeTtl">Frist des Negativ-Caches (Default 30 min).</param>
    public CoverCache(AppPaths paths, Func<GameKey, CancellationToken, Task<byte[]?>> fetch, TimeSpan? negativeTtl = null)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _fetch = fetch ?? throw new ArgumentNullException(nameof(fetch));
        _negativeTtl = negativeTtl ?? TimeSpan.FromMinutes(30);
    }

    /// <summary>
    /// Liefert das Cover eines Spiels als eingefrorene <see cref="ImageSource"/> oder <c>null</c>
    /// (kein Cover / nicht verbunden / Fehler). Reihenfolge: Speicher-Cache → Platten-Cache →
    /// Server. Wirft nie.
    /// </summary>
    public async Task<ImageSource?> GetCoverAsync(GameKey game, CancellationToken ct = default)
    {
        if (game is null)
            return null;
        var key = game.Value;

        lock (_lock)
        {
            if (_images.TryGetValue(key, out var cached))
                return cached;
            if (_negativeUntil.TryGetValue(key, out var until) && until > DateTime.UtcNow)
                return null;
        }

        // 1) Platten-Cache (aus einer früheren Sitzung) versuchen.
        var file = CacheFilePath(key);
        var fromDisk = TryLoadImage(file);
        if (fromDisk is not null)
        {
            lock (_lock) _images[key] = fromDisk;
            return fromDisk;
        }

        // 2) Vom Server holen – nie werfen.
        byte[]? bytes;
        try
        {
            bytes = await _fetch(game, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return null; // Abbruch: nicht negativ merken, beim nächsten Mal erneut versuchen.
        }
        catch
        {
            return null; // Netz-/Serverfehler → Fallback; kein dauerhafter Negativ-Cache.
        }

        if (bytes is null || bytes.Length == 0)
        {
            // 404 / kein Cover → kurzlebig negativ merken.
            RememberMissing(key);
            return null;
        }

        // 3) Fremd-Bytes dekodieren; Dekodier-Fehler → Fallback.
        var image = TryDecode(bytes);
        if (image is null)
        {
            RememberMissing(key);
            return null;
        }

        // 4) Best effort auf Platte cachen (Cache ist verwerfbar – Fehler ignorieren).
        TrySaveToDisk(file, bytes);

        lock (_lock) _images[key] = image;
        return image;
    }

    private void RememberMissing(string key)
    {
        lock (_lock)
            _negativeUntil[key] = DateTime.UtcNow + _negativeTtl;
    }

    private string CacheFilePath(string gameKeyValue)
    {
        // Dateiname aus einem Hash des gameKey – nie der rohe Schlüssel als Pfad.
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(gameKeyValue));
        var name = Convert.ToHexString(hash).ToLowerInvariant() + ".jpg";
        return Path.Combine(_paths.CoverCacheDirectory, name);
    }

    private static ImageSource? TryLoadImage(string file)
    {
        try
        {
            if (!File.Exists(file))
                return null;
            return TryDecode(File.ReadAllBytes(file));
        }
        catch
        {
            return null;
        }
    }

    private static ImageSource? TryDecode(byte[] bytes)
    {
        try
        {
            using var ms = new MemoryStream(bytes);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad; // Stream sofort auslesen, dann freigeben.
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze(); // thread-übergreifend nutzbar (Laden im Hintergrund, Anzeige im UI).
            return bmp;
        }
        catch
        {
            // Kaputte/kein-Bild-Bytes → kein Absturz, Fallback.
            return null;
        }
    }

    private void TrySaveToDisk(string file, byte[] bytes)
    {
        try
        {
            Directory.CreateDirectory(_paths.CoverCacheDirectory);
            var tmp = file + ".tmp-" + Guid.NewGuid().ToString("N");
            File.WriteAllBytes(tmp, bytes);
            if (File.Exists(file))
                File.Replace(tmp, file, destinationBackupFileName: null);
            else
                File.Move(tmp, file);
        }
        catch
        {
            // Cache ist verwerfbar – ein Schreibfehler darf die Anzeige nicht stören.
        }
    }
}
