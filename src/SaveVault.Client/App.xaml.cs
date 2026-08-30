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

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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

        // Schalter frisch lesen, damit die Einstellung ohne Neustart wirkt.
        bool enabled;
        try { enabled = new ClientConfigStore(new AppPaths()).Load().ToastsEnabled; }
        catch { enabled = true; }
        if (!enabled)
            return;

        var (text, isConflict) = ComposeToast(batch);
        if (string.IsNullOrEmpty(text))
            return;

        // Ausgabe über das Tray-Icon auf dem UI-Thread (NotifyIcon wurde dort erzeugt).
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_tray is null || !_tray.Visible)
                return;
            var icon = isConflict ? WinForms.ToolTipIcon.Warning : WinForms.ToolTipIcon.Info;
            _tray.ShowBalloonTip(5000, "SaveVault", text, icon);
        }));
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
