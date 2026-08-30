using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using SaveVault.Client.Services;
using SaveVault.Client.Ui;
using SaveVault.Core.Models;

namespace SaveVault.Client;

/// <summary>
/// Das Status-Fenster des Tray-Clients. Zeigt Verbindungszustand, einen Gesamt-Status, einen
/// Aufmerksamkeits-Bereich, ein durchsuchbares Spiel-Dropdown und ein Detail-Panel (inkl.
/// Versionshistorie und Wiederherstellen) aus der beobachtbaren <see cref="AgentState"/> und
/// ruft die Aktionen des <see cref="ClientAgent"/> auf. Alle Zustandsänderungen kommen aus
/// Hintergrund-Threads und werden über den <see cref="System.Windows.Threading.Dispatcher"/>
/// in den UI-Thread gebracht. Die Spiel-Liste wird additiv abgeglichen, damit die
/// Dropdown-Auswahl bei Live-Updates erhalten bleibt. Schließen versteckt das Fenster.
/// </summary>
public partial class MainWindow : Window
{
    private readonly ClientAgent _agent;

    // Additiv abgeglichene Spiel-Liste (Quelle für Dropdown + Aufmerksamkeit).
    private readonly ObservableCollection<GameRow> _games = new();
    private readonly Dictionary<string, GameRow> _rows = new(StringComparer.Ordinal);
    private readonly ObservableCollection<GameRow> _attention = new();
    private readonly ObservableCollection<RevisionRow> _revisions = new();

    private GameRow? _selectedRow;
    private string? _selectedKey;
    private DateTime? _historyLoadedAction;   // LastActionUtc, zu dem die Historie geladen wurde

