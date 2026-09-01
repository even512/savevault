using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using SaveVault.Client.Services;
using SaveVault.Core.Models;

namespace SaveVault.Client.Ui;

/// <summary>
/// Anzeige-Zeile eines Spiels im Status-Fenster. Reine View-Schicht über
/// <see cref="GameStatusView"/>; aktualisiert sich per <see cref="INotifyPropertyChanged"/>,
/// damit die Liste ohne Neuaufbau flüssig bleibt.
/// </summary>
public sealed class GameRow : INotifyPropertyChanged
{
    public GameRow(GameKey game)
    {
        Game = game;
        DisplayName = game.DisplayName;
    }

    /// <summary>Kanonische Spielidentität (zum Wiedererkennen/Andocken von Aktionen).</summary>
    public GameKey Game { get; }

    private string _displayName = "";
    public string DisplayName { get => _displayName; private set => Set(ref _displayName, value); }

    private string _statusLabel = "";
    public string StatusLabel { get => _statusLabel; private set => Set(ref _statusLabel, value); }

    private Brush _statusBrush = Brushes.Gray;
    public Brush StatusBrush { get => _statusBrush; private set => Set(ref _statusBrush, value); }

    private string _folderText = "";
    public string FolderText { get => _folderText; private set => Set(ref _folderText, value); }

    private string _lastActionText = "";
    public string LastActionText { get => _lastActionText; private set => Set(ref _lastActionText, value); }

    private Visibility _conflictVisibility = Visibility.Collapsed;
    public Visibility ConflictVisibility { get => _conflictVisibility; private set => Set(ref _conflictVisibility, value); }

    private Visibility _assignFolderVisibility = Visibility.Collapsed;
    /// <summary>Sichtbarkeit des „Ordner zuordnen"-Buttons (nur bei übersprungenen Spielen).</summary>
    public Visibility AssignFolderVisibility { get => _assignFolderVisibility; private set => Set(ref _assignFolderVisibility, value); }

    private Visibility _openFolderVisibility = Visibility.Collapsed;
    /// <summary>Sichtbarkeit des „Ordner öffnen"-Buttons (nur bei echt verwalteten Spielen mit Ordner).</summary>
    public Visibility OpenFolderVisibility { get => _openFolderVisibility; private set => Set(ref _openFolderVisibility, value); }

    private string? _folderPathRaw;
    /// <summary>Der tatsächliche Save-Ordner-Pfad (roh) oder <c>null</c>, falls keiner zugeordnet ist.</summary>
    public string? FolderPathRaw { get => _folderPathRaw; private set => Set(ref _folderPathRaw, value); }

    private bool _canOpenFolder;
    /// <summary>Ob der zugeordnete Save-Ordner aktuell existiert (steuert die „Ordner öffnen"-Aktivierung).</summary>
    public bool CanOpenFolder { get => _canOpenFolder; private set => Set(ref _canOpenFolder, value); }

    private bool _needsAttention;
    /// <summary>Ob dieses Spiel Aufmerksamkeit braucht (Konflikt, Fehler oder übersprungen).</summary>
    public bool NeedsAttention { get => _needsAttention; private set => Set(ref _needsAttention, value); }

    private string _attentionReason = "";
    /// <summary>Kurzer Grund für den Aufmerksamkeits-Bereich (nur wenn <see cref="NeedsAttention"/>).</summary>
    public string AttentionReason { get => _attentionReason; private set => Set(ref _attentionReason, value); }

    private Brush _attentionBrush = Brushes.Gray;
    /// <summary>Statusfarbe für den Aufmerksamkeits-Chip (Konflikt orange, Fehler rot, Skip amber).</summary>
    public Brush AttentionBrush { get => _attentionBrush; private set => Set(ref _attentionBrush, value); }

    /// <summary>Ob dieses Spiel übersprungen wurde und eine manuelle Zuordnung braucht.</summary>
    public bool IsSkipped { get; private set; }

    private bool _isExcluded;
    /// <summary>Ob dieses Spiel dauerhaft vom Sync ausgeschlossen ist („Sync pausieren").</summary>
    public bool IsExcluded { get => _isExcluded; private set => Set(ref _isExcluded, value); }

    private string _pauseLabel = "Sync pausieren";
    /// <summary>
    /// Beschriftung der Pause-Aktion: „Wieder einschließen", wenn ausgeschlossen, sonst
    /// „Sync pausieren".
    /// </summary>
    public string PauseLabel { get => _pauseLabel; private set => Set(ref _pauseLabel, value); }

    /// <summary>Aktueller Status (für Aktionslogik, z. B. Konflikt erkennen).</summary>
    public SyncStatus Status { get; private set; }

    /// <summary>Zeitpunkt der letzten Aktion (UTC) – zur Erkennung, ob die Historie neu zu laden ist.</summary>
    public DateTime? LastActionUtc { get; private set; }

