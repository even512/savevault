using System.ComponentModel;
using System.Windows;
using System.Windows.Media;

namespace SaveVault.Client.Ui;

/// <summary>
/// Anzeige einer an einem Konflikt beteiligten Fassung (ein Gerät + dessen Revision) mit
/// echten Revisions-Feldern (Zeitpunkt, Größe, Dateien, Prüfsumme). Auswählbar; die
/// Auswahl markiert den Gewinner für die Konfliktlösung.
/// </summary>
public sealed class ParticipantVm : INotifyPropertyChanged
{
    public ParticipantVm(string deviceId, long revision, string deviceLabel,
        string timeText, string sizeText, string fileCountText, string checksumText,
        bool revisionKnown)
    {
        DeviceId = deviceId;
        Revision = revision;
        DeviceLabel = deviceLabel;
        TimeText = timeText;
        SizeText = sizeText;
        FileCountText = fileCountText;
        ChecksumText = checksumText;
        RevisionKnown = revisionKnown;
    }

    /// <summary>Geräte-ID (für die Lösung: Gewinner).</summary>
    public string DeviceId { get; }

    /// <summary>Revisionsnummer dieses Standes (für die Lösung).</summary>
    public long Revision { get; }

    /// <summary>Ob zu dieser Teilnehmer-Revision echte Metadaten geladen werden konnten.</summary>
    public bool RevisionKnown { get; }

    public string DeviceLabel { get; }
    public string TimeText { get; }
    public string SizeText { get; }
    public string FileCountText { get; }
    public string ChecksumText { get; }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
                return;
            _isSelected = value;
            OnChanged(nameof(IsSelected));
            OnChanged(nameof(BorderBrush));
            OnChanged(nameof(BorderThickness));
            OnChanged(nameof(SelectedMarkVisibility));
        }
    }

    public Brush BorderBrush => _isSelected ? StatusVisuals.Syncing : Frozen("#34363F");
    public Thickness BorderThickness => _isSelected ? new Thickness(2) : new Thickness(1);
    public Visibility SelectedMarkVisibility => _isSelected ? Visibility.Visible : Visibility.Collapsed;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private static SolidColorBrush Frozen(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }
}
