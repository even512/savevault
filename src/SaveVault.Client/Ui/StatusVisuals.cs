using System.Windows.Media;
using SaveVault.Core.Models;

namespace SaveVault.Client.Ui;

/// <summary>
/// Übersetzt einen <see cref="SyncStatus"/> in die sichtbare Darstellung (Farbe + Label)
/// nach der dunklen Design-Palette der Spec. Reine Anzeige-Zuordnung – die Domäne kennt
/// keine Farben. Alle Brushes sind eingefroren (thread-übergreifend nutzbar).
/// </summary>
public static class StatusVisuals
{
    // Statusfarben (aus Spec/Mockup, oklch → Hex konvertiert).
    public static readonly SolidColorBrush Synced   = Freeze("#56C271");
    public static readonly SolidColorBrush Syncing  = Freeze("#5BC2B8");
    public static readonly SolidColorBrush Conflict = Freeze("#E8944A");
    public static readonly SolidColorBrush Pending  = Freeze("#E3C349");
    public static readonly SolidColorBrush Offline  = Freeze("#7A7C88");
    public static readonly SolidColorBrush Error    = Freeze("#E5615E");

    /// <summary>Farbe für den Statuspunkt/-text eines Spiels.</summary>
    public static Brush BrushFor(SyncStatus status) => status switch
    {
        SyncStatus.Synced   => Synced,
        SyncStatus.Syncing  => Syncing,
        SyncStatus.Conflict => Conflict,
        SyncStatus.Pending  => Pending,
        SyncStatus.Offline  => Offline,
        SyncStatus.Error    => Error,
        _                   => Offline,
    };

    /// <summary>Deutsches Label für den Status.</summary>
    public static string LabelFor(SyncStatus status) => status switch
    {
        SyncStatus.Synced   => "Synchronisiert",
        SyncStatus.Syncing  => "Wird synchronisiert",
        SyncStatus.Conflict => "Konflikt",
        SyncStatus.Pending  => "Ausstehend",
        SyncStatus.Offline  => "Offline",
        SyncStatus.Error    => "Fehler",
        _                   => status.ToString(),
    };

    private static SolidColorBrush Freeze(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
