using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using SaveVault.Client.Services;
using SaveVault.Client.Ui;
using SaveVault.Core.Api;
using SaveVault.Core.Models;

namespace SaveVault.Client;

/// <summary>
/// Das feste 860×510-Querformat-Fenster des Tray-Clients (eigene Titelleiste, linke Navigation,
/// zwei Ansichten „Übersicht" und „Optionen"). Zeigt Verbindungszustand, Aufmerksamkeits-Glocke,
/// ein zweispaltiges Spiel-Detail mit Cover, Versionshistorie samt In-Fenster-Wiederherstellen und
/// die verschmolzenen Einstellungen. Liest ausschließlich die beobachtbare <see cref="AgentState"/>
/// und ruft Aktionen des <see cref="ClientAgent"/> auf; Zustandsänderungen aus Hintergrund-Threads
/// werden über den <see cref="System.Windows.Threading.Dispatcher"/> in den UI-Thread gebracht.
/// Schließen versteckt das Fenster in den Infobereich (die App läuft weiter).
/// </summary>
public partial class MainWindow : Window
{
    private readonly ClientAgent _agent;
    private readonly ClientConfigStore _configStore = new(new AppPaths());
    private readonly UpdateService _updater = new();

    // Zuletzt gefundenes, noch nicht angewandtes Update (null = keins bekannt).
    private UpdateCheckResult? _pendingUpdate;

    // Additiv abgeglichene Spiel-Liste (Quelle für Auswahl-Dropdown + Aufmerksamkeit).
    private readonly ObservableCollection<GameRow> _games = new();
    private readonly Dictionary<string, GameRow> _rows = new(StringComparer.Ordinal);
    private readonly ObservableCollection<GameRow> _gameOptions = new();   // gefiltert (Dropdown)
    private readonly ObservableCollection<GameRow> _attention = new();     // bis 5 (Popover)
    private readonly ObservableCollection<GameRow> _attentionAll = new();  // alle (Modal)
    private readonly ObservableCollection<RevisionRow> _revisions = new();

    private GameRow? _selectedRow;
    private string? _selectedKey;
    private DateTime? _historyLoadedAction;
    private RevisionRow? _restoreTarget;
    private bool _isOverview = true;

    // Inline-Bestätigung des „Als geteilten Stand hochladen"-Knopfs (siehe OnForceUploadClick):
    // nur je EIN Spiel kann gerade die Bestätigung zeigen, daher genügt ein einzelner Timer.
    private DispatcherTimer? _forceUploadArmTimer;

    // Ecken-Auswahl (Index ↔ Enum) mit deutschen Labels – wie im bisherigen Einstellungs-Dialog.
    private static readonly (WatermarkCorner Corner, string Label)[] Corners =
    {
        (WatermarkCorner.BottomRight, "Unten rechts"),
        (WatermarkCorner.TopRight, "Oben rechts"),
        (WatermarkCorner.TopLeft, "Oben links"),
        (WatermarkCorner.BottomLeft, "Unten links"),
    };

    public MainWindow(ClientAgent agent)
    {
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
        InitializeComponent();

        GameList.ItemsSource = _gameOptions;
        AttentionItems.ItemsSource = _attention;
        AttentionAllList.ItemsSource = _attentionAll;
        HistoryList.ItemsSource = _revisions;

        foreach (var (_, label) in Corners)
            CornerCombo.Items.Add(label);

        SetView(overview: true);

        _agent.State.Changed += OnAgentStateChanged;
        Refresh();
    }

    // --- Zustands-Anbindung --------------------------------------------------------

    private void OnAgentStateChanged(object? sender, EventArgs e)
        => Dispatcher.BeginInvoke(new Action(Refresh));

