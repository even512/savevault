using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using SaveVault.Core.Models;

namespace SaveVault.Core.Ludusavi;

/// <summary>
/// Dünner, sicherer Wrapper um die mitgelieferte ludusavi-Binary. Ruft sie als
/// Subprozess im <c>--api</c>-JSON-Modus auf (<c>find</c>, <c>backup --preview</c>).
///
/// Sicherheitsdesign (siehe Spec „Sicherheitsflächen"):
///   * FESTE Binary (Pfad injizierbar, Default <see cref="DefaultRelativePath"/>).
///   * Argumente ausschließlich über <see cref="ProcessStartInfo.ArgumentList"/> –
///     NIEMALS ein zusammengesetzter Shell-String; <c>UseShellExecute=false</c>.
///   * Timeout mit Prozess-Kill; stdout und stderr GETRENNT und gleichzeitig gelesen.
///   * Vor dem Parsen wird geprüft, ob stdout leer ist (dann Fehlerpfad statt Crash).
///   * Defensive Deserialisierung (unbekannte Felder werden ignoriert).
/// </summary>
public sealed class LudusaviClient
{
    /// <summary>Standard-Pfad zur mitgelieferten Binary (relativ zum Arbeitsverzeichnis).</summary>
    public const string DefaultRelativePath = "tools/ludusavi/ludusavi.exe";

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(120);

    private readonly string _exePath;

    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public LudusaviClient(string? exePath = null)
        => _exePath = string.IsNullOrWhiteSpace(exePath) ? DefaultRelativePath : exePath!;

    /// <summary>Der tatsächlich verwendete Binary-Pfad.</summary>
    public string ExecutablePath => _exePath;

    /// <summary>Ob die Binary am erwarteten Pfad vorhanden ist (für den „nicht eingerichtet"-Pfad).</summary>
    public bool IsAvailable => File.Exists(_exePath);

    /// <summary>Ruft <c>ludusavi --api find</c> auf und liefert die gefundenen Spiele.</summary>
    public async Task<LudusaviFindResult> FindAsync(CancellationToken ct = default, TimeSpan? timeout = null)
    {
        var stdout = await RunAsync(new[] { "--api", "find" }, timeout ?? DefaultTimeout, ct).ConfigureAwait(false);
        return Deserialize<LudusaviFindResult>(stdout);
    }

    /// <summary>
    /// Ruft <c>ludusavi --api backup --preview</c> auf (Preview = kein Schreiben).
    /// Ist ein Spiel angegeben, wird dessen Anzeigename als fester Positionsparameter
    /// über die ArgumentList übergeben – ohne jede Shell-Konkatenation.
    /// </summary>
    public async Task<LudusaviBackupPreview> BackupPreviewAsync(
        GameKey? game = null, CancellationToken ct = default, TimeSpan? timeout = null)
    {
        var args = new List<string> { "--api", "backup", "--preview" };
        if (game is not null)
        {
            // Options-Terminator vor dem Positionsparameter: ein mit '-'/'--' beginnender
            // DisplayName wird so garantiert als Wert (Spielname) und NIE als Option gelesen
            // (Argument-Injection-Härtung).
            args.Add("--");
            args.Add(game.DisplayName);
        }
        var stdout = await RunAsync(args, timeout ?? DefaultTimeout, ct).ConfigureAwait(false);
        return Deserialize<LudusaviBackupPreview>(stdout);
    }

    private T Deserialize<T>(string stdout) where T : class
    {
        // stdout wurde in RunAsync bereits auf Leere geprüft.
        try
        {
            var result = JsonSerializer.Deserialize<T>(stdout, _json);
            if (result is null)
                throw new LudusaviException("ludusavi lieferte ungültiges/leeres JSON.");
            return result;
        }
        catch (JsonException ex)
        {
            throw new LudusaviException(
                "ludusavi-JSON konnte nicht geparst werden (Schema am Laufzeit-Gate gegen echten Aufruf prüfen).", ex);
        }
    }

    private async Task<string> RunAsync(IReadOnlyList<string> args, TimeSpan timeout, CancellationToken ct)
    {
        // „nicht eingerichtet"-Pfad: fehlt die Binary, klar melden statt zu crashen.
        if (!File.Exists(_exePath))
            throw new LudusaviNotAvailableException(_exePath);

        var psi = new ProcessStartInfo
        {
            FileName = _exePath,
            UseShellExecute = false,        // NIE über die Shell
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        // Argumente strikt einzeln über ArgumentList – kein zusammengesetzter String.
        foreach (var a in args)
            psi.ArgumentList.Add(a);

        using var process = new Process { StartInfo = psi };

        try
        {
            if (!process.Start())
                throw new LudusaviException("ludusavi-Prozess konnte nicht gestartet werden.");
        }
        catch (Exception ex) when (ex is not LudusaviException)
        {
            throw new LudusaviException("ludusavi-Prozess konnte nicht gestartet werden.", ex);
        }

        // stdout und stderr GETRENNT und gleichzeitig lesen, um Puffer-Deadlocks zu vermeiden.
        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            TryKill(process);
            throw new LudusaviException($"ludusavi-Aufruf überschritt das Zeitlimit ({timeout.TotalSeconds:0}s).");
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        // WICHTIG: bei Fehler kann stdout leer sein → vor dem Parsen prüfen.
        if (string.IsNullOrWhiteSpace(stdout))
        {
            var detail = string.IsNullOrWhiteSpace(stderr) ? "(keine Fehlerausgabe)" : stderr.Trim();
            throw new LudusaviException($"ludusavi lieferte keine Ausgabe (Exit {process.ExitCode}): {detail}");
        }

        return stdout;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Best effort – ein bereits beendeter/nicht killbarer Prozess ist kein Folgefehler.
        }
    }
}
