using System.Windows;
using SaveVault.Client.Services;
using SaveVault.Client.Ui;

namespace SaveVault.Client;

/// <summary>Welche Fassung beim Teilen gewinnt, wenn schon ein geteilter Stand existiert.</summary>
public enum ShareChoice
{
    /// <summary>Dialog abgebrochen – nichts ändern.</summary>
    Cancel,

    /// <summary>Den vorhandenen geteilten Stand übernehmen (herunterladen).</summary>
    TakeShared,

    /// <summary>Den lokalen Stand als neuen geteilten Stand hochladen.</summary>
    TakeLocal,
}

/// <summary>
/// Modaler Vergleichsdialog beim Umschalten eines Spiels auf „Synchron", wenn bereits ein
/// geteilter Speicherstand existiert. Zeigt lokalen und geteilten Stand mit Kennzahlen und lässt
/// den Nutzer wählen, welcher der geteilte wird (nichts wird ohne Wahl überschrieben).
/// </summary>
public partial class ShareCompareWindow : Window
{
    /// <summary>Die getroffene Wahl (gültig, wenn <see cref="Window.ShowDialog"/> <c>true</c> liefert).</summary>
    public ShareChoice Choice { get; private set; } = ShareChoice.Cancel;

    public ShareCompareWindow(string gameName, ShareProbe probe)
    {
        ArgumentNullException.ThrowIfNull(probe);
        InitializeComponent();

        SubtitleText.Text = $"{gameName}: Wähle, welcher Speicherstand ab jetzt über deine Geräte synchronisiert wird.";

        LocalDeviceText.Text = string.IsNullOrWhiteSpace(probe.Local.DeviceLabel) ? "Dieser PC" : probe.Local.DeviceLabel!;
        LocalSizeText.Text = ByteSize.Format(probe.Local.TotalBytes);
        LocalFilesText.Text = probe.Local.FileCount.ToString();

        var shared = probe.Shared;
        if (shared is not null)
        {
            SharedDeviceText.Text = $"von {ShortDevice(shared.DeviceLabel)}";
            SharedSizeText.Text = ByteSize.Format(shared.TotalBytes);
            SharedFilesText.Text = shared.FileCount.ToString();
            SharedTimeText.Text = shared.WhenUtc is { } utc ? utc.ToLocalTime().ToString("dd.MM.yyyy HH:mm") : "—";
        }
    }

    private void OnTakeSharedClick(object sender, RoutedEventArgs e)
    {
        Choice = ShareChoice.TakeShared;
        DialogResult = true;
        Close();
    }

    private void OnTakeLocalClick(object sender, RoutedEventArgs e)
    {
        Choice = ShareChoice.TakeLocal;
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        Choice = ShareChoice.Cancel;
        DialogResult = false;
        Close();
    }

    private static string ShortDevice(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return "einem anderen Gerät";
        return id.Length <= 8 ? $"Gerät {id}" : $"Gerät {id[..8]}";
    }
}
