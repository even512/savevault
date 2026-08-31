using System.Windows;
using System.Windows.Controls;
using SaveVault.Client.Services;

namespace SaveVault.Client;

/// <summary>
/// Einstellungen und Server-Kopplung. Liest/schreibt die lokale <see cref="ClientConfig"/>
/// über den öffentlichen <see cref="ClientConfigStore"/> und stößt das Pairing über den
/// <see cref="ClientAgent"/> an. Der Geräte-Token wird bewusst <b>nie</b> angezeigt.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly ClientAgent _agent;
    private readonly ClientConfigStore _configStore = new(new AppPaths());

    // Reihenfolge der Ecken-Auswahl (Index ↔ Enum), mit deutschen Labels.
    private static readonly (WatermarkCorner Corner, string Label)[] Corners =
    {
        (WatermarkCorner.BottomRight, "Unten rechts"),
        (WatermarkCorner.TopRight, "Oben rechts"),
        (WatermarkCorner.TopLeft, "Oben links"),
        (WatermarkCorner.BottomLeft, "Unten links"),
    };

    public SettingsWindow(ClientAgent agent)
    {
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
        InitializeComponent();

        // Ecken-Auswahl einmalig befüllen (feste, lokalisierte Labels).
        foreach (var (_, label) in Corners)
            CornerCombo.Items.Add(label);

        LoadFields();
    }

    private void LoadFields()
    {
        var config = _configStore.Load();
        ServerUrlBox.Text = config.ServerUrl ?? "";
        DeviceNameBox.Text = config.DeviceName ?? Environment.MachineName;
        IntervalBox.Text = config.SyncIntervalSeconds.ToString();
        AutostartCheck.IsChecked = config.AutostartEnabled;
        ToastsCheck.IsChecked = config.ToastsEnabled;
        NotifyTransfersCheck.IsChecked = config.NotifyTransfers;
        NotifyConflictsCheck.IsChecked = config.NotifyConflicts;
        NotificationSoundCheck.IsChecked = config.NotificationSound;
        WatermarkCheck.IsChecked = config.GameWatermarkEnabled;

        var cornerIndex = Array.FindIndex(Corners, c => c.Corner == config.WatermarkCorner);
        CornerCombo.SelectedIndex = cornerIndex >= 0 ? cornerIndex : 0;

        UpdateSubOptionsEnabled();
        // Pairing-Code bleibt leer; der Token wird nicht geladen/angezeigt.
    }

    /// <summary>Graut die Unter-Optionen aus, wenn der Master „Benachrichtigungen anzeigen" aus ist.</summary>
    private void OnMasterToggled(object sender, RoutedEventArgs e) => UpdateSubOptionsEnabled();

    private void UpdateSubOptionsEnabled()
    {
        var on = ToastsCheck.IsChecked == true;
        // Die Steuerelemente könnten während InitializeComponent noch null sein.
        if (NotifyTransfersCheck is null)
            return;
        NotifyTransfersCheck.IsEnabled = on;
        NotifyConflictsCheck.IsEnabled = on;
        NotificationSoundCheck.IsEnabled = on;
        WatermarkCheck.IsEnabled = on;
        CornerCombo.IsEnabled = on;
    }

    // --- Kopplung ------------------------------------------------------------------

    private async void OnPairClick(object sender, RoutedEventArgs e)
    {
        var serverUrl = ServerUrlBox.Text.Trim();
        var code = PairingCodeBox.Text.Trim();
        var deviceName = DeviceNameBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(serverUrl))
        {
            ShowPairResult("Bitte eine Server-URL angeben.", ok: false);
            return;
        }
        if (string.IsNullOrWhiteSpace(code))
        {
            ShowPairResult("Bitte den Pairing-Code angeben.", ok: false);
            return;
        }

        PairButton.IsEnabled = false;
        ShowPairResult("Kopple mit dem Server…", ok: true, neutral: true);
        try
        {
            var result = await _agent.PairAsync(serverUrl, code, deviceName);
            if (result.Success)
            {
                PairingCodeBox.Clear();
                ShowPairResult("Kopplung erfolgreich. Dieses Gerät ist jetzt verbunden.", ok: true);
                LoadFields();
            }
            else
            {
                ShowPairResult(result.ErrorMessage ?? "Kopplung fehlgeschlagen.", ok: false);
            }
        }
        catch (Exception ex)
        {
            ShowPairResult("Kopplung fehlgeschlagen: " + ex.Message, ok: false);
        }
        finally
        {
            PairButton.IsEnabled = true;
        }
    }

    // --- Speichern (Gerätename + Intervall) ----------------------------------------

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var deviceName = DeviceNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(deviceName))
            deviceName = Environment.MachineName;

        if (!int.TryParse(IntervalBox.Text.Trim(), out var seconds))
        {
            ShowSaveResult("Das Sync-Intervall muss eine Zahl (Sekunden) sein.", ok: false);
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
            config.AutostartEnabled = AutostartCheck.IsChecked == true;
            config.ToastsEnabled = ToastsCheck.IsChecked == true;
            config.NotifyTransfers = NotifyTransfersCheck.IsChecked == true;
            config.NotifyConflicts = NotifyConflictsCheck.IsChecked == true;
            config.NotificationSound = NotificationSoundCheck.IsChecked == true;
            config.GameWatermarkEnabled = WatermarkCheck.IsChecked == true;
            var idx = CornerCombo.SelectedIndex;
            config.WatermarkCorner = idx >= 0 && idx < Corners.Length
                ? Corners[idx].Corner
                : WatermarkCorner.BottomRight;
            _configStore.Save(config);
            IntervalBox.Text = seconds.ToString();

            // Autostart-Zustand sofort in der Registry anwenden (fehlertolerant, kein Abbruch).
            AutostartService.Apply(config.AutostartEnabled);

            // Änderungen wirksam machen: Dienst kurz neu starten (kein Fehler, wenn nicht eingerichtet).
            await _agent.StopAsync();
            await _agent.StartAsync();

            ShowSaveResult("Gespeichert.", ok: true);
        }
        catch (Exception ex)
        {
            ShowSaveResult("Speichern fehlgeschlagen: " + ex.Message, ok: false);
        }
        finally
        {
            SaveButton.IsEnabled = true;
        }
    }

    // --- Anzeige-Helfer ------------------------------------------------------------

    private void ShowPairResult(string message, bool ok, bool neutral = false)
        => SetResult(PairResultText, message, ok, neutral);

    private void ShowSaveResult(string message, bool ok, bool neutral = false)
        => SetResult(SaveResultText, message, ok, neutral);

    private static void SetResult(TextBlock target, string message, bool ok, bool neutral)
    {
        target.Text = message;
        target.Visibility = Visibility.Visible;
        target.Foreground = neutral
            ? Ui.StatusVisuals.Offline
            : (ok ? Ui.StatusVisuals.Synced : Ui.StatusVisuals.Error);
    }
}
