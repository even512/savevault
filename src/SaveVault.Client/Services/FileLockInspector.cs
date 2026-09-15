using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace SaveVault.Client.Services;

/// <summary>
/// Dünner, defensiver Wrapper um die Windows-Restart-Manager-API (<c>rstrtmgr.dll</c>), der zu
/// einem gegebenen Dateipfad die Namen der Prozesse liefert, die diese Datei aktuell offen
/// halten – generisch, ohne dass der Aufrufer vorher weiß, welcher Prozess das sein könnte
/// (siehe <c>specs/savevault-change-sync-anzeige-fixes.md</c>, Nachtrag 3, Fix 4). Reine
/// Zusatz-Diagnose: wird genutzt, um bei einer bereits anderweitig erkannten gesperrten Datei
/// (siehe <see cref="SaveVault.Core.Hashing.ManifestBuilder"/>/<see cref="SyncEngine"/>) eine
/// ehrliche Statuszeile zu zeigen ("X.exe hält die Datei offen") statt nur "Wartet" ohne
/// Erklärung. Keine Verhaltensänderung der Sync-Entscheidung selbst.
///
/// <para><b>Robustheit (verbindlich):</b> JEDER Fehler (API auf dieser Windows-Edition nicht
/// verfügbar, Marshaling-Problem, Berechtigungsproblem, Datei bereits wieder frei) liefert eine
/// leere Liste zurück – niemals eine Exception nach außen. Die Restart-Manager-Session wird in
/// jedem Fall (auch im Fehlerpfad) wieder sauber beendet (kein Handle-Leak).</para>
/// </summary>
public static class FileLockInspector
{
    private const int ErrorMoreData = 234;
    private const int CchRmMaxAppName = 255;
    private const int CchRmMaxSvcName = 63;
    private const int CchRmSessionKeyLen = 32;

    // Grosszuegige, aber endliche Obergrenze: verhindert, dass ein unplausibler Rueckgabewert
    // der API (oder ein Marshaling-Fehler) eine unbegrenzte Allokation ausloest.
    private const uint MaxProcessesConsidered = 256;

    private enum RM_APP_TYPE
    {
        RmUnknownApp = 0,
        RmMainWindow = 1,
        RmOtherWindow = 2,
        RmService = 3,
        RmExplorer = 4,
        RmConsole = 5,
        RmCritical = 1000,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RM_UNIQUE_PROCESS
    {
        public int dwProcessId;
        public FILETIME ProcessStartTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RM_PROCESS_INFO
    {
        public RM_UNIQUE_PROCESS Process;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchRmMaxAppName + 1)]
        public string strAppName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchRmMaxSvcName + 1)]
        public string strServiceShortName;

        public RM_APP_TYPE ApplicationType;
        public uint AppStatus;
        public uint TSSessionId;

        [MarshalAs(UnmanagedType.Bool)]
        public bool bRestartable;
    }

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmStartSession(out uint pSessionHandle, int dwSessionFlags, StringBuilder strSessionKey);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmEndSession(uint pSessionHandle);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmRegisterResources(
        uint pSessionHandle,
        uint nFiles,
        string[] rgsFilenames,
        uint nApplications,
        RM_UNIQUE_PROCESS[]? rgApplications,
        uint nServices,
        string[]? rgsServiceNames);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmGetList(
        uint dwSessionHandle,
        out uint pnProcInfoNeeded,
        ref uint pnProcInfo,
        [In, Out] RM_PROCESS_INFO[]? rgAffectedApps,
        ref uint lpdwRebootReasons);

    /// <summary>
    /// Liefert die (eindeutigen) Namen der Prozesse, die <paramref name="filePath"/> aktuell
    /// offen halten – leer, wenn niemand sperrt ODER die Abfrage selbst fehlschlägt. Wirft nie.
    /// </summary>
    public static IReadOnlyList<string> GetLockingProcessNames(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return Array.Empty<string>();

        uint session = 0;
        var started = false;
        try
        {
            var sessionKey = new StringBuilder(CchRmSessionKeyLen + 1);
            if (RmStartSession(out session, 0, sessionKey) != 0)
                return Array.Empty<string>();
            started = true;

            var resources = new[] { filePath };
            if (RmRegisterResources(session, (uint)resources.Length, resources, 0, null, 0, null) != 0)
                return Array.Empty<string>();

            uint neededFirst = 0;
            uint countFirst = 0;
            uint reasonsFirst = 0;
            var firstResult = RmGetList(session, out neededFirst, ref countFirst, null, ref reasonsFirst);
            if (firstResult != 0 && firstResult != ErrorMoreData)
                return Array.Empty<string>();
            if (neededFirst == 0)
                return Array.Empty<string>();

            var count = Math.Min(neededFirst, MaxProcessesConsidered);
            var processInfo = new RM_PROCESS_INFO[count];
            uint neededSecond = 0;
            var countSecond = count;
            uint reasonsSecond = 0;
            var secondResult = RmGetList(session, out neededSecond, ref countSecond, processInfo, ref reasonsSecond);
            if (secondResult != 0)
                return Array.Empty<string>();

            var names = new List<string>();
            for (var i = 0; i < countSecond && i < processInfo.Length; i++)
            {
                var name = ResolveProcessName(processInfo[i]);
                if (!string.IsNullOrWhiteSpace(name) && !names.Contains(name, StringComparer.OrdinalIgnoreCase))
                    names.Add(name);
            }
            return names;
        }
        catch
        {
            // Jeder unerwartete Fehler (Marshaling, API auf dieser Windows-Edition nicht
            // verfuegbar, ...) -> leere Liste statt Exception nach aussen.
            return Array.Empty<string>();
        }
        finally
        {
            if (started)
            {
                try { RmEndSession(session); }
                catch { /* best effort - kein Leak-Risiko fuer den Aufrufer, Session laeuft im
                            schlimmsten Fall bis zum Prozessende weiter */ }
            }
        }
    }

    /// <summary>
    /// Bevorzugt den echten Prozessnamen über die PID (robuster als der von RM selbst gemeldete
    /// <c>strAppName</c>, der z. B. bei Konsolenanwendungen oft leer ist); fällt auf
    /// <c>strAppName</c> zurück, wenn der Prozess nicht (mehr) auflösbar ist.
    /// </summary>
    private static string? ResolveProcessName(RM_PROCESS_INFO info)
    {
        try
        {
            using var process = Process.GetProcessById(info.Process.dwProcessId);
            var name = process.ProcessName;
            return string.IsNullOrWhiteSpace(name) ? Fallback(info) : name;
        }
        catch
        {
            // Prozess evtl. zwischenzeitlich beendet oder kein Zugriff.
            return Fallback(info);
        }
    }

    private static string? Fallback(RM_PROCESS_INFO info)
        => string.IsNullOrWhiteSpace(info.strAppName) ? null : info.strAppName;
}