    private void Refresh()
    {
        var state = _agent.State;

        UpdateConnection(state);
        ReconcileGames(state.SnapshotGames());
        UpdateOverview(state);
        UpdateAttention();

        var needEmpty = !state.IsConfigured || _games.Count == 0;
        EmptyPanel.Visibility = needEmpty ? Visibility.Visible : Visibility.Collapsed;
        DetailArea.Visibility = needEmpty ? Visibility.Collapsed : Visibility.Visible;

        if (needEmpty)
        {
            _selectedRow = null;
            _selectedKey = null;
            DetailArea.DataContext = null;

            if (!state.IsConfigured)
            {
                EmptyTitle.Text = "Noch nicht eingerichtet";
                EmptyText.Text = "Verbinde diesen Client mit deinem SaveVault-Server, um Spielstände zu synchronisieren.";
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
        Brush dot;
        string label, sub;

        if (!state.IsConfigured)
        {
            dot = StatusVisuals.Offline;
            label = "Nicht eingerichtet";
            sub = "Kein Server verbunden. Öffne die Optionen zum Koppeln.";
        }
        else if (state.ServerReachable)
        {
            dot = StatusVisuals.Synced;
            label = "Verbunden";
            var seen = state.LastServerContactUtc is null
                ? ""
                : $" · zuletzt {RelativeTime.Format(state.LastServerContactUtc)}";
            sub = "Server erreichbar" + seen;
        }
        else
        {
            dot = StatusVisuals.Error;
            label = "Server nicht erreichbar";
            sub = string.IsNullOrWhiteSpace(state.LastError)
                ? "Verbindung zum Server unterbrochen."
                : state.LastError!;
        }

        ConnDot.Fill = dot;
        ConnText.Text = label;
        ConnText.Foreground = dot;
        ConnSub.Text = sub;

        var name = _agent.CurrentDeviceName;
        DeviceNameText.Text = string.IsNullOrWhiteSpace(name)
            ? "Dieses Gerät: —"
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

        var gone = _rows.Keys.Where(k => !seen.Contains(k)).ToList();
        foreach (var key in gone)
        {
            if (_rows.TryGetValue(key, out var row))
                _games.Remove(row);
            _rows.Remove(key);
        }
    }

    private void UpdateOverview(AgentState state)
    {
        var total = _games.Count;
        TotalGamesText.Text = total == 1 ? "1 Spiel" : $"{total} Spiele";

        var synced = 0;
        DateTime? latest = null;
        foreach (var row in _games)
        {
            if (!row.IsExcluded && row.Status == SyncStatus.Synced)
                synced++;
            if (row.LastActionUtc is { } t && (latest is null || t > latest))
                latest = t;
        }
        SyncedCountText.Text = $"{synced} synchronisiert";

        var offline = state.IsConfigured && !state.ServerReachable;
        OfflineBanner.Visibility = offline ? Visibility.Visible : Visibility.Collapsed;

        LastSyncText.Text = offline
            ? "Letzter Sync: —"
            : (latest is null ? "Letzter Sync: —" : $"Letzter Sync: {RelativeTime.Format(latest)}");

        // „Jetzt synchronisieren" bei Offline sperren (Regression: war global immer aktiv, hier
        // bewusst gesperrt, sobald der Server nicht erreichbar ist – wie im Design).
        SyncAllButton.IsEnabled = !offline;
    }

    private void UpdateAttention()
    {
        _attentionAll.Clear();
        foreach (var row in _games)
            if (row.NeedsAttention)
                _attentionAll.Add(row);

        _attention.Clear();
        foreach (var row in _attentionAll.Take(5))
            _attention.Add(row);

        var count = _attentionAll.Count;
        BellBadge.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        BellBadgeText.Text = count > 99 ? "99+" : count.ToString();
        BellIcon.Fill = count > 0 ? StatusVisuals.Error : (Brush)FindResource("MutedBrush");

        AttentionEmptyText.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
        AttentionMoreButton.Visibility = count > 5 ? Visibility.Visible : Visibility.Collapsed;
        if (AttentionMoreButton.Template.FindName("MoreText", AttentionMoreButton) is TextBlock more)
            more.Text = $"Alle {count} anzeigen";

        AttentionAllCountText.Text = count == 1 ? "1 Spiel" : $"{count} Spiele";
    }

    // --- Auswahl & Detail ----------------------------------------------------------

    private void EnsureSelection()
    {
        if (_selectedKey is not null && _rows.ContainsKey(_selectedKey))
            return;

        var target = _attention.FirstOrDefault() ?? _games.FirstOrDefault();
        if (target is not null)
            SelectGame(target);
    }

    private void SelectGame(GameRow row)
    {
        // Ein Spielwechsel verwirft eine offene Force-Upload-Bestätigung des VORHERIGEN Spiels
        // (sonst bliebe der Knopf eines nicht mehr sichtbaren Spiels "scharf").
        DisarmForceUpload(_selectedRow);
        // Ebenso eine offene Erstkontakt-Entscheidung: sie bezieht sich auf das VORHERIGE Spiel und
        // würde sonst fälschlich auf das neu ausgewählte angewandt.
        CloseFirstContactOverlay();

        _selectedRow = row;
        _selectedKey = row.Game.Value;

        DetailArea.DataContext = row;
        DetailArea.Visibility = Visibility.Visible;
        HistoryPopup.IsOpen = false; // Spielwechsel schließt eine offene Historie sauber

        // Cover lazy anfordern (UI-Thread → Fortsetzung setzt die Property hier).
        _ = row.EnsureCoverAsync(_agent.Covers);

        _ = LoadHistoryAsync(row);
        _ = ProbeShareStatusAsync(row);
    }

    /// <summary>
    /// Fragt den geteilten Stand für die aktuelle Auswahl ab, sobald ihr Detail-Bereich sichtbar
    /// wird (siehe savevault-change-shared-save-sichtbarkeit.md, „Probe wird zum Dauerzustand statt
    /// Einmal-Klick"). Nutzt <see cref="ClientAgent.TryProbeShareAsync"/> (wirft nie außer bei
    /// echtem Abbruch); ein Fehlschlag lässt den Server-Kasten schlicht bei „—"/dem letzten
    /// bekannten Stand.
    /// </summary>
    private async Task ProbeShareStatusAsync(GameRow row)
    {
        var key = row.Game.Value;
        row.BeginShareProbe();
        var probe = await _agent.TryProbeShareAsync(row.Game);
        if (_selectedKey != key)
            return; // Nutzer hat inzwischen ein anderes Spiel gewählt
        row.ApplyShareProbe(probe);
    }

    private void RefreshDetailIfNeeded()
    {
        if (_selectedRow is null)
            return;
        DetailArea.Visibility = Visibility.Visible;
        _ = _selectedRow.EnsureCoverAsync(_agent.Covers);

        // LastActionUtc aendert sich nur bei einer ECHTEN, abgeschlossenen Uebertragung (Upload/
        // Download/neu erkannter Konflikt - siehe AgentState.SetStatus/Report), nicht bei jedem
        // Heartbeat/NoOp-Zyklus. Das ist bereits die richtige Drosselung fuer beide Nachladungen
        // unten: schliesst waehrend die Ansicht offen ist im Hintergrund ein regulaerer Sync-Zyklus
        // fuer das GERADE angezeigte Spiel ab, sollen sowohl Historie ALS AUCH die Zwei-Kaesten-
        // Anzeige ohne Spielwechsel aktuell werden (siehe
        // specs/savevault-change-sync-anzeige-fixes.md, Fix 4) - bisher fehlte hier die Kaesten-Probe.
        if (_selectedRow.LastActionUtc != _historyLoadedAction)
        {
            _ = LoadHistoryAsync(_selectedRow);
            _ = ProbeShareStatusAsync(_selectedRow);
        }
    }

    private async Task LoadHistoryAsync(GameRow row)
    {
        var key = row.Game.Value;
        _historyLoadedAction = row.LastActionUtc;
        HistoryHintText.Text = "wird geladen …";

        IReadOnlyList<Core.Api.RevisionInfo> revisions;
        try
        {
            // Aktiven Scope abfragen (ueber denselben pur-testbaren Helfer wie
            // ClientAgent.ActiveScope, siehe BucketKey.ForShared): ein "Synchron"-Spiel hat seinen
            // Verlauf im geteilten Bucket, dessen privater Bucket ist seit dem Umschalten
            // eingefroren. Der Default-Parameter (Privat) durchfallen zu lassen zeigte bei geteilten
            // Spielen faelschlich immer nur den alten privaten Verlauf (siehe
            // specs/savevault-change-sync-anzeige-fixes.md, Fix 5).
            revisions = await _agent.GetRevisionsAsync(row.Game, BucketKey.ForShared(row.IsShared));
        }
        catch
        {
            revisions = Array.Empty<Core.Api.RevisionInfo>();
        }

        if (_selectedKey != key)
            return;

        var deviceId = _agent.CurrentDeviceId;
        _revisions.Clear();
        foreach (var info in revisions.OrderByDescending(r => r.Number))
            _revisions.Add(new RevisionRow(row.Game, info, deviceId));

        // Best-effort-Größe aus der jüngsten Revision (fehlt sie, bleibt die Zeile ausgeblendet).
        var newest = revisions.OrderByDescending(r => r.Number).FirstOrDefault();
        row.SetSize(newest?.TotalBytes ?? 0);

        HistoryHintText.Text = "";
        HistoryEmptyText.Visibility = _revisions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // --- Titelleiste / Fenster -----------------------------------------------------

    private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;
        try { DragMove(); }
        catch { /* DragMove kann in Randfällen werfen – bewusst ignorieren. */ }
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void OnCloseButtonClick(object sender, RoutedEventArgs e)
        => Close(); // OnClosing bricht ab und versteckt in den Tray.

    // --- Navigation ----------------------------------------------------------------

    private void OnNavOverview(object sender, RoutedEventArgs e) => SetView(overview: true);
    private void OnNavSettings(object sender, RoutedEventArgs e) => SetView(overview: false);
    private void OnSetupClick(object sender, RoutedEventArgs e) => SetView(overview: false);

    private void SetView(bool overview)
    {
        _isOverview = overview;

        OverviewRoot.Visibility = overview ? Visibility.Visible : Visibility.Collapsed;
        SettingsRoot.Visibility = overview ? Visibility.Collapsed : Visibility.Visible;
        OverviewFooter.Visibility = overview ? Visibility.Visible : Visibility.Collapsed;
        SaveButton.Visibility = overview ? Visibility.Collapsed : Visibility.Visible;
        PageTitleText.Text = overview ? "Übersicht" : "Einstellungen";

        var accent = (Brush)FindResource("AccentBrush");
        var control = (Brush)FindResource("ControlBrush");
        var muted = (Brush)FindResource("MutedBrush");

        NavOverviewButton.Background = overview ? control : Brushes.Transparent;
        NavSettingsButton.Background = overview ? Brushes.Transparent : control;
        OverviewIcon.Fill = overview ? accent : muted;
        SettingsIcon.Fill = overview ? muted : accent;
        OverviewLabel.Foreground = overview ? accent : muted;
        SettingsLabel.Foreground = overview ? muted : accent;

        if (!overview)
            LoadSettingsFields();
    }

    // --- Glocke / Aufmerksamkeit ---------------------------------------------------

    private void OnBellClick(object sender, RoutedEventArgs e)
    {
        AttentionPopup.IsOpen = !AttentionPopup.IsOpen;
        if (AttentionPopup.IsOpen)
            foreach (var row in _attention)
                _ = row.EnsureCoverAsync(_agent.Covers);
    }

    private void OnAttentionItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: GameRow row })
        {
            AttentionPopup.IsOpen = false;
            AttentionAllOverlay.Visibility = Visibility.Collapsed;
            if (!_isOverview)
                SetView(overview: true);
            SelectGame(row);
        }
    }

