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
    /// <summary>Baut ein 32×32-Icon. Der Aufrufer hält es für die App-Lebensdauer und entsorgt es beim Beenden.</summary>
    public static Icon Create()
    {
        using var bmp = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

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
        }

        var hicon = bmp.GetHicon();
        // Kopie erzeugen, die vom Bitmap-Handle unabhängig ist.
        using var tmp = Icon.FromHandle(hicon);
        return (Icon)tmp.Clone();
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
