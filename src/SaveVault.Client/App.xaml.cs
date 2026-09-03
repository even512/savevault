using System.ComponentModel;
using System.Threading;
using System.Windows;
using SaveVault.Client.Services;
using SaveVault.Client.Ui;
using SaveVault.Core.Models;
using WinForms = System.Windows.Forms;

namespace SaveVault.Client;

/// <summary>
/// App-Lebenszyklus des Tray-Clients. Startet den <see cref="ClientAgent"/> (den WPF-freien
/// Hintergrund aus Schritt 5), legt das Tray-Symbol an und zeigt das Status-Fenster nur auf
/// Anforderung – die App läuft ohne sichtbares Hauptfenster weiter (Shutdown nur explizit).
/// Alle langen Aktionen laufen asynchron; Fehler werden gemeldet, nie als Absturz.
/// </summary>
public partial class App : Application
{
    private ClientAgent? _agent;
    private MainWindow? _window;
    private WinForms.NotifyIcon? _tray;
    private System.Drawing.Icon? _trayIcon;

    // --- Toast-Bündelung -----------------------------------------------------------
    // Sync-Aktivität kommt aus Hintergrund-Threads. Wir sammeln thread-sicher und feuern
    // erst nach einem kurzen Ruhefenster (kein Massen-Spam mitten im Sync-Durchgang) genau
    // einen Sammel-Toast über das Tray-Icon.
    private static readonly TimeSpan ToastQuietWindow = TimeSpan.FromSeconds(2);
    private readonly object _toastLock = new();
    private readonly List<SyncActivity> _pendingActivities = new();
    private Timer? _toastTimer;
    private EventHandler<SyncActivity>? _activityHandler;

    // Höchstens ein laufendes Wasserzeichen-Overlay – kein Fenster-Stapel.
    private WatermarkWindow? _watermark;

    // Selbst-Update: 24-h-Prüftakt und die zuletzt per Tray gemeldete Version (kein Doppel-Hinweis).
    private System.Windows.Threading.DispatcherTimer? _updateTimer;
    private Version? _announcedUpdate;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Applier-Modus: Wird diese exe von der gestagten Kopie mit --apply-update gestartet, tauscht
        // sie nur die Installation aus (kopiert Staging → Installationsordner, startet die neue exe)
        // und beendet sich – ohne Tray/Agent hochzufahren. Muss ganz am Anfang stehen.
        if (e.Args.Length >= 3 && e.Args[0] == UpdateService.ApplyUpdateSwitch)
        {
            base.OnStartup(e);
            try
            {
                _ = int.TryParse(e.Args[2], out var oldPid);
                UpdateService.RunApplier(e.Args[1], oldPid);
            }
            finally
            {
                Shutdown();
            }
            return;
        }

        base.OnStartup(e);

        // Reste eines vorangegangenen Updates aufräumen – verzögert im Hintergrund und mit
        // Wiederholungen, weil direkt nach einem Update der noch beendende Applier seine exe im
        // Staging kurz sperrt. Blockiert den Start nie.
        _ = CleanupStagingLaterAsync();

        DispatcherUnhandledException += (_, args) =>
        {
            // Letzte Rettung: unerwartete UI-Ausnahmen melden statt die App zu killen.
            System.Windows.MessageBox.Show(
                "Ein unerwarteter Fehler ist aufgetreten:\n\n" + args.Exception.Message,
                "SaveVault", MessageBoxButton.OK, MessageBoxImage.Warning);
            args.Handled = true;
        };

        _agent = new ClientAgent();
        _window = new MainWindow(_agent);

        CreateTray();

        // Sync-Aktivität → gebündelte Tray-Toasts (abschaltbar über die Einstellungen).
        _toastTimer = new Timer(_ => FlushToasts(), null, Timeout.Infinite, Timeout.Infinite);
        _activityHandler = OnSyncActivity;
        _agent.State.SyncActivityOccurred += _activityHandler;

        // Registry-Autostart an die Config angleichen (best-effort): „Standard AN" greift
        // so schon beim ersten Lauf ohne Öffnen der Einstellungen, ein verschobener exe-Pfad
        // wird korrigiert. Ein Fehler darf den Start nie abbrechen.
        SyncAutostart();