    private void OnShowAllAttention(object sender, RoutedEventArgs e)
    {
        AttentionPopup.IsOpen = false;
        AttentionAllOverlay.Visibility = Visibility.Visible;
        // Echte Cover der Modal-Einträge lazy anfordern (wie das Popover).
        foreach (var row in _attentionAll)
            _ = row.EnsureCoverAsync(_agent.Covers);
    }

    private void OnCloseAttentionAll(object sender, RoutedEventArgs e)
        => AttentionAllOverlay.Visibility = Visibility.Collapsed;

    private void OnCloseAttentionAllBackdrop(object sender, MouseButtonEventArgs e)
        => AttentionAllOverlay.Visibility = Visibility.Collapsed;

    private void OnSwallowClick(object sender, MouseButtonEventArgs e)
        => e.Handled = true; // Klick auf die Karte schließt das Modal nicht.

    // --- Spiel-Dropdown ------------------------------------------------------------

    private void OnToggleDropdown(object sender, RoutedEventArgs e)
        => GameDropdownPopup.IsOpen = !GameDropdownPopup.IsOpen;

    private void OnDropdownOpened(object sender, EventArgs e)
    {
        SearchBox.Text = "";
        FillGameOptions("");
        SearchBox.Focus();
    }

    private void OnDropdownClosed(object sender, EventArgs e) { }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
        => FillGameOptions(SearchBox.Text);

    private void FillGameOptions(string term)
    {
        _gameOptions.Clear();
        var t = (term ?? "").Trim();
        foreach (var row in _games)
        {
            if (t.Length == 0 || row.DisplayName.Contains(t, StringComparison.OrdinalIgnoreCase))
                _gameOptions.Add(row);
        }
        NoGamesText.Visibility = _gameOptions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        // Echte Cover der sichtbaren Dropdown-Einträge lazy anfordern (wie das Popover).
        foreach (var row in _gameOptions)
            _ = row.EnsureCoverAsync(_agent.Covers);
    }

