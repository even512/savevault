using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;

namespace SaveVault.Client.Services;

/// <summary>Ausgang einer Update-Prüfung.</summary>
public enum UpdateCheckStatus
{
    /// <summary>Der laufende Client ist bereits so neu wie das jüngste Release.</summary>
    UpToDate,

    /// <summary>Ein neueres Release liegt vor (siehe <see cref="UpdateCheckResult.Available"/>).</summary>
    UpdateAvailable,

    /// <summary>Die Prüfung ist fehlgeschlagen (kein Netz, GitHub-Fehler …) – nie eine Exception nach außen.</summary>
    Failed,
}

/// <summary>
/// Ergebnis einer Update-Prüfung. Bei <see cref="UpdateCheckStatus.UpdateAvailable"/> tragen
/// <see cref="Available"/> und <see cref="DownloadUrl"/> die neue Version und die Asset-URL; bei
/// <see cref="UpdateCheckStatus.Failed"/> steht in <see cref="Error"/> eine kurze Begründung.
/// </summary>
public sealed record UpdateCheckResult(
    UpdateCheckStatus Status,
    Version? Available,
    string? DownloadUrl,
    string? Error);

/// <summary>
/// Der Selbst-Updater des Windows-Clients. Prüft das jüngste GitHub-Release von
/// <c>even512/savevault</c>, lädt bei Bedarf das self-contained-ZIP, entpackt es in einen
/// Staging-Ordner unter <c>%LocalAppData%\SaveVault\update</c> und tauscht die laufende
/// Installation im laufenden Betrieb aus: Die gestagte <c>SaveVault.Client.exe</c> wird mit
/// <c>--apply-update &lt;installDir&gt; &lt;pid&gt;</c> gestartet, die alte Instanz beendet sich, die
/// gestagte kopiert Staging → Installationsordner (mit kurzen Wiederholungen gegen transiente
/// Sperren) und startet die neue exe. Reine Client-/Windows-Logik ohne WPF – der Aufrufer
/// (App/Fenster) verantwortet das Beenden der laufenden App.
/// </summary>
public sealed class UpdateService
{
    // Öffentliches Repo → keine Anmeldung nötig. Fester Pfad = feste Vertrauensgrenze.
    private const string LatestReleaseApi = "https://api.github.com/repos/even512/savevault/releases/latest";

    // GitHub verlangt einen User-Agent; ohne ihn antwortet die API mit 403.
    private const string UserAgent = "SaveVault-Client-Updater";

    // Sonder-Argument, mit dem sich die gestagte exe selbst als „Applier" erkennt.
    public const string ApplyUpdateSwitch = "--apply-update";

