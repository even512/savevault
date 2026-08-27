using System.ComponentModel;
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

    /// <summary>Aktueller Status (für Aktionslogik, z. B. Konflikt erkennen).</summary>
    public SyncStatus Status { get; private set; }

    /// <summary>Übernimmt einen frischen Snapshot in die Zeile.</summary>
    public void Update(GameStatusView view)
    {
        Status = view.Status;
        DisplayName = view.DisplayName;
        StatusLabel = StatusVisuals.LabelFor(view.Status);
        StatusBrush = StatusVisuals.BrushFor(view.Status);
        FolderText = string.IsNullOrWhiteSpace(view.FolderPath) ? "Kein Ordner zugeordnet" : view.FolderPath!;

        var action = string.IsNullOrWhiteSpace(view.LastAction) ? null : view.LastAction!;
        var time = RelativeTime.Format(view.LastActionUtc);
        LastActionText = action is null
            ? (view.LastActionUtc is null ? "Noch keine Aktion" : time)
            : (view.LastActionUtc is null ? action : $"{action} · {time}");

        ConflictVisibility = view.Status == SyncStatus.Conflict ? Visibility.Visible : Visibility.Collapsed;
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
