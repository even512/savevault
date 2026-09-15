using System.Diagnostics;
using System.IO;
using SaveVault.Client.Services;

namespace SaveVault.Client.Tests;

/// <summary>
/// Tests für <see cref="FileLockInspector"/> (Restart-Manager-Wrapper, siehe
/// <c>specs/savevault-change-sync-anzeige-fixes.md</c>, Nachtrag 3, Fix 4). Die Sperre wird über
/// eine im Testprozess selbst offen gehaltene Datei simuliert – reicht laut Spec als Nachweis der
/// Mechanik, ohne einen echten Fremdprozess zu starten.
/// </summary>
public class FileLockInspectorTests
{
    [Fact]
    public void GetLockingProcessNames_liefert_eigenen_Prozessnamen_fuer_selbst_gesperrte_Datei()
    {
        using var dir = new TempDirectory();
        var path = dir.WriteFile("locked.dat", "gesperrt");

        using var exclusiveHandle = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var names = FileLockInspector.GetLockingProcessNames(path);

        var currentProcessName = Process.GetCurrentProcess().ProcessName;
        Assert.Contains(names, n => string.Equals(n, currentProcessName, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GetLockingProcessNames_liefert_leere_Liste_wenn_niemand_sperrt()
    {
        using var dir = new TempDirectory();
        var path = dir.WriteFile("frei.dat", "niemand haelt mich offen");

        var names = FileLockInspector.GetLockingProcessNames(path);

        Assert.Empty(names);
    }

    [Fact]
    public void GetLockingProcessNames_wirft_nie_bei_nicht_existierendem_Pfad()
    {
        var missing = Path.Combine(Path.GetTempPath(), "savevault-client-tests", Guid.NewGuid().ToString("N") + ".dat");

        var names = FileLockInspector.GetLockingProcessNames(missing);

        Assert.Empty(names);
    }

    [Fact]
    public void GetLockingProcessNames_wirft_nie_bei_leerem_Pfad()
    {
        var names = FileLockInspector.GetLockingProcessNames(string.Empty);

        Assert.Empty(names);
    }
}