    // Ein gemeinsamer HttpClient (UA gesetzt, großzügiger Timeout für den ~50-MB-Download).
    private static readonly HttpClient Http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SaveVault-Client-Updater", "1.0"));
        return client;
    }

    /// <summary>Die normalisierte Version des laufenden Clients (Major.Minor.Build).</summary>
    public static Version CurrentVersion { get; } = Normalize(ParseVersion(DeviceIdentity.AgentVersion) ?? new Version(0, 0, 0));

    /// <summary>Wurzel aller Update-Dateien: <c>%LocalAppData%\SaveVault\update</c>.</summary>
    public static string UpdateRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SaveVault", "update");

    private static string StagingDir => Path.Combine(UpdateRoot, "staging");
    private static string DownloadFile => Path.Combine(UpdateRoot, "download.zip");

    // --- Prüfen --------------------------------------------------------------------

    /// <summary>
    /// Fragt das jüngste Release ab und vergleicht es mit der laufenden Version. Wirft nie –
    /// jeder Fehler landet als <see cref="UpdateCheckStatus.Failed"/> im Ergebnis.
    /// </summary>
    public async Task<UpdateCheckResult> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            using var response = await Http.GetAsync(LatestReleaseApi, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return Failed($"GitHub antwortete mit {(int)response.StatusCode}.");

            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            var root = doc.RootElement;

            // Draft/Pre-Release ignorieren (releases/latest liefert sie zwar ohnehin nicht, aber sicher ist sicher).
            if (root.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True)
                return Failed("Nur ein Entwurf-Release vorhanden.");
            if (root.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True)
                return Failed("Nur ein Pre-Release vorhanden.");

            if (!root.TryGetProperty("tag_name", out var tagEl) || tagEl.ValueKind != JsonValueKind.String)
                return Failed("Release ohne Versions-Tag.");

            var latest = ParseVersion(tagEl.GetString());
            if (latest is null)
                return Failed("Versions-Tag nicht lesbar.");
            latest = Normalize(latest);

            var url = FindClientAssetUrl(root);
            if (url is null)
                return Failed("Passendes Client-Asset (win-x64.zip) nicht gefunden.");

            return latest > CurrentVersion
                ? new UpdateCheckResult(UpdateCheckStatus.UpdateAvailable, latest, url, null)
                : new UpdateCheckResult(UpdateCheckStatus.UpToDate, latest, null, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Failed(ex.Message);
        }
    }

    /// <summary>Sucht in den Release-Assets die self-contained-Client-ZIP (<c>SaveVault-Client-…win-x64.zip</c>).</summary>
    private static string? FindClientAssetUrl(JsonElement root)
    {
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var asset in assets.EnumerateArray())
        {
            if (!asset.TryGetProperty("name", out var nameEl) || nameEl.ValueKind != JsonValueKind.String)
                continue;
            var name = nameEl.GetString();
            if (string.IsNullOrEmpty(name)
                || !name.StartsWith("SaveVault-Client", StringComparison.OrdinalIgnoreCase)
                || !name.EndsWith("win-x64.zip", StringComparison.OrdinalIgnoreCase))
                continue;
            if (asset.TryGetProperty("browser_download_url", out var urlEl) && urlEl.ValueKind == JsonValueKind.String)
                return urlEl.GetString();
        }
        return null;
    }

    // --- Herunterladen & Staging ---------------------------------------------------

    /// <summary>
    /// Lädt das Release-ZIP und entpackt es in den (zuvor geleerten) Staging-Ordner. Liefert den
    /// Pfad der gestagten <c>SaveVault.Client.exe</c>. Wirft bei Netz-/ZIP-Fehlern oder wenn die
    /// erwartete exe im Paket fehlt.
    /// </summary>
    public async Task<string> DownloadAndStageAsync(string downloadUrl, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(downloadUrl);

        Directory.CreateDirectory(UpdateRoot);
        TryDeleteDirectory(StagingDir);
        TryDeleteFile(DownloadFile);

        // ZIP herunterladen (gestreamt, kein Vollpuffer im Speicher).
        using (var response = await Http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();
            await using var src = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var dst = new FileStream(DownloadFile, FileMode.Create, FileAccess.Write, FileShare.None);
            await src.CopyToAsync(dst, ct).ConfigureAwait(false);
        }

        Directory.CreateDirectory(StagingDir);
        ZipFile.ExtractToDirectory(DownloadFile, StagingDir, overwriteFiles: true);
        TryDeleteFile(DownloadFile);

        var stagedExe = Path.Combine(StagingDir, "SaveVault.Client.exe");
        if (!File.Exists(stagedExe))
            throw new FileNotFoundException("Das entpackte Update enthält keine SaveVault.Client.exe.", stagedExe);
        return stagedExe;
    }

    // --- Anwenden (im laufenden Betrieb) -------------------------------------------

    /// <summary>Verzeichnis der laufenden Installation (Ordner der eigenen exe).</summary>
    public static string CurrentInstallDir
        => Path.GetDirectoryName(Environment.ProcessPath ?? AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))
           ?? AppContext.BaseDirectory;

    /// <summary>
    /// Startet die gestagte exe im Applier-Modus (sie tauscht danach die Installation aus) und
    /// gibt zurück, ob der Start gelang. Der Aufrufer beendet anschließend die laufende App, damit
    /// die Dateien im Installationsordner frei werden.
    /// </summary>
    public bool StartApplier(string stagedExe)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stagedExe);

        var psi = new ProcessStartInfo
        {
            FileName = stagedExe,
            UseShellExecute = false,
            WorkingDirectory = StagingDir,
        };
        psi.ArgumentList.Add(ApplyUpdateSwitch);
        psi.ArgumentList.Add(CurrentInstallDir);
        psi.ArgumentList.Add(Environment.ProcessId.ToString());

        var proc = Process.Start(psi);
        return proc is not null;
    }

    /// <summary>
    /// Der Applier-Zweig: läuft in der gestagten exe (aus dem Staging-Ordner). Wartet auf das Ende
    /// der alten Instanz, kopiert Staging → Installationsordner und startet die aktualisierte exe.
    /// Schlägt das Kopieren endgültig fehl, wird die vorhandene (alte) exe wieder gestartet – kein
    /// toter Zustand. Reiner Konsolen-/Prozess-Pfad, kein WPF; der Aufrufer beendet danach.
    /// </summary>
    public static void RunApplier(string installDir, int oldPid)
    {
        // 1) Auf das Ende der alten Instanz warten (deren exe/DLLs sind sonst gesperrt).
        WaitForProcessExit(oldPid, TimeSpan.FromSeconds(60));

        var sourceDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var targetExe = Path.Combine(installDir, "SaveVault.Client.exe");

        try
        {
            // 2) Staging → Installationsordner transaktional austauschen: jede zu überschreibende Datei
            //    wird zuvor zur Seite geschoben; scheitert das Kopieren, wird auf den letzten guten Stand
            //    zurückgerollt. So bleibt der Installationsordner nie in einem gemischten Halbzustand.
            ApplyStagedFiles(sourceDir, installDir);
        }
        catch
        {
            // Fehlgeschlagen: ApplyStagedFiles hat bereits zurückgerollt. Bewusst kein Rethrow –
            //  der Applier soll sauber enden; die (wiederhergestellte) alte exe wird unten gestartet.
        }

        // 3) Die exe im Installationsordner starten – bei Erfolg die neue, nach Rollback die alte
        //    (kein toter Zustand ohne laufenden Client).
        TryStart(targetExe);
    }

    /// <summary>
    /// Räumt ein zurückgebliebenes Staging-/Download-Verzeichnis auf (best-effort) und meldet, ob das
    /// Staging danach weg ist. Direkt nach einem Update kann der noch beendende Applier seine eigene
    /// exe im Staging kurz sperren – dann kommt <c>false</c> zurück und der Aufrufer wiederholt es.
    /// </summary>
    public static bool CleanupStaging()
    {
        TryDeleteFile(DownloadFile);
        TryDeleteDirectory(StagingDir);
        return !Directory.Exists(StagingDir);
    }

    // --- interne Helfer ------------------------------------------------------------

    private static void WaitForProcessExit(int pid, TimeSpan timeout)
    {
        try
        {
            using var proc = Process.GetProcessById(pid);
            proc.WaitForExit((int)timeout.TotalMilliseconds);
        }
        catch (ArgumentException)
        {
            // Prozess existiert nicht (mehr) → bereits beendet, nichts zu tun.
        }
        catch
        {
            // Jeder andere Fehler: nicht blockieren – das Kopieren mit Retries fängt kurze Restsperren ab.
        }
    }

    /// <summary>
    /// Tauscht die Dateien aus <paramref name="sourceDir"/> (Staging) transaktional in
    /// <paramref name="targetDir"/> (Installation) ein: Jede vorhandene Zieldatei wird zuvor per
    /// atomarem Rename (<c>.svold</c>, gleicher Ordner) zur Seite geschoben, dann die neue kopiert.
    /// Schlägt ein Kopiervorgang trotz Wiederholungen fehl, wird der komplette Vorgang zurückgerollt
    /// (neu angelegte Dateien entfernt, verschobene zurückgeholt) und die Ausnahme weitergereicht –
    /// der Installationsordner bleibt so auf dem letzten guten Stand.
    /// </summary>
    private static void ApplyStagedFiles(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);

        // Zielordner-Struktur anlegen (leere neue Unterordner sind bei einem Rollback unschädlich).
        foreach (var dir in Directory.EnumerateDirectories(sourceDir, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(targetDir, Path.GetRelativePath(sourceDir, dir)));

        var backups = new List<(string Dest, string Bak)>();  // vorhandene Dateien, zur Seite geschoben
        var created = new List<string>();                      // neu angelegte Dateien (kein Vorgänger)

        try
        {
            foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                var dest = Path.Combine(targetDir, Path.GetRelativePath(sourceDir, file));
                if (File.Exists(dest))
                {
                    var bak = dest + ".svold";
                    TryDeleteFile(bak);        // Rest eines früheren Abbruchs entfernen
                    File.Move(dest, bak);      // atomar im selben Ordner
                    backups.Add((dest, bak));
                }
                else
                {
                    created.Add(dest);
                }
                CopyFileWithRetries(file, dest);  // dest existiert jetzt nicht mehr → sauberer Kopiervorgang
            }
        }
        catch
        {
            // Rollback auf den letzten guten Stand.
            foreach (var dest in created)
                TryDeleteFile(dest);
            foreach (var (dest, bak) in backups)
            {
                try { TryDeleteFile(dest); File.Move(bak, dest); }
                catch { /* best-effort – so viel wie möglich wiederherstellen */ }
            }
            throw;
        }

        // Erfolg: die zur Seite geschobenen Alt-Dateien entfernen.
        foreach (var (_, bak) in backups)
            TryDeleteFile(bak);
    }

    private static void CopyFileWithRetries(string source, string dest, int attempts = 5)
    {
        for (var i = 1; ; i++)
        {
            try
            {
                File.Copy(source, dest, overwrite: true);
                return;
            }
            catch (IOException) when (i < attempts)
            {
                // Transiente Sperre (AV-Scanner, gerade erst beendeter Prozess): kurz warten und erneut.
                Thread.Sleep(300);
            }
            catch (UnauthorizedAccessException) when (i < attempts)
            {
                Thread.Sleep(300);
            }
        }
    }

    private static void TryStart(string exePath)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? Environment.CurrentDirectory,
            });
        }
        catch
        {
            // Wenn selbst der Neustart scheitert, bleibt nur, sauber zu enden – der Nutzer startet manuell.
        }
    }

    private static void TryDeleteDirectory(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch { /* best-effort */ }
    }

    private static void TryDeleteFile(string file)
    {
        try { if (File.Exists(file)) File.Delete(file); }
        catch { /* best-effort */ }
    }

    private static UpdateCheckResult Failed(string reason)
        => new(UpdateCheckStatus.Failed, null, null, reason);

    /// <summary>Liest eine Version aus einem Tag (<c>v1.6.0</c>) oder Roh-String; <c>null</c> bei Unlesbarem.</summary>
    private static Version? ParseVersion(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        var text = raw.Trim();
        if (text.StartsWith('v') || text.StartsWith('V'))
            text = text[1..];
        return Version.TryParse(text, out var v) ? v : null;
    }

    /// <summary>Normalisiert auf Major.Minor.Build (Build/Revision unbestimmt → 0), damit Vergleiche stabil sind.</summary>
    private static Version Normalize(Version v)
        => new(v.Major, v.Minor, v.Build < 0 ? 0 : v.Build);
}
