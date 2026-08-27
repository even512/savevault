using SaveVault.Core.Models;
using SaveVault.Core.Sync;

namespace SaveVault.Client.Services;

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

    internal GameStatusView Clone() => new(Game)
    {
        Status = Status,
        BaseRevision = BaseRevision,
        FolderPath = FolderPath,
        LastAction = LastAction,
        LastActionUtc = LastActionUtc,
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
