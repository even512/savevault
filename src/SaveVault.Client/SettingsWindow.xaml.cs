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

    public SettingsWindow(ClientAgent agent)
    {
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
        InitializeComponent();
        LoadFields();
    }

    private void LoadFields()
    {
        var config = _configStore.Load();
        ServerUrlBox.Text = config.ServerUrl ?? "";
        DeviceNameBox.Text = config.DeviceName ?? Environment.MachineName;
        IntervalBox.Text = config.SyncIntervalSeconds.ToString();
        AutostartCheck.IsChecked = config.AutostartEnabled;
        // Pairing-Code bleibt leer; der Token wird nicht geladen/angezeigt.
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