    /// <summary>Übernimmt einen frischen Snapshot in die Zeile.</summary>
    public void Update(GameStatusView view)
    {
        Status = view.Status;
        DisplayName = view.DisplayName;
        IsSkipped = view.IsSkipped;
        LastActionUtc = view.LastActionUtc;
        IsExcluded = view.IsExcluded;
        PauseLabel = view.IsExcluded ? "Wieder einschließen" : "Sync pausieren";

        if (view.IsExcluded)
        {
            // Ausgeschlossen ist ein eigener, orthogonaler Anzeige-Zustand: klar sichtbar,
            // aber KEIN „braucht Aufmerksamkeit". Ordner-Aktionen bleiben nutzbar, damit der
            // Nutzer den gesicherten Ordner weiterhin öffnen kann; ein Konflikt-Löse-Pfad
            // entfällt, weil ausgeschlossene Spiele nicht synchronisiert werden.
            StatusLabel = "Ausgeschlossen";
            StatusBrush = StatusVisuals.Excluded;
            LastActionText = "Vom Sync ausgeschlossen";

            FolderPathRaw = string.IsNullOrWhiteSpace(view.FolderPath) ? null : view.FolderPath;
            FolderText = FolderPathRaw ?? "Kein Ordner zugeordnet";
            OpenFolderVisibility = FolderPathRaw is null ? Visibility.Collapsed : Visibility.Visible;
            CanOpenFolder = FolderPathRaw is not null && SafeDirectoryExists(FolderPathRaw);
            ConflictVisibility = Visibility.Collapsed;
            AssignFolderVisibility = Visibility.Collapsed;

            NeedsAttention = false;
            AttentionReason = "";
            return;
        }

        if (view.IsSkipped)
        {
            // Übersprungenes Spiel: Hinweis statt Sync-Status, „Ordner zuordnen"-Aktion anbieten.
            StatusLabel = "Nicht automatisch erfasst";
            StatusBrush = StatusVisuals.Attention;
            FolderText = string.IsNullOrWhiteSpace(view.SkipReason)
                ? "Kein Ordner zugeordnet – bitte manuell zuordnen."
                : view.SkipReason!;
            LastActionText = "Bei der Erkennung übersprungen";
            ConflictVisibility = Visibility.Collapsed;
            AssignFolderVisibility = Visibility.Visible;
            OpenFolderVisibility = Visibility.Collapsed;
            FolderPathRaw = null;
            CanOpenFolder = false;

            NeedsAttention = true;
            AttentionReason = string.IsNullOrWhiteSpace(view.SkipReason)
                ? "Nicht automatisch erfasst – Ordner zuordnen"
                : view.SkipReason!;
            AttentionBrush = StatusVisuals.Attention;
            return;
        }

        StatusLabel = StatusVisuals.LabelFor(view.Status);
        StatusBrush = StatusVisuals.BrushFor(view.Status);
        FolderText = string.IsNullOrWhiteSpace(view.FolderPath) ? "Kein Ordner zugeordnet" : view.FolderPath!;

        var action = string.IsNullOrWhiteSpace(view.LastAction) ? null : view.LastAction!;
        var time = RelativeTime.Format(view.LastActionUtc);
        LastActionText = action is null
            ? (view.LastActionUtc is null ? "Noch keine Aktion" : time)
            : (view.LastActionUtc is null ? action : $"{action} · {time}");

        ConflictVisibility = view.Status == SyncStatus.Conflict ? Visibility.Visible : Visibility.Collapsed;
        AssignFolderVisibility = Visibility.Collapsed;

        // „Ordner öffnen" nur, wenn ein Ordner zugeordnet ist; aktiviert nur, wenn er auch existiert.
        FolderPathRaw = string.IsNullOrWhiteSpace(view.FolderPath) ? null : view.FolderPath;
        OpenFolderVisibility = FolderPathRaw is null ? Visibility.Collapsed : Visibility.Visible;
        CanOpenFolder = FolderPathRaw is not null && SafeDirectoryExists(FolderPathRaw);

        switch (view.Status)
        {
            case SyncStatus.Conflict:
                NeedsAttention = true;
                AttentionReason = "Konflikt – bitte lösen";
                AttentionBrush = StatusVisuals.Conflict;
                break;
            case SyncStatus.Error:
                NeedsAttention = true;
                AttentionReason = action ?? "Fehler beim Synchronisieren";
                AttentionBrush = StatusVisuals.Error;
                break;
            default:
                NeedsAttention = false;
                AttentionReason = "";
                break;
        }
    }

    private static bool SafeDirectoryExists(string path)
    {
        try { return Directory.Exists(path); }
        catch { return false; }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (Equals(field, value))
            return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