        // Hintergrunddienst starten (nicht eingerichtet → Ruhezustand, kein Fehler).
        _ = StartAgentAsync();

        // Selbst-Update-Prüfung planen (Start + täglich) – blockiert den Start nicht.
        SetupUpdateChecks();
    }

    // --- Selbst-Update -------------------------------------------------------------

    /// <summary>Räumt zurückgebliebenes Update-Staging auf – mehrere Versuche mit Pause, best-effort.</summary>
    private static async Task CleanupStagingLaterAsync()
    {
        for (var i = 0; i < 6; i++)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(2)); } catch { return; }
            try { if (UpdateService.CleanupStaging()) return; } catch { /* nächster Versuch */ }
        }
    }

    /// <summary>Richtet den 24-h-Prüftakt ein und stößt die (gedämpfte) Startprüfung an.</summary>
    private void SetupUpdateChecks()
    {
        _updateTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromHours(24) };
        _updateTimer.Tick += (_, _) => _ = RunAutoUpdateCheckAsync();
        _updateTimer.Start();

        _ = DelayThenStartupCheckAsync();
    }

    /// <summary>Startprüfung: kurz verzögert (freier Start) und nur, wenn seit der letzten Prüfung ~20 h um sind.</summary>
    private async Task DelayThenStartupCheckAsync()
    {
        try { await Task.Delay(TimeSpan.FromSeconds(8)); } catch { return; }

        ClientConfig config;
        try { config = new ClientConfigStore(new AppPaths()).Load(); } catch { return; }
        if (!config.AutoUpdateCheckEnabled)
            return;
        if (config.LastUpdateCheckUtc is { } last && DateTime.UtcNow - last < TimeSpan.FromHours(20))
            return;

        await RunAutoUpdateCheckAsync();
    }

    /// <summary>Führt eine selbsttätige Prüfung aus und meldet einen Fund einmalig per Tray-Hinweis.</summary>
    private async Task RunAutoUpdateCheckAsync()
    {
        if (_window is null)
            return;
        try
        {
            if (!new ClientConfigStore(new AppPaths()).Load().AutoUpdateCheckEnabled)
                return;
        }
        catch { /* im Zweifel prüfen */ }

        UpdateCheckResult result;
        try { result = await _window.CheckForUpdatesAsync(userInitiated: false); }
        catch { return; }

        if (result.Status == UpdateCheckStatus.UpdateAvailable && result.Available is not null
            && !result.Available.Equals(_announcedUpdate))
        {
            _announcedUpdate = result.Available;
            if (_tray is not null && _tray.Visible)
                _tray.ShowBalloonTip(6000, "SaveVault",
                    $"Neue Version {result.Available} verfügbar. Fenster öffnen, um zu aktualisieren.",
                    WinForms.ToolTipIcon.Info);
        }
    }

    private static void SyncAutostart()
    {
        try
        {
            var config = new ClientConfigStore(new AppPaths()).Load();
            AutostartService.Apply(config.AutostartEnabled);
        }
        catch
        {
            // Best-effort: jeder Fehler beim Abgleich wird verschluckt, der Start läuft weiter.
        }
    }

    private async Task StartAgentAsync()
    {
        try
        {
            await _agent!.StartAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _agent!.State.MarkServerUnreachable("Start fehlgeschlagen: " + ex.Message);
        }
    }

    private void CreateTray()
    {
        _trayIcon = TrayIconFactory.Create();

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Öffnen", null, (_, _) => ShowMainWindow());
        menu.Items.Add("Jetzt synchronisieren", null, (_, _) => SyncNow());
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Beenden", null, (_, _) => ShutdownApp());

        _tray = new WinForms.NotifyIcon
        {
            Icon = _trayIcon,
            Visible = true,
            Text = "SaveVault",
            ContextMenuStrip = menu,
        };
        _tray.DoubleClick += (_, _) => ShowMainWindow();
    }

    private void ShowMainWindow()
    {
        if (_window is null)
            return;

        if (!_window.IsVisible)
            _window.Show();

        if (_window.WindowState == WindowState.Minimized)
            _window.WindowState = WindowState.Normal;

        _window.Activate();
        _window.Topmost = true;
        _window.Topmost = false;
        _window.Focus();
    }

    private void SyncNow()
    {
        _ = SyncNowAsync();
    }

    private async Task SyncNowAsync()
    {
        if (_agent is null)
            return;
        try
        {
            await _agent.SyncNowAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Fehlerdetails landen bereits im AgentState; hier bewusst still.
        }
    }

    private void ShutdownApp()
    {
        Shutdown();
    }

    // --- Toast-Ausgabe -------------------------------------------------------------

    /// <summary>Sammelt ein Sync-Ereignis (Hintergrund-Thread) und stößt das Ruhefenster neu an.</summary>
    private void OnSyncActivity(object? sender, SyncActivity activity)
    {
        lock (_toastLock)
        {
            _pendingActivities.Add(activity);
            // Timer bei jedem Ereignis neu setzen → erst ~2 s nach dem LETZTEN Ereignis feuern.
            _toastTimer?.Change(ToastQuietWindow, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>Gibt einen Sammel-Toast für die gepufferten Ereignisse aus (nach dem Ruhefenster).</summary>
    private void FlushToasts()
    {
        List<SyncActivity> batch;
        lock (_toastLock)
        {
            if (_pendingActivities.Count == 0)
                return;
            batch = new List<SyncActivity>(_pendingActivities);
            _pendingActivities.Clear();
        }

        // Config frisch lesen, damit alle Einstellungen ohne Neustart wirken.
        ClientConfig config;
        try { config = new ClientConfigStore(new AppPaths()).Load(); }
        catch { config = new ClientConfig(); }

        // (2) Master: „Benachrichtigungen anzeigen" aus ⇒ weder Toast noch Wasserzeichen.
        if (!config.ToastsEnabled)
            return;

        // (3) Kategorie-Filter: nur Ereignisse behalten, deren Kategorie aktiv ist.
        //     Übertragungen (Uploaded/Downloaded) nur bei NotifyTransfers, Konflikte nur bei
        //     NotifyConflicts. Bleibt nichts übrig ⇒ nichts.
        var filtered = batch.Where(a => a.Kind switch
        {
            SyncActivityKind.Uploaded or SyncActivityKind.Downloaded => config.NotifyTransfers,
            SyncActivityKind.Conflict => config.NotifyConflicts,
            _ => false,
        }).ToList();
        if (filtered.Count == 0)
            return;

        // (4) Vollbild-Weiche: läuft ein Spiel, unterbleibt der laute Toast.
        if (!FullscreenDetection.IsFullscreenAppRunning())
        {
            // Kein Spiel: Toast wie gewohnt – bei „ohne Ton" lautlos (ToolTipIcon.None).
            var (text, isConflict) = ComposeToast(filtered);
            if (string.IsNullOrEmpty(text))
                return;

            var icon = !config.NotificationSound
                ? WinForms.ToolTipIcon.None
                : (isConflict ? WinForms.ToolTipIcon.Warning : WinForms.ToolTipIcon.Info);

            // Ausgabe über das Tray-Icon auf dem UI-Thread (NotifyIcon wurde dort erzeugt).
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_tray is null || !_tray.Visible)
                    return;
                _tray.ShowBalloonTip(5000, "SaveVault", text, icon);
            }));
        }
        else
        {
            // Spiel läuft: kein Toast, kein Ton. Nur (optional) das Wasserzeichen.
            if (!config.GameWatermarkEnabled)
                return;

            // Label aus festem Vokabular: Konflikt hat Vorrang, sonst „gesichert".
            var hasConflict = filtered.Any(a => a.Kind == SyncActivityKind.Conflict);
            var label = hasConflict ? "Konflikt" : "gesichert";
            var corner = config.WatermarkCorner;

            Dispatcher.BeginInvoke(new Action(() => ShowWatermark(label, corner)));
        }
    }

    /// <summary>
    /// Zeigt das Wasserzeichen-Overlay (UI-Thread). Ein evtl. noch offenes wird zuvor geschlossen,
    /// damit nie ein Fenster-Stapel entsteht. Fehler bleiben lokal – das Overlay darf die App
    /// nie stören.
    /// </summary>
    private void ShowWatermark(string label, WatermarkCorner corner)
    {
        try
        {
            _watermark?.Close();
            _watermark = null;

            var window = new WatermarkWindow(label, corner);
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(_watermark, window))
                    _watermark = null;
            };
            _watermark = window;
            window.Show();
        }
        catch
        {
            // Das Overlay ist rein kosmetisch: jeder Fehler wird verschluckt.
        }
    }

    /// <summary>
    /// Baut den Toast-Text aus den gebündelten Ereignissen: Übertragungen (gesichert/
    /// synchronisiert) als eine Zeile, Konflikte getrennt und deutlich. Liefert zusätzlich,
    /// ob (auch) ein Konflikt dabei war (steuert das Toast-Symbol).
    /// </summary>
    private static (string Text, bool IsConflict) ComposeToast(IReadOnlyList<SyncActivity> batch)
    {
        var lines = new List<string>();

        // Übertragungen: je Spiel die „stärkere" Art (Download = synchronisiert) merken.
        var transfers = new Dictionary<string, (string Name, bool Downloaded)>(StringComparer.Ordinal);
        foreach (var a in batch)
        {
            if (a.Kind is not (SyncActivityKind.Uploaded or SyncActivityKind.Downloaded))
                continue;
            var key = a.Game.Value;
            var downloaded = a.Kind == SyncActivityKind.Downloaded;
            if (transfers.TryGetValue(key, out var cur))
                transfers[key] = (cur.Name, cur.Downloaded || downloaded);
            else
                transfers[key] = (a.Game.DisplayName, downloaded);
        }

        if (transfers.Count == 1)
        {
            var t = transfers.Values.First();
            lines.Add(t.Downloaded ? $"»{t.Name}« synchronisiert" : $"»{t.Name}« gesichert");
        }
        else if (transfers.Count > 1)
        {
            var names = transfers.Values.Select(v => v.Name).ToList();
            lines.Add($"{transfers.Count} Spiele synchronisiert: {JoinNames(names)}");
        }

        // Konflikte: getrennt und deutlich.
        var conflicts = new List<string>();
        var conflictSeen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var a in batch)
        {
            if (a.Kind == SyncActivityKind.Conflict && conflictSeen.Add(a.Game.Value))
                conflicts.Add(a.Game.DisplayName);
        }

        var hasConflict = conflicts.Count > 0;
        if (conflicts.Count == 1)
            lines.Add($"Konflikt bei »{conflicts[0]}«");
        else if (conflicts.Count > 1)
            lines.Add($"Konflikt bei {conflicts.Count} Spielen: {JoinNames(conflicts)}");

        return (string.Join("\n", lines), hasConflict);
    }

    /// <summary>Fasst Namen knapp zusammen (max. 3, Rest als „+N weitere").</summary>
    private static string JoinNames(IReadOnlyList<string> names)
    {
        const int max = 3;
        if (names.Count <= max)
            return string.Join(", ", names);
        var shown = string.Join(", ", names.Take(max));
        return $"{shown} +{names.Count - max} weitere";
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        // Toast-Verdrahtung sauber lösen.
        if (_agent is not null && _activityHandler is not null)
            _agent.State.SyncActivityOccurred -= _activityHandler;
        _activityHandler = null;
        _toastTimer?.Dispose();
        _toastTimer = null;

        // Update-Prüftakt stoppen.
        _updateTimer?.Stop();
        _updateTimer = null;

        // Ein evtl. noch offenes Wasserzeichen sauber schließen (kein hängendes Fenster).
        try { _watermark?.Close(); }
        catch { /* Herunterfahren nie blockieren */ }
        _watermark = null;

        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
            _tray = null;
        }
        _trayIcon?.Dispose();
        _trayIcon = null;

        if (_agent is not null)
        {
            try { await _agent.DisposeAsync().ConfigureAwait(false); }
            catch { /* Herunterfahren nie blockieren */ }
            _agent = null;
        }

        base.OnExit(e);
    }
}
