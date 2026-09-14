using Microsoft.Extensions.Logging.Abstractions;
using SaveVault.Core.Api;
using SaveVault.Core.Models;
using SaveVault.Server.Storage;

namespace SaveVault.Core.Tests;

/// <summary>
/// Regressionstest für Tims Meldung: „Herkunftsgerät" zeigte im Client die rohe DeviceId
/// (GUID) statt des Gerätenamens, weil <see cref="RevisionInfo"/>/<see cref="RevisionDownload"/>
/// den Namen nicht mitlieferten.
/// </summary>
public sealed class VaultStoreDeviceNameTests
{
    [Fact]
    public async Task GetRevisionsAsync_LiefertDenGeraetenamenDesHochladenden()
    {
        using var dir = new TempDirectory();
        var store = new VaultStore(dir.Path, NullLogger<VaultStore>.Instance);
        var game = GameKey.FromName("Testspiel");

        // TouchDevice() in RegisterRevisionAsync legt unbekannte Geräte bewusst nicht implizit
        // an ("nur via Pairing") - das Gerät muss also erst gepairt werden, wie im echten Ablauf.
        var (code, _) = await store.GetPairingCodeAsync(CancellationToken.None);
        var pairing = await store.PairAsync(
            new PairRequest(code, "TIMS-PC", "Windows 11", "1.8.0"), CancellationToken.None);
        var device = new DeviceInfo(pairing.DeviceId, "TIMS-PC", "Windows 11", "1.8.0", DateTime.UtcNow);

        await store.RegisterRevisionAsync(
            game,
            new UploadRevisionRequest(device, FileManifest.Empty, IsConflict: false, BasedOnRevision: null),
            CancellationToken.None);

        var list = await store.GetRevisionsAsync(game, CancellationToken.None);
        var info = Assert.Single(list.Revisions);
        Assert.Equal(pairing.DeviceId, info.DeviceId);
        Assert.Equal("TIMS-PC", info.DeviceName);

        var download = await store.GetRevisionAsync(game, info.Number, CancellationToken.None);
        Assert.Equal("TIMS-PC", download.DeviceName);
    }
}
