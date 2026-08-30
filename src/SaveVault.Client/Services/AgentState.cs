using SaveVault.Core.Models;
using SaveVault.Core.Sync;

namespace SaveVault.Client.Services;

/// <summary>
/// Art einer abgeschlossenen, meldenswerten Sync-Aktion (Grundlage für die Tray-Toasts).
/// <see cref="Uploaded"/> = „gesichert" (Upload zum Server), <see cref="Downloaded"/> =
/// „synchronisiert" (Download vom Server), <see cref="Conflict"/> = neu erkannter Konflikt.
/// </summary>
public enum SyncActivityKind
{
    /// <summary>Lokaler Stand wurde zum Server hochgeladen („gesichert").</summary>
    Uploaded,

    /// <summary>Server-Stand wurde lokal angewandt („synchronisiert").</summary>
    Downloaded,

    /// <summary>Ein neuer Konflikt wurde erkannt.</summary>
    Conflict,
}

/// <summary>
/// Nutzlast des <see cref="AgentState.SyncActivityOccurred"/>-Ereignisses: welches Spiel,
/// welche Art von Aktion und wann (UTC). Rein informativ für die GUI (Toast-Ausgabe).
/// </summary>
public sealed record SyncActivity(GameKey Game, SyncActivityKind Kind, DateTime WhenUtc);

/// <summary>
/// Beobachtbarer Zustand eines Spiels aus Sicht dieses Geräts – die Datengrundlage, die
/// die WPF-GUI (Schritt 6) pro Spiel anzeigt. Reine Anzeige-Sicht (kein WPF), veränderbar
/// nur über <see cref="AgentState"/>.
/// </summary>
public sealed class GameStatusView
{
    public GameStatusView(GameKey game)
        => Game = game ?? throw new ArgumentNullException(nameof(game));

    /// <summary>Kanonische Spielidentität.</summary>
    public GameKey Game { get; }

    /// <summary>Anzeigename für die GUI.</summary>
    public string DisplayName => Game.DisplayName;

    /// <summary>Aktueller Sync-Status dieses Geräts für das Spiel.</summary>
    public SyncStatus Status { get; internal set; } = SyncStatus.Pending;

    /// <summary>Zuletzt gesehene Basis-Revision.</summary>
    public long BaseRevision { get; internal set; }

    /// <summary>Zugeordneter lokaler Save-Ordner (falls bekannt).</summary>
    public string? FolderPath { get; internal set; }

    /// <summary>Kurztext der letzten Aktion (z. B. „Hochgeladen → Rev 4").</summary>
    public string? LastAction { get; internal set; }

    /// <summary>Zeitpunkt der letzten Aktion (UTC).</summary>
    public DateTime? LastActionUtc { get; internal set; }

    /// <summary>
    /// Ob dieses Spiel bei der Erkennung ÜBERSPRUNGEN wurde (Save-Ordner nicht automatisch
    /// bestimmbar oder Save-Set zu groß) und deshalb manuell zugeordnet werden muss.
    /// </summary>
    public bool IsSkipped { get; internal set; }

    /// <summary>Menschenlesbarer Grund/Hinweis für das Überspringen (nur wenn <see cref="IsSkipped"/>).</summary>
    public string? SkipReason { get; internal set; }

    internal GameStatusView Clone() => new(Game)
    {
        Status = Status,
        BaseRevision = BaseRevision,
        FolderPath = FolderPath,
        LastAction = LastAction,
        LastActionUtc = LastActionUtc,
        IsSkipped = IsSkipped,
        SkipReason = SkipReason,
    };
}

/// <summary>
/// Die zentrale, thread-sichere <b>Status-Fläche</b> des Client-Hintergrunds, die die GUI
/// (Schritt 6) nur noch anzeigen muss: je Spiel ein <see cref="GameStatusView"/> sowie
/// globale Merkmale (eingerichtet? Server erreichbar? letzte Meldung/Zeit). Jede Änderung
/// löst <see cref="Changed"/> aus – die GUI aktualisiert sich dann aus
/// <see cref="SnapshotGames"/> und den Properties.
/// </summary>
public sealed class AgentState
{
    private readonly object _lock = new();
    private readonly Dictionary<string, GameStatusView> _games = new(StringComparer.Ordinal);
    private readonly Func<DateTime> _nowUtc;

    public AgentState(Func<DateTime>? nowUtc = null)
        => _nowUtc = nowUtc ?? (() => DateTime.UtcNow);

    /// <summary>Wird bei jeder Zustandsänderung ausgelöst (für Datenbindung/Refresh der GUI).</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Wird <b>nur</b> bei einer echten, abgeschlossenen Übertragung ausgelöst (Upload,
    /// Download, neu erkannter Konflikt) – zusätzlich zu <see cref="Changed"/>. Grundlage für
    /// die Tray-Toasts. Wird bewusst <b>nicht</b> bei jedem <see cref="SetStatus"/>, bei
    /// reinem Statuswechsel oder bei „NoOp" gefeuert.
    /// </summary>
    public event EventHandler<SyncActivity>? SyncActivityOccurred;

    /// <summary>Ob der Client eingerichtet ist (Server-URL + Token vorhanden).</summary>
    public bool IsConfigured { get; private set; }

    /// <summary>Ob der Server beim letzten Kontakt erreichbar war.</summary>
    public bool ServerReachable { get; private set; }

    /// <summary>Zeitpunkt des letzten erfolgreichen Server-Kontakts (UTC).</summary>
    public DateTime? LastServerContactUtc { get; private set; }

    /// <summary>Letzte Fehlermeldung (z. B. „Server nicht erreichbar"); Secrets stehen hier nie.</summary>
    public string? LastError { get; private set; }

