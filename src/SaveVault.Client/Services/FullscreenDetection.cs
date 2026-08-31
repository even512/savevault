using System.Runtime.InteropServices;

namespace SaveVault.Client.Services;

/// <summary>
/// Dünne, rein lesende Win32-Abfrage: „Läuft gerade ein Vollbild-/Randlos-Spiel?"
/// Dient der Ausgabe-Weiche (Toast vs. Wasserzeichen vs. still): läuft ein Spiel, sollen die
/// lauten Toasts unterbleiben.
///
/// <para><b>Nie werfen:</b> jeder P/Invoke-/Ausnahmefall liefert <c>false</c> („kein Vollbild")
/// – im Zweifel lieber den Toast zeigen als einen Fehler schlucken. Die Methode ist synchron,
/// hält keine Fenster-/Monitor-Handles (kopiert nur Werte) und braucht kein Polling/keine
/// Timer.</para>
/// </summary>
public static class FullscreenDetection
{
    /// <summary>
    /// <c>true</c>, wenn gerade ein Spiel/eine App im (exklusiven) Vollbild oder im Randlos-
    /// Vollbild den Vordergrund einnimmt. Primär über die Shell-Benachrichtigungs-Zustände,
    /// als Rückfall über die Geometrie des Vordergrundfensters gegen seinen Monitor.
    /// </summary>
    public static bool IsFullscreenAppRunning()
    {
        try
        {
            // 1) Primär: Shell meldet den Benachrichtigungs-Zustand. D3D-Vollbild, Präsentations-
            //    modus und „beschäftigt" gelten als „nicht stören → Spiel/Vollbild läuft".
            if (SHQueryUserNotificationState(out var state) == S_OK)
            {
                switch (state)
                {
                    case QUNS_RUNNING_D3D_FULL_SCREEN:
                    case QUNS_PRESENTATION_MODE:
                    case QUNS_BUSY:
                        return true;
                }
            }

            // 2) Rückfall: Randlos-Vollbild – das Vordergrundfenster füllt seinen Monitor exakt
            //    aus und ist nicht Desktop/Shell.
            return IsForegroundWindowBorderlessFullscreen();
        }
        catch
        {
            // Jeder Ausnahmefall ⇒ „kein Vollbild": nie werfen.
            return false;
        }
    }

    /// <summary>
    /// Prüft, ob das Vordergrundfenster seinen Monitor vollständig ausfüllt (Randlos-Vollbild)
    /// und dabei nicht der Desktop/die Shell ist.
    /// </summary>
    private static bool IsForegroundWindowBorderlessFullscreen()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
            return false;

        // Desktop/Shell nie als „Spiel" werten.
        if (hwnd == GetDesktopWindow() || hwnd == GetShellWindow())
            return false;

        if (!GetWindowRect(hwnd, out var windowRect))
            return false;

        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero)
            return false;

        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info))
            return false;

        // Randlos-Vollbild: Fensterrechteck deckt sich exakt mit dem gesamten Monitorrechteck.
        var m = info.rcMonitor;
        return windowRect.Left == m.Left
            && windowRect.Top == m.Top
            && windowRect.Right == m.Right
            && windowRect.Bottom == m.Bottom;
    }

    // --- Win32-Konstanten -------------------------------------------------------------------

    private const int S_OK = 0;

    // QUERY_USER_NOTIFICATION_STATE
    private const int QUNS_BUSY = 2;
    private const int QUNS_RUNNING_D3D_FULL_SCREEN = 3;
    private const int QUNS_PRESENTATION_MODE = 4;

    private const uint MONITOR_DEFAULTTONEAREST = 2;

    // --- Win32-Strukturen -------------------------------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    // --- Win32-P/Invoke (nur lesend) --------------------------------------------------------

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int state);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
}
