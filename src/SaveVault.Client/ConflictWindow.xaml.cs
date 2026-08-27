using System.Windows;
using System.Windows.Input;
using SaveVault.Client.Services;
using SaveVault.Client.Ui;
using SaveVault.Core.Api;
using SaveVault.Core.Models;

namespace SaveVault.Client;

/// <summary>
/// Modaler Konflikt-Dialog: zeigt die beteiligten Fassungen mit <b>echten</b> Revisions-Feldern
/// (Zeitpunkt, Größe, Dateien, Prüfsumme = ManifestHash) nebeneinander und löst den Konflikt
/// über den <see cref="ClientAgent"/> (ein Gerät gewinnt oder beide behalten). „Dieses Gerät"
/// wird anhand von <see cref="ClientAgent.CurrentDeviceId"/> markiert.
/// </summary>
public partial class ConflictWindow : Window
{
    private readonly ClientAgent _agent;
    private readonly Conflict _conflict;
    private readonly List<ParticipantVm> _participants = new();

    public ConflictWindow(ClientAgent agent, Conflict conflict)
    {
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
        _conflict = conflict ?? throw new ArgumentNullException(nameof(conflict));
        InitializeComponent();

        SubtitleText.Text = $"{conflict.Game.DisplayName} · {conflict.Participants.Count} abweichende Spielstände gefunden";
        Loaded += async (_, _) => await LoadParticipantsAsync();
    }

    private async Task LoadParticipantsAsync()
    {
        IReadOnlyList<RevisionInfo> revisions;
        try
        {
            revisions = await _agent.GetRevisionsAsync(_conflict.Game);
        }
        catch (Exception)
        {
            revisions = Array.Empty<RevisionInfo>();
        }

        var currentId = _agent.CurrentDeviceId;
        var currentName = _agent.CurrentDeviceName;

        foreach (var participant in _conflict.Participants)
        {
            var rev = revisions.FirstOrDefault(r => r.Number == participant.Revision);
            var isThisDevice = !string.IsNullOrEmpty(currentId)
                && string.Equals(currentId, participant.DeviceId, StringComparison.Ordinal);

            var deviceLabel = isThisDevice
                ? (string.IsNullOrWhiteSpace(currentName)
                    ? "Dieses Gerät"
                    : $"{currentName} (dieses Gerät)")
                : $"Gerät {ShortId(participant.DeviceId)}";

            var timeText = rev is not null
                ? $"Rev {rev.Number} · {rev.TimestampUtc.ToLocalTime():dd.MM.yyyy HH:mm}"
                : $"Rev {participant.Revision} · Zeitpunkt unbekannt";
            var sizeText = rev is not null ? ByteSize.Format(rev.TotalBytes) : "—";
            var fileText = rev is not null ? rev.FileCount.ToString() : "—";
            var checksumText = rev is not null ? ShortHash(rev.ManifestHash) : "—";

            _participants.Add(new ParticipantVm(
                participant.DeviceId, participant.Revision, deviceLabel,
                timeText, sizeText, fileText, checksumText, rev is not null));
        }

        ParticipantsList.ItemsSource = _participants;
        LoadingText.Visibility = _participants.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_participants.Count == 0)
            LoadingText.Text = "Keine Konflikt-Details verfügbar (Server nicht erreichbar).";
    }

    // --- Auswahl -------------------------------------------------------------------

    private void OnParticipantClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ParticipantVm selected })
            return;

        foreach (var p in _participants)
            p.IsSelected = ReferenceEquals(p, selected);

        ResolveButton.IsEnabled = true;
    }

    // --- Lösung --------------------------------------------------------------------

    private async void OnResolveClick(object sender, RoutedEventArgs e)
    {
        var winner = _participants.FirstOrDefault(p => p.IsSelected);
        if (winner is null)
        {
            ShowResult("Bitte zuerst eine Fassung auswählen.", ok: false);
            return;
        }

        var request = new ResolveConflictRequest(
            ConflictResolutionKind.KeepDevice, winner.DeviceId, winner.Revision);
        await ApplyAsync(request, $"Fassung von »{winner.DeviceLabel}« übernommen.");
    }

    private async void OnKeepBothClick(object sender, RoutedEventArgs e)
    {
        var request = new ResolveConflictRequest(ConflictResolutionKind.KeepBoth);
        await ApplyAsync(request, "Beide Fassungen werden behalten (umbenannt).");
    }

    private async Task ApplyAsync(ResolveConflictRequest request, string successMessage)
    {
        SetBusy(true);
        try
        {
            var ok = await _agent.ResolveConflictAsync(_conflict.Id, request);
            if (ok)
            {
                ShowResult(successMessage, ok: true);
                DialogResult = true;
                Close();
            }
            else
            {
                ShowResult("Konflikt konnte nicht gelöst werden (Server nicht erreichbar oder abgelehnt).", ok: false);
            }
        }
        catch (Exception ex)
        {
            ShowResult("Konflikt konnte nicht gelöst werden: " + ex.Message, ok: false);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    // --- Helfer --------------------------------------------------------------------

    private void SetBusy(bool busy)
    {
        ResolveButton.IsEnabled = !busy && _participants.Any(p => p.IsSelected);
        KeepBothButton.IsEnabled = !busy;
    }

    private void ShowResult(string message, bool ok)
    {
        ResultText.Text = message;
        ResultText.Visibility = Visibility.Visible;
        ResultText.Foreground = ok ? StatusVisuals.Synced : StatusVisuals.Error;
    }

    private static string ShortId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return "unbekannt";
        return id.Length <= 8 ? id : id.Substring(0, 8);
    }

    private static string ShortHash(string? hash)
    {
        if (string.IsNullOrWhiteSpace(hash))
            return "—";
        return hash.Length <= 12 ? hash : $"{hash.Substring(0, 8)}…{hash.Substring(hash.Length - 4)}";
    }
}
