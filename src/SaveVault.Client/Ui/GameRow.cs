using System.ComponentModel;
using System.IO;
using System.Threading;
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
        _coverFallbackBrush = BuildFallbackBrush(game.Value);
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

    private string _pauseLabel = "Hochladen deaktivieren";
    /// <summary>
    /// Beschriftung der Upload-Aktion: „Hochladen wieder aktivieren", wenn deaktiviert, sonst
    /// „Hochladen deaktivieren".
    /// </summary>
    public string PauseLabel { get => _pauseLabel; private set => Set(ref _pauseLabel, value); }

    private bool _isShared;
    /// <summary>Ob dieses Spiel „Synchron" (geräteübergreifend geteilt) ist, sonst „Lokal".</summary>
    public bool IsShared
    {
        get => _isShared;
        private set
        {
            if (Set(ref _isShared, value))
            {
                OnChanged(nameof(ShareLabel));
                OnChanged(nameof(CanShare));
            }
        }
    }

    /// <summary>Beschriftung der Teilen-Aktion: Zustand „Synchron" bzw. Umschalt-Angebot.</summary>
    public string ShareLabel => IsShared ? "Geteilt (synchron)" : "Über Geräte synchronisieren";

    /// <summary>Ob der Teilen-Umschalter aktiv ist (nur solange „Lokal"; Rückschalten ist v1 nicht vorgesehen).</summary>
    public bool CanShare => !IsShared;

    private Visibility _shareVisibility = Visibility.Collapsed;
    /// <summary>Sichtbarkeit des Teilen-Umschalters (nur bei echt verwalteten, nicht deaktivierten Spielen).</summary>
    public Visibility ShareVisibility { get => _shareVisibility; private set => Set(ref _shareVisibility, value); }

    private Visibility _errorVisibility = Visibility.Collapsed;
    /// <summary>Sichtbarkeit des Fehler-Banners (nur bei Sync-Fehler).</summary>
    public Visibility ErrorVisibility { get => _errorVisibility; private set => Set(ref _errorVisibility, value); }

    private string _errorMessage = "";
    /// <summary>Text des Fehler-Banners (nur bei Sync-Fehler).</summary>
    public string ErrorMessage { get => _errorMessage; private set => Set(ref _errorMessage, value); }

    // --- Cover (lazy) -----------------------------------------------------------------

    private readonly Brush _coverFallbackBrush;
    /// <summary>
    /// Deterministischer Farbverlauf als Cover-Ersatz (aus dem Spiel-Schlüssel abgeleitet, stabil).
    /// Wird angezeigt, solange kein echtes Cover geladen ist oder der Server keins liefert.
    /// </summary>
    public Brush CoverFallbackBrush => _coverFallbackBrush;

    private ImageSource? _cover;
    /// <summary>Das echte Box-Art-Bild (lazy geladen) oder <c>null</c> (dann greift der Fallback).</summary>
    public ImageSource? Cover { get => _cover; private set => Set(ref _cover, value); }

    private bool _hasCover;
    /// <summary>Ob ein echtes Cover geladen wurde (steuert Bild ↔ Farbverlauf).</summary>
    public bool HasCover
    {
        get => _hasCover;
        private set
        {
            if (Set(ref _hasCover, value))
            {
                OnChanged(nameof(CoverImageVisibility));
                OnChanged(nameof(CoverFallbackVisibility));
            }
        }
    }

    /// <summary>Sichtbarkeit des echten Cover-Bildes.</summary>
    public Visibility CoverImageVisibility => HasCover ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Sichtbarkeit des Farbverlauf-Fallbacks.</summary>
    public Visibility CoverFallbackVisibility => HasCover ? Visibility.Collapsed : Visibility.Visible;

    private bool _coverRequested;

    /// <summary>
    /// Fordert das echte Cover <b>einmalig und lazy</b> an (ein Spiel je Aufruf) und füllt bei
    /// Erfolg <see cref="Cover"/>/<see cref="HasCover"/>. Liefert der Server (noch) keins, bleibt
    /// der Farbverlauf-Fallback und ein späterer Aufruf darf es erneut versuchen. Muss vom
    /// UI-Thread aufgerufen werden (die Fortsetzung setzt die Properties dort). Wirft nie.
    /// </summary>
    public async Task EnsureCoverAsync(CoverCache covers, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(covers);
        if (HasCover || _coverRequested)
            return;
        _coverRequested = true;
        try
        {
            var image = await covers.GetCoverAsync(Game, ct);
            if (image is not null)
            {
                Cover = image;
                HasCover = true;
            }
            else
            {
                // Kein Cover (noch nicht verbunden / kein Bild): späteren Versuch erlauben.
                _coverRequested = false;
            }
        }
        catch
        {
            _coverRequested = false; // Fallback bleibt sichtbar; nie ein Absturz.
        }
    }

    // --- Größe (best effort) ----------------------------------------------------------

    private string _sizeText = "";
    /// <summary>Belegter Speicher des Spiels (z. B. „142 MB"); leer, wenn unbekannt.</summary>
    public string SizeText
    {
        get => _sizeText;
        private set
        {
            if (Set(ref _sizeText, value))
                OnChanged(nameof(SizeVisibility));
        }
    }

    /// <summary>Sichtbarkeit der Größenzeile (nur wenn eine Größe bekannt ist).</summary>
    public Visibility SizeVisibility => string.IsNullOrEmpty(SizeText) ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Setzt die best-effort-Größe aus einem bekannten Byte-Wert (0/negativ = ausblenden).</summary>
    public void SetSize(long bytes)
        => SizeText = bytes > 0 ? ByteSize.Format(bytes) : "";

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
        IsShared = view.IsShared;
        PauseLabel = view.IsExcluded ? "Hochladen wieder aktivieren" : "Hochladen deaktivieren";

        if (view.IsExcluded)
        {
            // „Hochladen deaktiviert" ist ein eigener, orthogonaler Anzeige-Zustand: klar sichtbar,
            // aber KEIN „braucht Aufmerksamkeit". Ordner-Aktionen bleiben nutzbar, damit der
            // Nutzer den lokalen Ordner weiterhin öffnen kann; Teilen/Konflikt-Pfade entfallen,
            // weil ein nicht hochgeladenes Spiel weder gesichert noch geteilt wird.
            StatusLabel = "Hochladen deaktiviert";
            StatusBrush = StatusVisuals.Excluded;
            LastActionText = "Nur lokal – nicht hochgeladen";

            FolderPathRaw = string.IsNullOrWhiteSpace(view.FolderPath) ? null : view.FolderPath;
            FolderText = FolderPathRaw ?? "Kein Ordner zugeordnet";
            OpenFolderVisibility = FolderPathRaw is null ? Visibility.Collapsed : Visibility.Visible;
            CanOpenFolder = FolderPathRaw is not null && SafeDirectoryExists(FolderPathRaw);
            ConflictVisibility = Visibility.Collapsed;
            AssignFolderVisibility = Visibility.Collapsed;
            ShareVisibility = Visibility.Collapsed;
            ErrorVisibility = Visibility.Collapsed;
            ErrorMessage = "";

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
            ShareVisibility = Visibility.Collapsed;
            FolderPathRaw = null;
            CanOpenFolder = false;
            ErrorVisibility = Visibility.Collapsed;
            ErrorMessage = "";

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
        // Teilen-Umschalter nur bei echt verwalteten Spielen und NICHT bei offenem Konflikt anbieten
        // (sonst würde man einen ungelösten/mehrdeutigen Stand teilen). Erst lösen, dann teilen.
        ShareVisibility = view.Status == SyncStatus.Conflict ? Visibility.Collapsed : Visibility.Visible;

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
                ErrorVisibility = Visibility.Collapsed;
                ErrorMessage = "";
                break;
            case SyncStatus.Error:
                NeedsAttention = true;
                AttentionReason = action ?? "Fehler beim Synchronisieren";
                AttentionBrush = StatusVisuals.Error;
                ErrorVisibility = Visibility.Visible;
                ErrorMessage = action ?? "Sync fehlgeschlagen.";
                break;
            default:
                NeedsAttention = false;
                AttentionReason = "";
                ErrorVisibility = Visibility.Collapsed;
                ErrorMessage = "";
                break;
        }
    }

    private static bool SafeDirectoryExists(string path)
    {
        try { return Directory.Exists(path); }
        catch { return false; }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (Equals(field, value))
            return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }

    private void OnChanged(string name)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>
    /// Baut einen stabilen, dezenten Farbverlauf als Cover-Ersatz. Die Grundfarbe wird
    /// deterministisch aus dem Spiel-Schlüssel abgeleitet (gleiches Spiel → gleiche Farbe),
    /// diagonal (≈150°) zu einem dunklen Kartenton (#14151D), passend zum Design-Canvas.
    /// </summary>
    private static Brush BuildFallbackBrush(string key)
    {
        // Deterministischer Farbton aus einem stabilen String-Hash (nicht String.GetHashCode,
        // das je Prozesslauf variiert), damit dasselbe Spiel immer dieselbe Farbe erhält.
        var hash = 2166136261u;
        foreach (var ch in key)
        {
            hash ^= ch;
            hash *= 16777619u;
        }
        var hue = hash % 360u;

        var top = FromHsl(hue, 0.45, 0.42);
        var bottom = (Color)ColorConverter.ConvertFromString("#14151D");

        var brush = new LinearGradientBrush(top, bottom, new Point(0, 0), new Point(1, 1));
        brush.Freeze();
        return brush;
    }

    private static Color FromHsl(double h, double s, double l)
    {
        h /= 360.0;
        double r, g, b;
        if (s == 0)
        {
            r = g = b = l;
        }
        else
        {
            var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            var p = 2 * l - q;
            r = HueToRgb(p, q, h + 1.0 / 3.0);
            g = HueToRgb(p, q, h);
            b = HueToRgb(p, q, h - 1.0 / 3.0);
        }
        return Color.FromRgb((byte)Math.Round(r * 255), (byte)Math.Round(g * 255), (byte)Math.Round(b * 255));
    }

    private static double HueToRgb(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        if (t < 1.0 / 6.0) return p + (q - p) * 6 * t;
        if (t < 1.0 / 2.0) return q;
        if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6;
        return p;
    }
}