    /// <summary>Setzt das „eingerichtet"-Merkmal.</summary>
    public void SetConfigured(bool configured)
    {
        lock (_lock)
            IsConfigured = configured;
        RaiseChanged();
    }

    /// <summary>Markiert den Server als erreichbar (nach erfolgreichem Aufruf).</summary>
    public void MarkServerReachable(DateTime serverTimeUtc)
    {
        lock (_lock)
        {
            ServerReachable = true;
            LastServerContactUtc = serverTimeUtc;
            LastError = null;
        }
        RaiseChanged();
    }

    /// <summary>Markiert den Server als nicht erreichbar samt (secret-freier) Meldung.</summary>
    public void MarkServerUnreachable(string message)
    {
        lock (_lock)
        {
            ServerReachable = false;
            LastError = message;
        }
        RaiseChanged();
    }

    /// <summary>Legt (falls nötig) einen Spiel-Eintrag an und ordnet ihm den Ordner zu.</summary>
    public void EnsureGame(GameKey game, string? folder = null, long? baseRevision = null)
    {
        ArgumentNullException.ThrowIfNull(game);
        lock (_lock)
        {
            var view = GetOrCreate(game);
            if (folder is not null) view.FolderPath = folder;
            if (baseRevision is not null) view.BaseRevision = baseRevision.Value;
            // Ein echt verwaltetes Spiel ist nicht (mehr) „übersprungen".
            view.IsSkipped = false;
            view.SkipReason = null;
        }
        RaiseChanged();
    }

    /// <summary>
    /// Aktualisiert den Status eines Spiels. <paramref name="action"/> (falls gesetzt)
    /// wird mit Zeitstempel als „letzte Aktion" hinterlegt.
    /// </summary>
    public void SetStatus(
        GameKey game,
        SyncStatus status,
        string? action = null,
        string? folder = null,
        long? baseRevision = null)
    {
        ArgumentNullException.ThrowIfNull(game);
        lock (_lock)
        {
            var view = GetOrCreate(game);
            view.Status = status;
            if (action is not null)
            {
                view.LastAction = action;
                view.LastActionUtc = _nowUtc();
            }
            if (folder is not null) view.FolderPath = folder;
            if (baseRevision is not null) view.BaseRevision = baseRevision.Value;
            // Ein Spiel mit echtem Sync-Status ist nicht (mehr) „übersprungen".
            view.IsSkipped = false;
            view.SkipReason = null;
        }
        RaiseChanged();
    }

    /// <summary>
    /// Ersetzt die Menge der als „übersprungen" markierten Spiele (aus der letzten Erkennung).
    /// Bestehende Skip-Einträge, die nicht mehr in <paramref name="skipped"/> vorkommen, werden
    /// entfernt; echt verwaltete Spiele (mit Ordner/Status) bleiben unangetastet und werden nie
    /// als übersprungen markiert. So bleiben rausgefallene Spiele sichtbar (mit Hinweis), ohne den
    /// echten Zustand zu überschreiben.
    /// </summary>
    public void ReplaceSkipped(IReadOnlyList<(GameKey Game, string Reason)> skipped)
    {
        ArgumentNullException.ThrowIfNull(skipped);
        lock (_lock)
        {
            var newKeys = new HashSet<string>(skipped.Select(s => s.Game.Value), StringComparer.Ordinal);

            // Veraltete Skip-Einträge entfernen (nur reine Skip-Einträge, echte Spiele bleiben).
            var stale = _games.Where(kv => kv.Value.IsSkipped && !newKeys.Contains(kv.Key))
                              .Select(kv => kv.Key).ToList();
            foreach (var key in stale)
                _games.Remove(key);

            foreach (var (game, reason) in skipped)
            {
                // Ist das Spiel inzwischen echt verwaltet (Ordner vorhanden), Vorrang für den echten
                // Eintrag – nicht als übersprungen markieren.
                if (_games.TryGetValue(game.Value, out var existing) && !existing.IsSkipped)
                    continue;
                var view = GetOrCreate(game);
                view.IsSkipped = true;
                view.SkipReason = reason;
                view.FolderPath = null;
            }
        }
        RaiseChanged();
    }

    /// <summary>Aktueller Status eines Spiels oder <c>null</c>, wenn unbekannt.</summary>
    public SyncStatus? GetStatus(GameKey game)
    {
        ArgumentNullException.ThrowIfNull(game);
        lock (_lock)
            return _games.TryGetValue(game.Value, out var v) ? v.Status : null;
    }

    /// <summary>Unveränderliche Momentaufnahme aller Spiel-Zustände (für die GUI).</summary>
    public IReadOnlyList<GameStatusView> SnapshotGames()
    {
        lock (_lock)
            return _games.Values.Select(v => v.Clone()).ToList();
    }

    /// <summary>
    /// Meldet eine echte, abgeschlossene Sync-Aktion und feuert <see cref="SyncActivityOccurred"/>
    /// (Zeitstempel über den injizierten <c>nowUtc</c>-Delegaten). Ausschließlich aus den echten
    /// Aktions-Pfaden der <see cref="SyncEngine"/> aufgerufen – nie bei NoOp/reinem Statuswechsel.
    /// </summary>
    public void NotifySyncActivity(GameKey game, SyncActivityKind kind)
    {
        ArgumentNullException.ThrowIfNull(game);
        SyncActivityOccurred?.Invoke(this, new SyncActivity(game, kind, _nowUtc()));
    }

    private GameStatusView GetOrCreate(GameKey game)
    {
        if (!_games.TryGetValue(game.Value, out var view))
        {
            view = new GameStatusView(game);
            _games[game.Value] = view;
        }
        return view;
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
