using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using SaveVault.Client.Services;
using SaveVault.Client.Ui;
using SaveVault.Core.Models;

namespace SaveVault.Client;

/// <summary>
/// Das Status-Fenster des Tray-Clients. Zeigt Verbindungszustand und je-Spiel-Status aus der
/// beobachtbaren <see cref="AgentState"/> und ruft die Aktionen des <see cref="ClientAgent"/>
/// auf. Alle Zustandsänderungen kommen aus Hintergrund-Threads und werden über den
/// <see cref="System.Windows.Threading.Dispatcher"/> in den UI-Thread gebracht.
/// Schließen versteckt das Fenster (Tray bleibt), statt die App zu beenden.
/// </summary>
public partial class MainWindow : Window
{
    private readonly ClientAgent _agent;
    private readonly ObservableCollection<GameRow> _games = new();
    private readonly Dictionary<string, GameRow> _rows = new(StringComparer.Ordinal);

    public MainWindow(ClientAgent agent)
    {
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
        InitializeComponent();

        GamesList.ItemsSource = _games;
        _agent.State.Changed += OnAgentStateChanged;
        Refresh();
    }

    // --- Zustands-Anbindung --------------------------------------------------------

    private void OnAgentStateChanged(object? sender, EventArgs e)
    {
        // Event kommt aus Hintergrund-Threads → in den UI-Thread marshallen.
        Dispatcher.BeginInvoke(new Action(Refresh));
    }

    private void Refresh()
    {
        var state = _agent.State;

        UpdateConnection(state);
        ReconcileGames(state.SnapshotGames());

        var needEmpty = !state.IsConfigured || _games.Count == 0;
        EmptyPanel.Visibility = needEmpty ? Visibility.Visible : Visibility.Collapsed;
        GamesScroll.Visibility = needEmpty ? Visibility.Collapsed : Visibility.Visible;

        if (!state.IsConfigured)
        {
            EmptyTitle.Text = "Noch nicht eingerichtet";
            EmptyText.Text = "Verbinde diesen Client mit deinem SaveVault-Server, um Spielstände zu synchronisieren.";
            SetupButton.Content = "Einrichten";
            SetupButton.Visibility = Visibility.Visible;
        }
        else if (_games.Count == 0)
        {
            EmptyTitle.Text = "Keine Spiele";
            EmptyText.Text = "Es wurden noch keine Spiele erkannt. Nutze »Spiele neu erkennen« oder füge manuell einen Save-Ordner hinzu.";
            SetupButton.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateConnection(AgentState state)
    {
        if (!state.IsConfigured)
        {
            ConnDot.Fill = StatusVisuals.Offline;
            ConnText.Text = "Nicht eingerichtet";
            ConnSubText.Text = "Kein Server verbunden. Öffne die Einstellungen zum Koppeln.";
            SubtitleText.Text = "Spielstände-Synchronisation";
            return;
        }

        if (state.ServerReachable)
        {
            ConnDot.Fill = StatusVisuals.Synced;
            ConnText.Text = "Verbunden";
            var seen = state.LastServerContactUtc is null
                ? ""
                : $" · zuletzt {RelativeTime.Format(state.LastServerContactUtc)}";
            ConnSubText.Text = "Server erreichbar" + seen;
        }
        else
        {
            ConnDot.Fill = StatusVisuals.Error;
            ConnText.Text = "Server nicht erreichbar";
            ConnSubText.Text = string.IsNullOrWhiteSpace(state.LastError)
                ? "Verbindung zum Server unterbrochen."
                : state.LastError!;
        }

        var name = _agent.CurrentDeviceName;
        SubtitleText.Text = string.IsNullOrWhiteSpace(name)
            ? "Spielstände-Synchronisation"
            : $"Dieses Gerät: {name}";
    }

    private void ReconcileGames(IReadOnlyList<GameStatusView> snapshot)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var view in snapshot)
        {
            var key = view.Game.Value;
            seen.Add(key);
            if (_rows.TryGetValue(key, out var row))
            {
                row.Update(view);
            }
            else
            {
                row = new GameRow(view.Game);
                row.Update(view);
                _rows[key] = row;
                _games.Add(row);
            }
        }

        // Verschwundene Einträge entfernen.
        var gone = _rows.Keys.Where(k => !seen.Contains(k)).ToList();
        foreach (var key in gone)
        {
            if (_rows.TryGetValue(key, out var row))
                _games.Remove(row);
            _rows.Remove(key);
        }
    }

    // --- Aktionen ------------------------------------------------------------------

    private async void OnSyncNowClick(object sender, RoutedEventArgs e)
    {
        await RunGuarded((Button)sender, () => _agent.SyncNowAsync());
    }

    private async void OnRediscoverClick(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        DiscoveryResult? result = null;
        await RunGuarded(button, async () => { result = await _agent.RefreshDiscoveryAsync(); });

        if (result is null)
            return;

        if (!result.LudusaviAvailable)
        {
            Info("Spiele-Erkennung nicht verfügbar: ludusavi wurde nicht gefunden. Du kannst Save-Ordner manuell hinzufügen.");
        }
        else if (!string.IsNullOrWhiteSpace(result.Error))
        {
            Info("Erkennung mit Hinweis abgeschlossen: " + result.Error);
        }
        else
        {
            Info($"Erkennung abgeschlossen: {result.Games.Count} Spiel(e) gefunden.");
        }
    }

    private void OnAddFolderClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Save-Ordner auswählen",
            Multiselect = false,
        };

        if (dialog.ShowDialog(this) != true)
            return;

        var path = dialog.FolderName;
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            Info("Der gewählte Ordner ist ungültig.");
            return;
        }

        var name = new DirectoryInfo(path).Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            Info("Aus dem Ordner konnte kein Spielname abgeleitet werden.");
            return;
        }

        try
        {
            var game = GameKey.FromName(name);
            _agent.AddManualFolder(game, path);
            Info($"Ordner für »{name}« hinzugefügt.");
        }
        catch (Exception ex)
        {
            Info("Ordner konnte nicht hinzugefügt werden: " + ex.Message);
        }
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        var window = new SettingsWindow(_agent) { Owner = this };
        window.ShowDialog();
        Refresh();
    }

    private async void OnResolveConflictClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: GameRow row })
            return;

        var button = (Button)sender;
        button.IsEnabled = false;
        try
        {
            var conflicts = await _agent.GetConflictsAsync();
            var conflict = conflicts.FirstOrDefault(c => c.Game.Equals(row.Game) && !c.Resolved);
            if (conflict is null)
            {
                Info("Für dieses Spiel liegt aktuell kein offener Konflikt vor.");
                return;
            }

            var dialog = new ConflictWindow(_agent, conflict) { Owner = this };
            dialog.ShowDialog();
        }
        catch (Exception ex)
        {
            Info("Konflikt konnte nicht geladen werden: " + ex.Message);
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    /// <summary>Führt eine asynchrone Aktion aus, sperrt kurz den Button und meldet Fehler.</summary>
    private async Task RunGuarded(Button button, Func<Task> action)
    {
        button.IsEnabled = false;
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Info("Aktion fehlgeschlagen: " + ex.Message);
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private void Info(string message)
        => System.Windows.MessageBox.Show(this, message, "SaveVault", MessageBoxButton.OK, MessageBoxImage.Information);

    // --- Fensterverhalten ----------------------------------------------------------

    protected override void OnClosing(CancelEventArgs e)
    {
        // Nicht schließen, sondern in den Tray zurückziehen – die App läuft weiter.
        e.Cancel = true;
        Hide();
        base.OnClosing(e);
    }
}
