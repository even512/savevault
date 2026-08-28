using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace SaveVault.Client.Ui;

/// <summary>
/// Erzeugt das Tray-Symbol zur Laufzeit (kein eingebettetes .ico nötig): eine abgerundete
/// Teal-Kachel mit stilisiertem Speicher-Symbol im Design-Geist der App. Isoliert von WPF
/// (nur <c>System.Drawing</c>), damit keine Typ-Mehrdeutigkeiten entstehen.
/// </summary>
public static class TrayIconFactory
{
    /// <summary>
    /// Baut ein quadratisches Icon der Kantenlänge <paramref name="size"/> (Default 32; das
    /// Tray nutzt weiterhin 32 und bleibt damit optisch unverändert). Die Geometrie ist im
    /// 32er-Designraster definiert und wird über <see cref="Graphics.ScaleTransform(float,float)"/>
    /// gleichmäßig skaliert – bei 32 ist der Skalierungsfaktor 1, also pixelgleich zum
    /// bisherigen Symbol. Der Aufrufer hält das Icon und entsorgt es beim Beenden.
    /// </summary>
    public static Icon Create(int size = 32)
    {
        using var bmp = RenderBitmap(size);
        var hicon = bmp.GetHicon();
        // Kopie erzeugen, die vom Bitmap-Handle unabhängig ist.
        using var tmp = Icon.FromHandle(hicon);
        return (Icon)tmp.Clone();
    }

    /// <summary>
    /// Zeichnet das Symbol als 32-bit-ARGB-Bitmap der Kantenlänge <paramref name="size"/>.
    /// Wird vom Tray (indirekt über <see cref="Create(int)"/>) und vom Einmal-Icon-Generator
    /// genutzt, damit es genau eine Zeichenquelle für alle Größen gibt.
    /// </summary>
    public static Bitmap RenderBitmap(int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.Clear(Color.Transparent);

        // Alle Koordinaten im 32er-Designraster; für andere Größen gleichmäßig skalieren.
        g.ScaleTransform(size / 32f, size / 32f);

        // Abgerundete Teal-Kachel als Hintergrund.
        using (var bg = new SolidBrush(ColorFromHex("#2A9D93")))
        using (var path = RoundedRect(1, 1, 30, 30, 7))
            g.FillPath(bg, path);

        // Stilisierte Speicher-/Disk-Form in Weiß.
        using (var white = new SolidBrush(Color.FromArgb(235, 255, 255, 255)))
        {
            // Oberer „Schieber".
            using var top = RoundedRect(10, 7, 12, 6, 1);
            g.FillPath(white, top);
            // Unteres „Label"-Feld.
            using var body = RoundedRect(9, 16, 14, 9, 2);
            g.FillPath(white, body);
        }
        using (var teal = new SolidBrush(ColorFromHex("#2A9D93")))
        {
            g.FillRectangle(teal, 12, 18, 8, 2);
            g.FillRectangle(teal, 12, 21, 5, 2);
        }

        return bmp;
    }

    private static GraphicsPath RoundedRect(int x, int y, int w, int h, int r)
    {
        var path = new GraphicsPath();
        var d = r * 2;
        path.AddArc(x, y, d, d, 180, 90);
        path.AddArc(x + w - d, y, d, d, 270, 90);
        path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
        path.AddArc(x, y + h - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static Color ColorFromHex(string hex)
    {
        hex = hex.TrimStart('#');
        var r = Convert.ToInt32(hex.Substring(0, 2), 16);
        var g = Convert.ToInt32(hex.Substring(2, 2), 16);
        var b = Convert.ToInt32(hex.Substring(4, 2), 16);
        return Color.FromArgb(255, r, g, b);
    }
}
