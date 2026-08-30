using System.Windows;

namespace SaveVault.Client;

/// <summary>
/// Bestätigungsdialog vor dem Wiederherstellen einer älteren Revision. Reiner Ja/Nein-Dialog
/// im dunklen Design-Geist – meldet über <see cref="Window.DialogResult"/> zurück, ob der
/// Nutzer das Überschreiben des lokalen Standes bestätigt hat. Führt selbst keine Aktion aus.
/// </summary>
public partial class RestoreDialog : Window
{
    public RestoreDialog(string gameName, long revisionNumber, string revisionDate)
    {
        InitializeComponent();
        MessageText.Text =
            $"Aktuellen Stand von »{gameName}« mit Version {revisionNumber} vom {revisionDate} überschreiben?\n\n" +
            "Der jetzige lokale Stand wird ersetzt.";
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
