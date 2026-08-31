using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using SaveVault.Client.Services;
using SaveVault.Client.Ui;
using WinForms = System.Windows.Forms;

namespace SaveVault.Client;

/// <summary>
/// Kurzes, klickdurchlässiges Overlay: das animierte SaveVault-Logo mit winzigem Label
/// („gesichert"/„Konflikt") als Wasserzeichen in einer Bildschirmecke – die Alternative zum
/// lauten Toast, während ein Vollbild-/Randlos-Spiel läuft.
///
/// <para><b>Sicherheit (Spec-Auflage):</b> das Fenster wird per Interop mit
/// <c>WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW</c> versehen – es ist
/// klickdurchlässig, stiehlt nie Fokus/Eingabe und taucht nicht in Alt-Tab auf. Es zeigt
/// ausschließlich Logo + Label aus <b>festem</b> Vokabular; es fließen keine Fremd-/Server-
/// Daten in die Ausgabe.</para>
///
/// <para>Das Fenster blendet sanft ein/aus (~2,5 s) und <b>schließt sich danach selbst</b>
/// (kein Dauer-Overlay, kein Fenster-Stapel). Das Logo wird ohne GDI-Handle-Leak eingebunden:
/// das <c>System.Drawing.Bitmap</c> wird als PNG in einen Speicherstrom geschrieben und als
/// eingefrorene <see cref="BitmapImage"/> geladen (kein <c>GetHbitmap</c>/kein HBITMAP).</para>
/// </summary>
public partial class WatermarkWindow : Window
{
    private readonly WatermarkCorner _corner;

    /// <summary>
    /// Erzeugt das Overlay mit dem festen Label-Text und der gewünschten Ecke. Das Fenster
    /// wird erst mit <see cref="Window.Show()"/> sichtbar (und beginnt dann seine Animation).
    /// </summary>
    public WatermarkWindow(string label, WatermarkCorner corner)
    {
        _corner = corner;
        InitializeComponent();

        LabelText.Text = label ?? string.Empty;
        // Logo scharf bei 96 px rendern und ohne GDI-Handle als eingefrorene ImageSource laden.
        LogoImage.Source = RenderLogo(96);

        Loaded += OnLoaded;
    }

    /// <summary>
    /// Setzt die erweiterten Fensterstile: klickdurchlässig (<c>WS_EX_TRANSPARENT</c>), nie
    /// aktivierend (<c>WS_EX_NOACTIVATE</c>) und aus Alt-Tab ausgeblendet
    /// (<c>WS_EX_TOOLWINDOW</c>). Fehler beim Interop werden verschluckt – das Overlay bleibt
    /// im schlimmsten Fall ein normales, aber durch <c>IsHitTestVisible=False</c> weiterhin
    /// nicht-interagierbares Fenster.
    /// </summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero)
                return;
            var ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        }
        catch
        {
            // Interop-Fehler dürfen das Overlay nie zum Absturz bringen.
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        PositionInCorner();
        StartAnimation();
    }

    /// <summary>
    /// Positioniert das Fenster mit ~24 px Rand in der gewählten Ecke der Arbeitsfläche des
    /// Monitors, auf dem das Vordergrundfenster (das Spiel) liegt – Rückfall Primärmonitor.
    /// Die Monitor-Geometrie kommt in Geräte-Pixeln; sie wird über die DPI-Transformation des
    /// Fensters in geräteunabhängige Einheiten (WPF-Koordinaten) umgerechnet.
    /// </summary>
    private void PositionInCorner()
    {
        const double margin = 24;
        try
        {
            var screen = ForegroundScreen();
            var wa = screen.WorkingArea; // Geräte-Pixel

            // Geräte-Pixel → WPF-DIP (berücksichtigt DPI-Skalierung).
            double left = wa.Left, top = wa.Top, right = wa.Right, bottom = wa.Bottom;
            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget is not null)
            {
                var m = source.CompositionTarget.TransformFromDevice;
                var tl = m.Transform(new Point(wa.Left, wa.Top));
                var br = m.Transform(new Point(wa.Right, wa.Bottom));
                left = tl.X; top = tl.Y; right = br.X; bottom = br.Y;
            }

            var w = ActualWidth;
            var h = ActualHeight;

            (Left, Top) = _corner switch
            {
                WatermarkCorner.TopLeft => (left + margin, top + margin),
                WatermarkCorner.TopRight => (right - w - margin, top + margin),
                WatermarkCorner.BottomLeft => (left + margin, bottom - h - margin),
                // BottomRight ist der Default.
                _ => (right - w - margin, bottom - h - margin),
            };
        }
        catch
        {
            // Positionierung schlägt nie fehl: im Zweifel WPF-Standardplatzierung belassen.
        }
    }

    /// <summary>Bildschirm des Vordergrundfensters oder – als Rückfall – der Primärbildschirm.</summary>
    private static WinForms.Screen ForegroundScreen()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd != IntPtr.Zero)
                return WinForms.Screen.FromHandle(hwnd);
        }
        catch
        {
            // Rückfall unten.
        }
        return WinForms.Screen.PrimaryScreen ?? WinForms.Screen.AllScreens[0];
    }

    /// <summary>
    /// Fade-in → halten → Fade-out über ~2,5 s (Ziel-Opacity ~0.85). Am Ende schließt sich das
    /// Fenster selbst (kein Dauer-Overlay, kein Leak).
    /// </summary>
    private void StartAnimation()
    {
        const double peak = 0.85;
        var anim = new DoubleAnimationUsingKeyFrames();
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(peak, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.4))));
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(peak, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2.1))));
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2.5))));
        anim.Completed += (_, _) =>
        {
            try { Close(); }
            catch { /* schon geschlossen */ }
        };
        BeginAnimation(OpacityProperty, anim);
    }

    /// <summary>
    /// Rendert das Logo als eingefrorene <see cref="BitmapImage"/> – über einen PNG-Speicherstrom
    /// statt über ein HBITMAP, sodass <b>kein GDI-Handle geleakt</b> wird. Das gezeichnete
    /// <c>System.Drawing.Bitmap</c> wird deterministisch entsorgt.
    /// </summary>
    private static ImageSource RenderLogo(int size)
    {
        using var bmp = TrayIconFactory.RenderBitmap(size);
        using var ms = new MemoryStream();
        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        ms.Position = 0;

        var img = new BitmapImage();
        img.BeginInit();
        img.CacheOption = BitmapCacheOption.OnLoad; // Stream sofort auslesen …
        img.StreamSource = ms;
        img.EndInit();
        img.Freeze(); // … dann ist der Stream entbehrlich und das Bild threadsicher.
        return img;
    }

    // --- Win32-Interop: klickdurchlässiges, nicht-aktivierendes Tool-Fenster -----------------

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}