    public MainWindow(ClientAgent agent)
    {
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
        InitializeComponent();

        GameCombo.ItemsSource = _games;
        AttentionList.ItemsSource = _attention;
        HistoryList.ItemsSource = _revisions;

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
        UpdateOverview();
        UpdateAttention();

        var needEmpty = !state.IsConfigured || _games.Count == 0;

        OverallCard.Visibility = needEmpty ? Visibility.Collapsed : Visibility.Visible;
        GameCombo.Visibility = needEmpty ? Visibility.Collapsed : Visibility.Visible;
        EmptyPanel.Visibility = needEmpty ? Visibility.Visible : Visibility.Collapsed;

        if (needEmpty)
        {
            DetailScroll.Visibility = Visibility.Collapsed;
            _selectedRow = null;
            _selectedKey = null;

            if (!state.IsConfigured)
            {
                EmptyTitle.Text = "Noch nicht eingerichtet";
                EmptyText.Text = "Verbinde diesen Client mit deinem SaveVault-Server, um Spielstände zu synchronisieren.";
                SetupButton.Content = "Einrichten";
                SetupButton.Visibility = Visibility.Visible;
            }
            else
            {
                EmptyTitle.Text = "Keine Spiele";
                EmptyText.Text = "Es wurden noch keine Spiele erkannt. Nutze »Spiele neu erkennen« oder füge manuell einen Save-Ordner hinzu.";
                SetupButton.Visibility = Visibility.Collapsed;
            }
            return;
        }

        EnsureSelection();
        RefreshDetailIfNeeded();
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

    // --- Gesamt-Status -------------------------------------------------------------

    private void UpdateOverview()
    {
        TotalGamesText.Text = _games.Count == 1 ? "1 Spiel" : $"{_games.Count} Spiele";

        var byStatus = new Dictionary<SyncStatus, int>();
        var skipped = 0;
        DateTime? latest = null;

        foreach (var row in _games)
        {
            if (row.IsSkipped)
            {
                skipped++;
            }
            else
            {
                byStatus.TryGetValue(row.Status, out var n);
                byStatus[row.Status] = n + 1;
            }

            if (row.LastActionUtc is { } t && (latest is null || t > latest))
                latest = t;
        }

        CountersPanel.Children.Clear();
        // Feste, sinnvolle Reihenfolge der Zustände.
        AddCounter(byStatus, SyncStatus.Synced, "synchronisiert");
        AddCounter(byStatus, SyncStatus.Syncing, "wird synchronisiert");
        AddCounter(byStatus, SyncStatus.Conflict, "Konflikt");
        AddCounter(byStatus, SyncStatus.Pending, "ausstehend");
        AddCounter(byStatus, SyncStatus.Error, "Fehler");
        AddCounter(byStatus, SyncStatus.Offline, "offline");
        if (skipped > 0)
            AddCounterChip(StatusVisuals.Attention, skipped, "übersprungen");

        if (CountersPanel.Children.Count == 0)
            AddCounterChip(StatusVisuals.Offline, 0, "keine Zustände");

        LastSyncText.Text = latest is null
            ? "Zuletzt synchronisiert: —"
            : $"Zuletzt synchronisiert: {RelativeTime.Format(latest)}";
    }

    private void AddCounter(Dictionary<SyncStatus, int> byStatus, SyncStatus status, string label)
    {
        if (byStatus.TryGetValue(status, out var n) && n > 0)
            AddCounterChip(StatusVisuals.BrushFor(status), n, label);
    }

    private void AddCounterChip(Brush brush, int count, string label)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 14, 6),
        };
        panel.Children.Add(new Ellipse
        {
            Width = 9,
            Height = 9,
            Fill = brush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        });
        panel.Children.Add(new TextBlock
        {
            Text = $"{count} {label}",
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        });
        CountersPanel.Children.Add(panel);
    }

    // --- Aufmerksamkeits-Bereich ---------------------------------------------------

    private void UpdateAttention()
    {
        _attention.Clear();
        foreach (var row in _games)
        {
            if (row.NeedsAttention)
                _attention.Add(row);
        }
        AttentionCard.Visibility = _attention.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnAttentionClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: GameRow row })
            SelectGame(row);
    }

    // --- Auswahl & Detail ----------------------------------------------------------

    private void EnsureSelection()
    {
        // Auswahl noch gültig? (Objekt kann durch Reconcile entfernt worden sein.)
        if (_selectedKey is not null && _rows.ContainsKey(_selectedKey))
            return;

        // Sinnvoller Default: erstes Spiel mit Aufmerksamkeit, sonst das erste Spiel.
        var target = _attention.FirstOrDefault() ?? _games.FirstOrDefault();
        if (target is not null)
            SelectGame(target);
    }

    private void SelectGame(GameRow row)
    {
        // Auswahl im Dropdown setzen (löst OnGameSelectionChanged aus, das das Detail zeigt).
        if (!ReferenceEquals(GameCombo.SelectedItem, row))
        {
            GameCombo.SelectedItem = row;
        }
        else
        {
            ShowDetail(row);
        }
    }

    private void OnGameSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GameCombo.SelectedItem is GameRow row)
            ShowDetail(row);
    }

    private void ShowDetail(GameRow row)
    {
        _selectedRow = row;
        _selectedKey = row.Game.Value;

        DetailScroll.DataContext = row;
        DetailScroll.Visibility = Visibility.Visible;

        _ = LoadHistoryAsync(row);
    }

    private void RefreshDetailIfNeeded()
    {
        if (_selectedRow is null)
            return;

        // Detail sichtbar halten; Kernfelder aktualisieren sich per Datenbindung selbst.
        DetailScroll.Visibility = Visibility.Visible;

        // Historie nur neu laden, wenn seit dem letzten Laden eine neue Aktion passiert ist
        // (z. B. Upload/Download hat eine neue Revision erzeugt).
        if (_selectedRow.LastActionUtc != _historyLoadedAction)
            _ = LoadHistoryAsync(_selectedRow);
    }

    private async Task LoadHistoryAsync(GameRow row)
    {
        var key = row.Game.Value;
        _historyLoadedAction = row.LastActionUtc;
        HistoryHintText.Text = "wird geladen …";

        IReadOnlyList<Core.Api.RevisionInfo> revisions;
        try
        {
            revisions = await _agent.GetRevisionsAsync(row.Game);
        }
        catch
        {
            revisions = Array.Empty<Core.Api.RevisionInfo>();
        }

        // Zwischenzeitlicher Auswahlwechsel: Ergebnis verwerfen.
        if (_selectedKey != key)
            return;

        var deviceId = _agent.CurrentDeviceId;
        _revisions.Clear();
        foreach (var info in revisions.OrderByDescending(r => r.Number))
            _revisions.Add(new RevisionRow(row.Game, info, deviceId));

        HistoryHintText.Text = "";
        HistoryEmptyText.Visibility = _revisions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // --- Aktionen ------------------------------------------------------------------

    private void OnOpenFolderClick(object sender, RoutedEventArgs e)
    {
        var path = _selectedRow?.FolderPathRaw;
        if (string.IsNullOrWhiteSpace(path) || !SafeDirectoryExists(path))
        {
            Info("Der Save-Ordner ist nicht (mehr) vorhanden.");
            return;
        }

        try
        {
            // Pfad als eigenes Ziel (UseShellExecute öffnet den Ordner im Explorer) – keine
            // Shell-String-Konkatenation von Fremddaten.
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Info("Ordner konnte nicht geöffnet werden: " + ex.Message);
        }
    }

    private static bool SafeDirectoryExists(string path)
    {
        try { return Directory.Exists(path); }
        catch { return false; }
    }

    private async void OnRestoreClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: RevisionRow rev } button)
            return;

        var name = _selectedRow?.DisplayName ?? rev.Game.DisplayName;
        var dialog = new RestoreDialog(name, rev.Number, rev.LocalDate) { Owner = this };
        if (dialog.ShowDialog() != true)
            return; // Abbrechen ändert nichts.

        button.IsEnabled = false;
        try
        {
            var ok = await _agent.RestoreAsync(rev.Game, rev.Number);
            Info(ok
                ? $"Wiederherstellung von »{name}« auf Version {rev.Number} angestoßen. Der lokale Stand wird beim nächsten Sync ersetzt."
                : "Die Wiederherstellung konnte nicht angestoßen werden. Ist der Server verbunden?");
        }
        catch (Exception ex)
        {
            Info("Wiederherstellung fehlgeschlagen: " + ex.Message);
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

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
            var msg = $"Erkennung abgeschlossen: {result.Games.Count} Spiel(e) gefunden.";
            if (result.SkippedAmbiguous.Count > 0)
            {
                msg += $"\n\n{result.SkippedAmbiguous.Count} Spiel(e) übersprungen, weil ihre Savegames über einen zu weit " +
                       "gefassten Ordner (Laufwerks-/Systemwurzel) streuen. Bei Bedarf über »Ordner hinzufügen« mit dem " +
                       "konkreten Save-Ordner nachtragen:\n• " + string.Join("\n• ", result.SkippedAmbiguous);
            }
            if (result.SkippedTooLarge.Count > 0)
            {
                msg += $"\n\n{result.SkippedTooLarge.Count} Spiel(e) übersprungen, weil ihr Save-Ordner zu groß ist " +
                       "(zu viele Dateien oder zu viele Daten) und den Sync über Stunden blockieren würde. Bei Bedarf " +
                       "über »Ordner hinzufügen« einen konkreten, kleineren Unterordner nachtragen:\n• " +
                       string.Join("\n• ", result.SkippedTooLarge);
            }
            Info(msg);
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

    private void OnAssignFolderClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: GameRow row })
            return;

        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = $"Save-Ordner für »{row.DisplayName}« auswählen",
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

        try
        {
            // Ordner GENAU diesem (übersprungenen) Spiel zuordnen – nicht aus dem Ordnernamen
            // ein neues Spiel ableiten. Danach ist das Spiel regulär verwaltet.
            _agent.AddManualFolder(row.Game, path);
            Info($"Ordner für »{row.DisplayName}« zugeordnet. Das Spiel wird jetzt synchronisiert.");
        }
        catch (Exception ex)
        {
            Info("Ordner konnte nicht zugeordnet werden: " + ex.Message);
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
