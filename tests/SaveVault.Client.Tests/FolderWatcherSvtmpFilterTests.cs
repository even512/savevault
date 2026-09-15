using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SaveVault.Client.Services;

namespace SaveVault.Client.Tests;

/// <summary>
/// Regressionstest für <c>specs/savevault-change-sync-anzeige-fixes.md</c>, Nachtrag 2+3, Fix 3:
/// eigene <c>*.svtmp-*</c>-Zwischendateien (aus einem gescheiterten
/// <see cref="SyncEngine.ApplyRevisionAsync"/>-Download) dürfen keinen eigenen
/// <see cref="FolderWatcher.Changed"/>-Zyklus mehr auslösen – sonst löst sich ein gescheiterter
/// Download durchs Anlegen/Löschen seiner eigenen Temp-Datei selbst im Sekundentakt erneut aus.
/// </summary>
public class FolderWatcherSvtmpFilterTests
{
    [Fact]
    public async Task Svtmp_Datei_loest_kein_Changed_aus()
    {
        using var dir = new TempDirectory();
        using var watcher = new FolderWatcher(dir.Path, TimeSpan.FromMilliseconds(100));

        var changedCount = 0;
        watcher.Changed += _ => Interlocked.Increment(ref changedCount);

        var tmpPath = Path.Combine(dir.Path, "save.dat.svtmp-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(tmpPath, "teilweise heruntergeladen");
        await Task.Delay(400);
        File.Delete(tmpPath);
        await Task.Delay(400);

        Assert.Equal(0, Volatile.Read(ref changedCount));
    }

    [Fact]
    public async Task Normale_Datei_loest_weiterhin_Changed_aus()
    {
        // Gegenprobe: der Filter darf nicht "alles" unterdrücken - eine echte Aenderung muss
        // weiterhin ein Changed-Ereignis ausloesen.
        using var dir = new TempDirectory();
        using var watcher = new FolderWatcher(dir.Path, TimeSpan.FromMilliseconds(100));

        var changedCount = 0;
        watcher.Changed += _ => Interlocked.Increment(ref changedCount);

        File.WriteAllText(Path.Combine(dir.Path, "save.dat"), "echte Aenderung");

        var fired = await WaitForAsync(() => Volatile.Read(ref changedCount) > 0, TimeSpan.FromSeconds(10));
        Assert.True(fired, "Eine normale Datei-Aenderung haette ein Changed-Ereignis ausloesen muessen.");
    }

    private static async Task<bool> WaitForAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return true;
            await Task.Delay(50);
        }
        return condition();
    }
}