    private void OnGameOptionClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: GameRow row })
        {
            GameDropdownPopup.IsOpen = false;
            SelectGame(row);
        }
    }

    // --- Detail-Aktionen -----------------------------------------------------------

    private async void OnGameSyncNowClick(object sender, RoutedEventArgs e)
    {
        // Ein einzelnes Spiel „jetzt sichern": es gibt nur einen globalen Sync-Choke-Point,
        // der pro Spiel serialisiert – also einen Gesamt-Sync anstoßen (deckt dieses Spiel ab).
        await RunGuarded((Button)sender, () => _agent.SyncNowAsync());
    }

    private async void OnRetryClick(object sender, RoutedEventArgs e)
    {
        await RunGuarded((Button)sender, () => _agent.SyncNowAsync());
    }

    private void OnTogglePauseClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: GameRow row })
            return;
        if (row.IsExcluded)
            _agent.IncludeGame(row.Game);
        else
            _agent.ExcludeGame(row.Game);
        // Der Rest läuft über State.Changed → Refresh.
    }

    // Verhindert paralleles/mehrfaches Umschalten (Doppelklick auf einen der beiden Kästen).
    private bool _switchInFlight;

    /// <summary>
    /// Klick auf den (inaktiven) Server-Kasten: existiert schon ein geteilter Stand, wird er
    /// übernommen (bestehender Übernehmen-Pfad); existiert noch keiner, wird der lokale Stand ohne
    /// Rückfrage zum Seed. Klick auf den bereits aktiven Kasten ist ein No-Op.
    /// </summary>
    private async void OnServerBoxClick(object sender, RoutedEventArgs e)
    {
        // Bei deaktivierter Sicherung gelten beide Kästen als inaktiv (Tims Korrektur) – ein Klick
        // löst dann bewusst nichts aus, statt einen Wechsel auf einen bereits ausgegrauten Kasten.
        if (sender is not Button { Tag: GameRow row } || row.IsShared || row.IsExcluded || _switchInFlight)
            return;

        // Der Force-Upload-Knopf ist ohnehin nur bei "Lokal aktiv" sichtbar – ein Wechsel weg von
        // "Lokal" macht seine Bestätigung ungültig (defensiv, auch falls sie gerade offen war).
        DisarmForceUpload(row);

        _switchInFlight = true;
        try
        {
            var probe = row.LastShareProbe;
            if (probe is null)
            {
                probe = await _agent.TryProbeShareAsync(row.Game);
                row.ApplyShareProbe(probe);
            }
            if (probe is null)
            {
                Info("Nicht mit dem Server verbunden – Wechsel ist gerade nicht möglich.");
                return;
            }

            if (!probe.SharedExists)
            {
                await _agent.SeedShareAsync(row.Game);
                // Nach jeder mutierenden Aktion die Zwei-Kästen-Anzeige sofort neu abfragen (siehe
                // savevault-change-shared-save-sichtbarkeit.md, Nachtrag "Probe-Refresh nach jeder
                // mutierenden Aktion") – behebt die zuvor stehen bleibende CTA-Meldung.
                await ProbeShareStatusAsync(row);
                return;
            }

            // Erstkontakt: dieses Gerät ist dem geteilten Stand noch NIE beigetreten, obwohl bereits
            // einer existiert – vor der Übernahme fragen, welcher Stand gelten soll (siehe
            // specs/savevault-change-sync-anzeige-fixes.md, Fix „Erstkontakt-Dialog"). Ein SPÄTERES
            // Hin- und Herschalten eines bereits bekannten geteilten Spiels (IsFirstContact == false)
            // bleibt bewusst ohne diesen Dialog – Tims ausdrückliche Vorgabe, keine Ausnahme.
            if (probe.IsFirstContact)
            {
                ShowFirstContactOverlay(row, probe);
                return;
            }

            await _agent.JoinTakeSharedAsync(row.Game, probe.SharedRevision, probe.SharedManifest!);
            await ProbeShareStatusAsync(row);
        }
        catch (Exception ex)
        {
            Info("Wechsel fehlgeschlagen: " + ex.Message);
        }
        finally
        {
            _switchInFlight = false;
        }
    }

    // --- Erstkontakt-Entscheidungsdialog (In-Fenster-Overlay) -----------------------
    // Siehe OnServerBoxClick: nur beim ERSTEN Beitritt zu einem bereits existierenden geteilten
    // Stand gezeigt (ShareProbe.IsFirstContact). Zwei gleichwertige Optionen, spiegelbildlich zu den
    // bestehenden Übernahme-Pfaden (JoinTakeSharedAsync/JoinTakeLocalAsync) – kein neuer Kern-Pfad.

    private GameRow? _firstContactRow;
    private ShareProbe? _firstContactProbe;

    private void ShowFirstContactOverlay(GameRow row, ShareProbe probe)
    {
        _firstContactRow = row;
        _firstContactProbe = probe;

        FirstContactTitleText.Text = $"„{row.DisplayName}“ wird bereits geteilt";

        var shared = probe.Shared;
        FirstContactServerInfo.Text = shared is null
            ? "—"
            : $"{ByteSize.Format(shared.TotalBytes)} · {shared.FileCount} Dat. · {RelativeTime.Format(shared.WhenUtc)} · "
              + (string.IsNullOrWhiteSpace(shared.DeviceLabel) ? "unbekanntes Gerät" : shared.DeviceLabel!);

        var local = probe.Local;
        FirstContactLocalInfo.Text =
            $"{ByteSize.Format(local.TotalBytes)} · {local.FileCount} Dat. · {RelativeTime.Format(local.WhenUtc)}";

        FirstContactOverlay.Visibility = Visibility.Visible;
    }

    private void CloseFirstContactOverlay()
    {
        FirstContactOverlay.Visibility = Visibility.Collapsed;
        _firstContactRow = null;
        _firstContactProbe = null;
    }

    private void OnCloseFirstContactBackdrop(object sender, MouseButtonEventArgs e)
        => CloseFirstContactOverlay();

    private void OnCancelFirstContact(object sender, RoutedEventArgs e)
        => CloseFirstContactOverlay();

    private async void OnTakeServerClick(object sender, RoutedEventArgs e)
    {
        var row = _firstContactRow;
        var probe = _firstContactProbe;
        CloseFirstContactOverlay();
        if (row is null || probe is null || _switchInFlight)
            return;

        _switchInFlight = true;
        try
        {
            await _agent.JoinTakeSharedAsync(row.Game, probe.SharedRevision, probe.SharedManifest!);
            await ProbeShareStatusAsync(row);
        }
        catch (Exception ex)
        {
            Info("Wechsel fehlgeschlagen: " + ex.Message);
        }
        finally
        {
            _switchInFlight = false;
        }
    }

    private async void OnTakeLocalClick(object sender, RoutedEventArgs e)
    {
        var row = _firstContactRow;
        var probe = _firstContactProbe;
        CloseFirstContactOverlay();
        if (row is null || probe is null || _switchInFlight)
            return;

        _switchInFlight = true;
        try
        {
            await _agent.JoinTakeLocalAsync(row.Game, probe.SharedRevision, probe.SharedManifest!);
            await ProbeShareStatusAsync(row);
        }
        catch (Exception ex)
        {
            Info("Hochladen fehlgeschlagen: " + ex.Message);
        }
        finally
        {
            _switchInFlight = false;
        }
    }

    /// <summary>
    /// Klick auf den (inaktiven) Lokal-Kasten: schaltet zurück auf den eigenen, seit dem Teilen
    /// eingefrorenen privaten Bucket. Klick auf den bereits aktiven Kasten ist ein No-Op.
    /// </summary>
    private async void OnLocalBoxClick(object sender, RoutedEventArgs e)
    {
        // Siehe OnServerBoxClick: bei deaktivierter Sicherung ist auch der Lokal-Kasten inaktiv
        // ausgegraut und ein Klick darauf ist bewusst ein No-Op.
        if (sender is not Button { Tag: GameRow row } || !row.IsShared || row.IsExcluded || _switchInFlight)
            return;

        _switchInFlight = true;
        try
        {
            await _agent.SwitchToLocalAsync(row.Game);

            // Nach jeder mutierenden Aktion die Zwei-Kästen-Anzeige sofort neu abfragen (siehe
            // savevault-change-shared-save-sichtbarkeit.md, Nachtrag "Probe-Refresh nach jeder
            // mutierenden Aktion") – behebt die zuvor stehen bleibende CTA-Meldung.
            await ProbeShareStatusAsync(row);
        }
        catch (Exception ex)
        {
            Info("Wechsel fehlgeschlagen: " + ex.Message);
        }
        finally
        {
            _switchInFlight = false;
        }
    }

    // --- „Als geteilten Stand hochladen" (expliziter Force-Upload-Knopf) --------------

    /// <summary>
    /// Klick auf „Als geteilten Stand hochladen": erster Klick zeigt die Inline-Bestätigung mit den
    /// Kennzahlen beider Seiten direkt am Knopf (kein neuer Dialog-Typ), der zweite Klick löst den
    /// Upload aus (siehe savevault-change-shared-save-sichtbarkeit.md, Nachtrag „Neuer expliziter
    /// Knopf"). Der Knopf ist nur sichtbar, wenn „Lokal" aktiv ist und bereits ein geteilter Stand
    /// existiert (Bindung <see cref="GameRow.ForceUploadVisibility"/>).
    /// </summary>
    private async void OnForceUploadClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: GameRow row })
            return;

        if (!row.ForceUploadArmed)
        {
            ArmForceUpload(row);
            return;
        }

        DisarmForceUpload(row);
        if (_switchInFlight)
            return;

        _switchInFlight = true;
        try
        {
            await _agent.ForceUploadLocalAsSharedAsync(row.Game);

            // Nach jeder mutierenden Aktion die Zwei-Kästen-Anzeige sofort neu abfragen (siehe
            // savevault-change-shared-save-sichtbarkeit.md, Nachtrag "Probe-Refresh nach jeder
            // mutierenden Aktion").
            await ProbeShareStatusAsync(row);
        }
        catch (Exception ex)
        {
            Info("Hochladen fehlgeschlagen: " + ex.Message);
        }
        finally
        {
            _switchInFlight = false;
        }
    }

    /// <summary>Schaltet die Inline-Bestätigung eines Force-Upload-Knopfs scharf (mit Auto-Timeout).</summary>
    private void ArmForceUpload(GameRow row)
    {
        // Nur eine Bestätigung gleichzeitig – ein zuvor scharf geschaltetes anderes Spiel (Randfall,
        // z. B. schnelles Umschalten) wird zuerst entwaffnet.
        if (_selectedRow is { } previous && !ReferenceEquals(previous, row))
            DisarmForceUpload(previous);

        row.SetForceUploadArmed(true);
        _forceUploadArmTimer?.Stop();
        _forceUploadArmTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _forceUploadArmTimer.Tick += (_, _) => DisarmForceUpload(row);
        _forceUploadArmTimer.Start();
    }

    /// <summary>Nimmt die Inline-Bestätigung eines Force-Upload-Knopfs zurück und stoppt den Timeout-Timer.</summary>
    private void DisarmForceUpload(GameRow? row)
    {
        _forceUploadArmTimer?.Stop();
        _forceUploadArmTimer = null;
        row?.SetForceUploadArmed(false);
    }

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

    private async void OnResolveConflictClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: GameRow row } button)
            return;

        button.IsEnabled = false;
        try
        {
            var conflicts = await _agent.GetConflictsAsync();
            // c.Game trägt seit der Bucket-Umstellung den GESCOPTEN Schlüssel (z. B. "dev|{owner}|{value}"
            // oder "shared|{value}"), row.Game dagegen den kanonischen Wert – ein direkter Equals-Vergleich
            // schlägt daher praktisch immer fehl ("kein Konflikt" trotz sichtbarem Konflikt-Status). Über
            // BucketKey.Original() auf die kanonische Identität zurückführen, bevor verglichen wird.
            var conflict = conflicts.FirstOrDefault(c => BucketKey.Original(c.Game).Equals(row.Game) && !c.Resolved);
            if (conflict is null)
            {
                // Kein offener Server-Konflikt (mehr) gefunden, aber die Zeile zeigt trotzdem
                // "Konflikt" – das ist genau das Signal für einen VERWAISTEN Status (siehe
                // savevault-change-shared-save-sichtbarkeit.md, Nachtrag 4). Statt nur einer
                // Info-Meldung: prüfen, ob es überhaupt etwas zum Vergleichen/Übernehmen gibt.
                var probe = await _agent.ProbeOrphanedConflictAsync(row.Game);
                if (probe is null)
                {
                    Info("Für dieses Spiel liegt aktuell kein offener Konflikt vor.");
                    return;
                }

                var localInfo = $"Lokal: {probe.Local.FileCount} Dat. · {ByteSize.Format(probe.Local.TotalBytes)} · {RelativeTime.Format(probe.Local.WhenUtc)}";
                var serverInfo = $"Server: {probe.Server.FileCount} Dat. · {ByteSize.Format(probe.Server.TotalBytes)} · {RelativeTime.Format(probe.Server.WhenUtc)}";
                var message = "Kein offener Konflikt mehr auf dem Server. Aktuellen Server-Stand übernehmen? "
                    + "Das ersetzt deinen jetzigen lokalen Ordnerinhalt.\n\n" + localInfo + "\n" + serverInfo;

                var choice = System.Windows.MessageBox.Show(this, message, "Verwaisten Konflikt auflösen",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (choice != MessageBoxResult.Yes)
                    return;

                try
                {
                    await _agent.ClearOrphanedConflictAsync(row.Game);
                    Info("Server-Stand übernommen – das Spiel ist jetzt synchronisiert.");
                }
                catch (Exception ex)
                {
                    Info("Übernehmen fehlgeschlagen: " + ex.Message);
                }
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
            _agent.AddManualFolder(row.Game, path);
            Info($"Ordner für »{row.DisplayName}« zugeordnet. Das Spiel wird jetzt synchronisiert.");
        }
        catch (Exception ex)
        {
            Info("Ordner konnte nicht zugeordnet werden: " + ex.Message);
        }
    }

    // --- Wiederherstellen (In-Fenster-Overlay) -------------------------------------

    private void OnRestoreClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: RevisionRow rev })
            return;
        _restoreTarget = rev;
        var name = _selectedRow?.DisplayName ?? rev.Game.DisplayName;
        RestoreSubText.Text = $"{name} · Version {rev.Number} · {rev.LocalDate}";
        RestoreOverlay.Visibility = Visibility.Visible;
    }

    private void OnCloseRestore(object sender, RoutedEventArgs e) => CloseRestoreOverlay();
    private void OnCloseRestoreBackdrop(object sender, MouseButtonEventArgs e) => CloseRestoreOverlay();

    private void CloseRestoreOverlay()
    {
        RestoreOverlay.Visibility = Visibility.Collapsed;
        _restoreTarget = null;
    }

    private async void OnConfirmRestore(object sender, RoutedEventArgs e)
    {
        var rev = _restoreTarget;
        if (rev is null)
        {
            CloseRestoreOverlay();
            return;
        }

        var name = _selectedRow?.DisplayName ?? rev.Game.DisplayName;
        RestoreConfirmButton.IsEnabled = false;
        try
        {
            var ok = await _agent.RestoreAsync(rev.Game, rev.Number);
            CloseRestoreOverlay();
            Info(ok
                ? $"Wiederherstellung von »{name}« auf Version {rev.Number} angestoßen. Der lokale Stand wird beim nächsten Sync ersetzt."
                : "Die Wiederherstellung konnte nicht angestoßen werden. Ist der Server verbunden?");
        }
        catch (Exception ex)
        {
            CloseRestoreOverlay();
            Info("Wiederherstellung fehlgeschlagen: " + ex.Message);
        }
        finally
        {
            RestoreConfirmButton.IsEnabled = true;
        }
    }

    // --- Versionshistorie (Flyout) --------------------------------------------------

    private void OnToggleHistoryClick(object sender, RoutedEventArgs e)
    {
        if (!HistoryPopup.IsOpen)
        {
            // Der Popup hängt jetzt am Hauptfenster statt am Button (Tims Korrektur: das Flyout
            // soll direkt rechts neben dem GESAMTEN Fenster ausfahren, nicht mittig über dem
            // Button überlappen). Vertikal am Button ausrichten, damit es trotzdem sichtbar neben
            // der Aktionsreihe erscheint, unabhängig davon, wie hoch der Detail-Bereich gerade ist.
            var buttonTop = HistoryToggleButton.TransformToAncestor(this).Transform(new Point(0, 0)).Y;
            HistoryPopup.VerticalOffset = buttonTop - HistoryToggleButton.ActualHeight;
        }
        HistoryPopup.IsOpen = !HistoryPopup.IsOpen;
    }

    /// <summary>Kurze Fade+Slide-Öffnen-Transition (~180 ms) und Chevron-Richtung, analog dem
    /// bestehenden kurzen UI-Übergangs-Stil des Clients.</summary>
    private void OnHistoryPopupOpened(object sender, EventArgs e)
    {
        HistoryChevronRotate.Angle = 90;

        var duration = TimeSpan.FromMilliseconds(180);
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        HistoryFlyoutTransform.X = -10;
        HistoryFlyoutBorder.Opacity = 0;
        HistoryFlyoutBorder.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
        HistoryFlyoutTransform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-10, 0, duration) { EasingFunction = ease });
    }

    private void OnHistoryPopupClosed(object sender, EventArgs e)
        => HistoryChevronRotate.Angle = 0;

    // --- Fußleiste (Übersicht) -----------------------------------------------------

    private async void OnSyncNowClick(object sender, RoutedEventArgs e)
        => await RunGuarded((Button)sender, () => _agent.SyncNowAsync());

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

    // --- Optionen (verschmolzen aus dem früheren Einstellungs-Fenster) -------------

    private void LoadSettingsFields()
    {
        var config = _configStore.Load();
        ServerUrlBox.Text = config.ServerUrl ?? "";
        DeviceNameBox.Text = config.DeviceName ?? Environment.MachineName;
        IntervalBox.Text = config.SyncIntervalSeconds.ToString();
        AutostartToggle.IsChecked = config.AutostartEnabled;
        AutoUpdateToggle.IsChecked = config.AutoUpdateCheckEnabled;
        UpdateVersionText.Text = "Installierte Version: " + UpdateService.CurrentVersion;
        NotifyEnabledToggle.IsChecked = config.ToastsEnabled;
        NotifyTransfersToggle.IsChecked = config.NotifyTransfers;
        NotifyConflictsToggle.IsChecked = config.NotifyConflicts;
        NotifySoundToggle.IsChecked = config.NotificationSound;
        WatermarkToggle.IsChecked = config.GameWatermarkEnabled;

        var cornerIndex = Array.FindIndex(Corners, c => c.Corner == config.WatermarkCorner);
        CornerCombo.SelectedIndex = cornerIndex >= 0 ? cornerIndex : 0;

        UpdateSubOptionsEnabled();
        PairResultText.Visibility = Visibility.Collapsed;
        SaveResultText.Visibility = Visibility.Collapsed;
        // Pairing-Code bleibt leer; der Token wird nie geladen/angezeigt.
    }

    private void OnMasterToggled(object sender, RoutedEventArgs e) => UpdateSubOptionsEnabled();

    private void UpdateSubOptionsEnabled()
    {
        var on = NotifyEnabledToggle.IsChecked == true;
        if (NotifyTransfersToggle is null)
            return;
        NotifyTransfersToggle.IsEnabled = on;
        NotifyConflictsToggle.IsEnabled = on;
        NotifySoundToggle.IsEnabled = on;
        WatermarkToggle.IsEnabled = on;
        CornerCombo.IsEnabled = on;
        NotifySubPanel.Opacity = on ? 1.0 : 0.4;
        CornerPanel.Opacity = on ? 1.0 : 0.4;
    }

    private async void OnPairClick(object sender, RoutedEventArgs e)
    {
        var serverUrl = ServerUrlBox.Text.Trim();
        var code = PairingCodeBox.Text.Trim();
        var deviceName = DeviceNameBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(serverUrl))
        {
            ShowResult(PairResultText, "Bitte eine Server-URL angeben.", ok: false);
            return;
        }
        if (string.IsNullOrWhiteSpace(code))
        {
            ShowResult(PairResultText, "Bitte den Pairing-Code angeben.", ok: false);
            return;
        }

        PairButton.IsEnabled = false;
        ShowResult(PairResultText, "Kopple mit dem Server…", ok: true, neutral: true);
        try
        {
            var result = await _agent.PairAsync(serverUrl, code, deviceName);
            if (result.Success)
            {
                PairingCodeBox.Clear();
                ShowResult(PairResultText, "Kopplung erfolgreich. Dieses Gerät ist jetzt verbunden.", ok: true);
                LoadSettingsFields();
                Refresh();
            }
            else
            {
                ShowResult(PairResultText, result.ErrorMessage ?? "Kopplung fehlgeschlagen.", ok: false);
            }
        }
        catch (Exception ex)
        {
            ShowResult(PairResultText, "Kopplung fehlgeschlagen: " + ex.Message, ok: false);
        }
        finally
        {
            PairButton.IsEnabled = true;
        }
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var deviceName = DeviceNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(deviceName))
            deviceName = Environment.MachineName;

        if (!int.TryParse(IntervalBox.Text.Trim(), out var seconds))
        {
            ShowResult(SaveResultText, "Das Sync-Intervall muss eine Zahl (Sekunden) sein.", ok: false);
            return;
        }
        if (seconds < 5)
            seconds = 5;

        SaveButton.IsEnabled = false;
        try
        {
            var config = _configStore.Load();
            config.DeviceName = deviceName;
            config.SyncIntervalSeconds = seconds;
            config.AutostartEnabled = AutostartToggle.IsChecked == true;
            config.AutoUpdateCheckEnabled = AutoUpdateToggle.IsChecked == true;
            config.ToastsEnabled = NotifyEnabledToggle.IsChecked == true;
            config.NotifyTransfers = NotifyTransfersToggle.IsChecked == true;
            config.NotifyConflicts = NotifyConflictsToggle.IsChecked == true;
            config.NotificationSound = NotifySoundToggle.IsChecked == true;
            config.GameWatermarkEnabled = WatermarkToggle.IsChecked == true;
            var idx = CornerCombo.SelectedIndex;
            config.WatermarkCorner = idx >= 0 && idx < Corners.Length
                ? Corners[idx].Corner
                : WatermarkCorner.BottomRight;
            _configStore.Save(config);
            IntervalBox.Text = seconds.ToString();

            AutostartService.Apply(config.AutostartEnabled);

            await _agent.StopAsync();
            await _agent.StartAsync();

            ShowResult(SaveResultText, "Gespeichert.", ok: true);
            Refresh();
        }
        catch (Exception ex)
        {
            ShowResult(SaveResultText, "Speichern fehlgeschlagen: " + ex.Message, ok: false);
        }
        finally
        {
            SaveButton.IsEnabled = true;
        }
    }

    private static void ShowResult(TextBlock target, string message, bool ok, bool neutral = false)
    {
        target.Text = message;
        target.Visibility = Visibility.Visible;
        target.Foreground = neutral
            ? StatusVisuals.Offline
            : (ok ? StatusVisuals.Synced : StatusVisuals.Error);
    }

    // --- Selbst-Update -------------------------------------------------------------

    /// <summary>
    /// Prüft gegen GitHub, ob ein neueres Release vorliegt, und spiegelt das Ergebnis in Banner und
    /// Optionen. Wird vom Nutzer („Nach Updates suchen") wie auch selbsttätig (App: Start/täglich)
    /// aufgerufen. Läuft auf dem UI-Thread und wirft nie – jeder Fehler landet als
    /// <see cref="UpdateCheckStatus.Failed"/> im Ergebnis.
    /// </summary>
    public async Task<UpdateCheckResult> CheckForUpdatesAsync(bool userInitiated)
    {
        if (userInitiated)
        {
            CheckUpdatesButton.IsEnabled = false;
            ShowUpdateStatus("Suche nach Updates…", neutral: true);
        }

        UpdateCheckResult result;
        try
        {
            result = await _updater.CheckAsync();
        }
        catch (Exception ex)
        {
            result = new UpdateCheckResult(UpdateCheckStatus.Failed, null, null, ex.Message);
        }
        finally
        {
            CheckUpdatesButton.IsEnabled = true;
        }

        // Zeitpunkt einer ERFOLGREICHEN Prüfung merken (dämpft die Startprüfung). Bei einem
        // Fehlschlag (z. B. Netz beim Boot noch nicht da) NICHT stempeln, sonst würde die
        // 20-h-Dämpfung die nächste Startprüfung unterdrücken, obwohl nie geprüft wurde.
        if (result.Status != UpdateCheckStatus.Failed)
        {
            try
            {
                var config = _configStore.Load();
                config.LastUpdateCheckUtc = DateTime.UtcNow;
                _configStore.Save(config);
            }
            catch { /* nicht kritisch */ }
        }

        ApplyUpdateResultToUi(result, userInitiated);
        return result;
    }

    /// <summary>Überträgt ein Prüfergebnis in Banner + Optionen (Status/Buttons).</summary>
    private void ApplyUpdateResultToUi(UpdateCheckResult result, bool userInitiated)
    {
        switch (result.Status)
        {
            case UpdateCheckStatus.UpdateAvailable:
                _pendingUpdate = result;
                UpdateBannerText.Text = $"Neue Version {result.Available} verfügbar";
                UpdateBannerSubText.Text = "Der Client aktualisiert sich und startet neu.";
                UpdateBanner.Visibility = Visibility.Visible;
                SettingsApplyButton.Visibility = Visibility.Visible;
                ShowUpdateStatus(
                    $"Neue Version {result.Available} verfügbar (installiert: {UpdateService.CurrentVersion}).",
                    neutral: false, ok: true);
                break;

            case UpdateCheckStatus.UpToDate:
                _pendingUpdate = null;
                UpdateBanner.Visibility = Visibility.Collapsed;
                SettingsApplyButton.Visibility = Visibility.Collapsed;
                if (userInitiated)
                    ShowUpdateStatus($"Du hast bereits die aktuelle Version ({UpdateService.CurrentVersion}).",
                        neutral: false, ok: true);
                break;

            default: // Failed
                if (userInitiated)
                    ShowUpdateStatus("Update-Prüfung fehlgeschlagen: " + (result.Error ?? "unbekannt"),
                        neutral: false, ok: false);
                break;
        }
    }

    private async void OnCheckUpdatesClick(object sender, RoutedEventArgs e)
        => await CheckForUpdatesAsync(userInitiated: true);

    /// <summary>
    /// Wendet das gefundene Update an: lädt/entpackt das Release ins Staging, startet die gestagte
    /// exe im Applier-Modus und beendet die App, damit der Austausch die laufenden Dateien freibekommt.
    /// </summary>
    private async void OnApplyUpdateClick(object sender, RoutedEventArgs e)
    {
        var pending = _pendingUpdate;
        if (pending?.DownloadUrl is null)
            return;

        SetApplyBusy(true);
        UpdateBannerSubText.Text = "Lade Update…";
        ShowUpdateStatus("Lade Update…", neutral: true);
        try
        {
            var stagedExe = await _updater.DownloadAndStageAsync(pending.DownloadUrl);

            UpdateBannerSubText.Text = "Starte Aktualisierung…";
            ShowUpdateStatus("Starte Aktualisierung…", neutral: true);

            if (!_updater.StartApplier(stagedExe))
            {
                SetApplyBusy(false);
                UpdateBannerSubText.Text = "Der Client aktualisiert sich und startet neu.";
                ShowUpdateStatus("Aktualisierung konnte nicht gestartet werden.", neutral: false, ok: false);
                return;
            }

            // Applier läuft und wartet auf unser Ende → sauber beenden. Danach tauscht er aus und startet neu.
            System.Windows.Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            SetApplyBusy(false);
            UpdateBannerSubText.Text = "Der Client aktualisiert sich und startet neu.";
            ShowUpdateStatus("Update fehlgeschlagen: " + ex.Message, neutral: false, ok: false);
        }
    }

    /// <summary>Sperrt/entsperrt die Update-Knöpfe (Banner + Optionen) während des Anwendens.</summary>
    private void SetApplyBusy(bool busy)
    {
        BannerApplyButton.IsEnabled = !busy;
        SettingsApplyButton.IsEnabled = !busy;
        CheckUpdatesButton.IsEnabled = !busy;
    }

    private void ShowUpdateStatus(string message, bool neutral, bool ok = true)
    {
        UpdateStatusText.Text = message;
        UpdateStatusText.Visibility = Visibility.Visible;
        UpdateStatusText.Foreground = neutral
            ? StatusVisuals.Offline
            : (ok ? StatusVisuals.Synced : StatusVisuals.Error);
    }

    // --- Helfer --------------------------------------------------------------------

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

    protected override void OnClosing(CancelEventArgs e)
    {
        // Nicht schließen, sondern in den Tray zurückziehen – die App läuft weiter.
        e.Cancel = true;
        Hide();
        base.OnClosing(e);
    }
}
