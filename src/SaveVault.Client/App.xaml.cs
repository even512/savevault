using System.ComponentModel;
using System.Windows;
using SaveVault.Client.Services;
using SaveVault.Client.Ui;
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

        // Hintergrunddienst starten (nicht eingerichtet → Ruhezustand, kein Fehler).
        _ = StartAgentAsync();
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

    protected override async void OnExit(ExitEventArgs e)
    {
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
