using System.Globalization;
using System.IO;
using System.Text;
using SaveVault.Core.Api;
using SaveVault.Core.Models;
using SaveVault.Core.Sync;

namespace SaveVault.Client.Services;

/// <summary>
/// Dauerhaftes, rollierendes Diagnose-Log der Sync-Entscheidungen (siehe
/// <c>specs/savevault-change-sync-anzeige-fixes.md</c>, Fix 0, Schritt 2): pro abgeschlossenem
/// <see cref="SyncEngine.RunCycleAsync"/>-Zyklus eine Zeile mit Zeitpunkt, Spiel, Scope, gewählter
/// <see cref="SyncAction"/> und lokaler/Basis-/Server-Revisionsstand. Zweck: beim nächsten
/// unerwarteten Konflikt lässt sich anhand des Logs exakt rekonstruieren, welche Revisionsnummern
/// zum Zeitpunkt der Entscheidung vorlagen, statt erneut zu rätseln.
///
/// <para>Reine Beobachtung, <b>keine Verhaltensänderung</b>: jeder Schreib-/Rotationsfehler wird
/// verschluckt (Logging darf den Sync nie zum Absturz bringen oder verzögern). Rolliert bei
/// Überschreiten von <see cref="MaxBytes"/>: die aktuelle Datei wird zu <c>sync.log.1</c>
/// verschoben (überschreibt eine ältere Sicherung), danach beginnt eine frische Datei – der
/// Speicherbedarf bleibt so auf maximal <c>2 × MaxBytes</c> begrenzt.</para>
///
/// <para>Da ein lokaler Stand vor dem ersten Hochladen keine Revisionsnummer hat, trägt die Zeile
/// für „lokal" die ersten 8 Zeichen des <see cref="FileManifest.ManifestHash"/> (stabiler
/// Kurz-Fingerabdruck des gescannten Ordnerinhalts) statt einer Zahl.</para>
/// </summary>
public sealed class SyncDiagnosticsLog
{
    private const long MaxBytes = 2 * 1024 * 1024; // 2 MB je Datei, 1 Sicherung -> max. 4 MB gesamt
    private readonly object _lock = new();
    private readonly string _path;

    public SyncDiagnosticsLog(AppPaths paths)
        => _path = (paths ?? throw new ArgumentNullException(nameof(paths))).SyncLogFile;

    /// <summary>
    /// Hängt eine Zeile für einen abgeschlossenen Sync-Zyklus an (best effort, wirft nie).
    /// </summary>
    public void Append(
        GameKey game,
        BucketScope scope,
        SyncAction action,
        string reason,
        string localManifestHash,
        long baseRevision,
        long serverRevision,
        DateTime timestampUtc)
    {
        ArgumentNullException.ThrowIfNull(game);
        try
        {
            var line =
                $"{timestampUtc.ToString("o", CultureInfo.InvariantCulture)}\t" +
                $"{game.DisplayName}\t" +
                $"{BucketKey.ToWire(scope)}\t" +
                $"{action}\t" +
                $"local={ShortHash(localManifestHash)}\t" +
                $"base={baseRevision.ToString(CultureInfo.InvariantCulture)}\t" +
                $"server={serverRevision.ToString(CultureInfo.InvariantCulture)}\t" +
                $"{reason}" +
                Environment.NewLine;

            lock (_lock)
            {
                RotateIfNeededLocked();
                var dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                File.AppendAllText(_path, line, Encoding.UTF8);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Diagnose-Log ist reine Beobachtung - ein Schreibfehler darf den Sync nie stoeren.
        }
    }

    /// <summary>
    /// Hängt eine zweite Zeile an, die das TATSÄCHLICHE Ergebnis eines Zyklus festhält (siehe
    /// <c>specs/savevault-change-sync-anzeige-fixes.md</c>, Nachtrag „Diagnose-Log erfasst nur die
    /// Entscheidung, nicht das Ergebnis"): <see cref="Append"/> wird VOR der Ausführung der Aktion
    /// aufgerufen und beweist daher nur die Absicht, nicht den Erfolg. Diese Methode wird NACH dem
    /// Ausführungsversuch aufgerufen — bei Erfolg mit der resultierenden Basis-Revision (zeigt, ob
    /// sie wirklich vorgerückt ist), bei einem Fehler mit Ausnahme-Typ und -Nachricht.
    /// </summary>
    public void AppendOutcome(
        GameKey game,
        BucketScope scope,
        SyncAction decidedAction,
        bool success,
        string detail,
        long resultingBaseRevision,
        DateTime timestampUtc)
    {
        ArgumentNullException.ThrowIfNull(game);
        try
        {
            var line =
                $"{timestampUtc.ToString("o", CultureInfo.InvariantCulture)}\t" +
                $"{game.DisplayName}\t" +
                $"{BucketKey.ToWire(scope)}\t" +
                $"ERGEBNIS({decidedAction})\t" +
                $"{(success ? "OK" : "FEHLER")}\t" +
                $"neueBasis={resultingBaseRevision.ToString(CultureInfo.InvariantCulture)}\t" +
                $"{detail}" +
                Environment.NewLine;

            lock (_lock)
            {
                RotateIfNeededLocked();
                var dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                File.AppendAllText(_path, line, Encoding.UTF8);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Diagnose-Log ist reine Beobachtung - ein Schreibfehler darf den Sync nie stoeren.
        }
    }

    private void RotateIfNeededLocked()
    {
        try
        {
            var info = new FileInfo(_path);
            if (!info.Exists || info.Length < MaxBytes)
                return;
            var backup = _path + ".1";
            File.Copy(_path, backup, overwrite: true);
            File.Delete(_path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Rotation ist best effort - im schlimmsten Fall waechst die Datei etwas ueber das Limit.
        }
    }

    private static string ShortHash(string? hash)
        => string.IsNullOrEmpty(hash) ? "-" : hash[..Math.Min(8, hash.Length)];
}
