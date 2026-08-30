using System.Windows;
using SaveVault.Core.Api;
using SaveVault.Core.Models;

namespace SaveVault.Client.Ui;

/// <summary>
/// Anzeige-Zeile einer Revision in der Versionshistorie des Detail-Panels. Reine View-Schicht
/// über <see cref="RevisionInfo"/>; die Werte werden beim Laden einmal aufbereitet (immutabel).
/// </summary>
public sealed class RevisionRow
{
    public RevisionRow(GameKey game, RevisionInfo info, string? currentDeviceId)
    {
        Game = game;
        Number = info.Number;
        Title = $"Version {info.Number}";

        var time = RelativeTime.Format(info.TimestampUtc);
        var size = ByteSize.Format(info.TotalBytes);
        var files = info.FileCount == 1 ? "1 Datei" : $"{info.FileCount} Dateien";
        MetaText = $"{time} · {size} · {files}";

        OriginText = !string.IsNullOrWhiteSpace(currentDeviceId)
                     && string.Equals(info.DeviceId, currentDeviceId, StringComparison.Ordinal)
            ? "Herkunft: dieses Gerät"
            : $"Herkunft: {info.DeviceId}";

        ConflictVisibility = info.IsConflict ? Visibility.Visible : Visibility.Collapsed;

        // Für den Bestätigungsdialog: lokales Datum der Revision (TT.MM.JJJJ).
        LocalDate = info.TimestampUtc.ToLocalTime().ToString("dd.MM.yyyy");
    }

    /// <summary>Spiel, zu dem diese Revision gehört (für die Restore-Aktion).</summary>
    public GameKey Game { get; }

    /// <summary>Revisionsnummer (Ziel der Wiederherstellung).</summary>
    public long Number { get; }

    /// <summary>Titel („Version N").</summary>
    public string Title { get; }

    /// <summary>Zeit · Größe · Dateizahl.</summary>
    public string MetaText { get; }

    /// <summary>Herkunftsgerät der Revision.</summary>
    public string OriginText { get; }

    /// <summary>Sichtbarkeit des Konflikt-Kennzeichens.</summary>
    public Visibility ConflictVisibility { get; }

    /// <summary>Lokales Datum der Revision für den Bestätigungstext.</summary>
    public string LocalDate { get; }
}
