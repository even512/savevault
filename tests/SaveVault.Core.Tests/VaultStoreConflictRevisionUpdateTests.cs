using Microsoft.Extensions.Logging.Abstractions;
using SaveVault.Core.Api;
using SaveVault.Core.Models;
using SaveVault.Server.Storage;

namespace SaveVault.Core.Tests;

/// <summary>
/// Regressionstests für Fix 1 aus <c>specs/savevault-change-sync-anzeige-fixes.md</c>:
/// <see cref="VaultStore"/> aktualisierte bisher bei einem bereits bekannten Teilnehmer eines
/// offenen Konflikts dessen <see cref="ConflictParticipant.Revision"/> NICHT, wenn derselbe Konflikt
/// erneut auftrat (weitere lokale Änderung, Server-Revision weiterhin über der Basis) – der
/// Konflikt-Datensatz blieb dauerhaft auf dem Stand der ERSTMALIGEN Erkennung eingefroren, obwohl
/// zwischenzeitlich weitere Konflikt-Revisionen hochgeladen wurden (Tims Warcraft-3-artiger Fall:
/// mehrfaches Speichern bei offenem, ignoriertem Konflikt, danach "Lösen" zeigte die veraltete
/// Erst-Revision statt der aktuellsten).
/// </summary>
public sealed class VaultStoreConflictRevisionUpdateTests
{
    [Fact]
    public async Task RegisterConflict_DesselbenGeraets_AktualisiertAufDieNeuesteRevision()
    {
        using var dir = new TempDirectory();
        var store = new VaultStore(dir.Path, NullLogger<VaultStore>.Instance);
        var game = GameKey.FromName("Warcraft III Reforged");

        var (codeA, _) = await store.GetPairingCodeAsync(CancellationToken.None);
        var pairA = await store.PairAsync(new PairRequest(codeA, "Notebook", "Windows 11", "1.8.4"), CancellationToken.None);
        var (codeB, _) = await store.GetPairingCodeAsync(CancellationToken.None);
        var pairB = await store.PairAsync(new PairRequest(codeB, "Hauptrechner", "Windows 11", "1.8.4"), CancellationToken.None);

        var deviceA = new DeviceInfo(pairA.DeviceId, "Notebook", "Windows 11", "1.8.4", DateTime.UtcNow);
        var deviceB = new DeviceInfo(pairB.DeviceId, "Hauptrechner", "Windows 11", "1.8.4", DateTime.UtcNow);

        // Hauptrechner (B) etabliert den Kopf (Revision 1, unkonfligiert).
        var respB1 = await store.RegisterRevisionAsync(
            game, new UploadRevisionRequest(deviceB, FileManifest.Empty, IsConflict: false, BasedOnRevision: 0),
            CancellationToken.None);
        Assert.Equal(1, respB1.Revision);

        // Notebook (A) erkennt den Konflikt zum ERSTEN Mal (Konflikt-Revision 2).
        var respA1 = await store.RegisterRevisionAsync(
            game, new UploadRevisionRequest(deviceA, FileManifest.Empty, IsConflict: true, BasedOnRevision: 0),
            CancellationToken.None);
        Assert.Equal(2, respA1.Revision);

        var afterFirst = await store.GetConflictsAsync(CancellationToken.None);
        var conflictAfterFirst = Assert.Single(afterFirst.Conflicts);
        var participantAfterFirst = conflictAfterFirst.Participants.Single(p => p.DeviceId == deviceA.Id);
        Assert.Equal(respA1.Revision, participantAfterFirst.Revision);

        // Notebook speichert weiter, der offene Konflikt bleibt ignoriert -> derselbe Konflikt tritt
        // ERNEUT auf (Konflikt-Revision 3). Die gespeicherte Teilnehmer-Revision muss auf den NEUEN
        // Stand nachgezogen werden, nicht auf der Erst-Erkennung (2) eingefroren bleiben.
        var respA2 = await store.RegisterRevisionAsync(
            game, new UploadRevisionRequest(deviceA, FileManifest.Empty, IsConflict: true, BasedOnRevision: 0),
            CancellationToken.None);
        Assert.Equal(3, respA2.Revision);

        var afterSecond = await store.GetConflictsAsync(CancellationToken.None);
        var conflictAfterSecond = Assert.Single(afterSecond.Conflicts);
        Assert.Equal(conflictAfterFirst.Id, conflictAfterSecond.Id); // derselbe offene Konflikt-Datensatz
        var participantAfterSecond = conflictAfterSecond.Participants.Single(p => p.DeviceId == deviceA.Id);
        Assert.Equal(respA2.Revision, participantAfterSecond.Revision); // NEUESTE Revision, nicht die erste
        Assert.NotEqual(respA1.Revision, participantAfterSecond.Revision);
    }

    [Fact]
    public async Task RegisterConflict_ZiehtDenGegenpartNach_WennSichDessenKopfRevisionWeiterbewegtHat()
    {
        using var dir = new TempDirectory();
        var store = new VaultStore(dir.Path, NullLogger<VaultStore>.Instance);
        var game = GameKey.FromName("Testspiel");

        var (codeA, _) = await store.GetPairingCodeAsync(CancellationToken.None);
        var pairA = await store.PairAsync(new PairRequest(codeA, "GeraetA", "Windows 11", "1.8.4"), CancellationToken.None);
        var (codeB, _) = await store.GetPairingCodeAsync(CancellationToken.None);
        var pairB = await store.PairAsync(new PairRequest(codeB, "GeraetB", "Windows 11", "1.8.4"), CancellationToken.None);

        var deviceA = new DeviceInfo(pairA.DeviceId, "GeraetA", "Windows 11", "1.8.4", DateTime.UtcNow);
        var deviceB = new DeviceInfo(pairB.DeviceId, "GeraetB", "Windows 11", "1.8.4", DateTime.UtcNow);

        // Geraet B etabliert den Kopf (Revision 1).
        var respB1 = await store.RegisterRevisionAsync(
            game, new UploadRevisionRequest(deviceB, FileManifest.Empty, IsConflict: false, BasedOnRevision: 0),
            CancellationToken.None);
        Assert.Equal(1, respB1.Revision);

        // Geraet A erkennt einen Konflikt (Teilnehmer: A@2, B@1).
        var respA1 = await store.RegisterRevisionAsync(
            game, new UploadRevisionRequest(deviceA, FileManifest.Empty, IsConflict: true, BasedOnRevision: 0),
            CancellationToken.None);
        Assert.Equal(2, respA1.Revision);

        // Geraet B (Kopf-Besitzer, am Konflikt bereits beteiligt) laedt UNABHAENGIG vom Konflikt eine
        // weitere, nicht konfligierende Revision hoch (Kopf bewegt sich von 1 auf 3 weiter).
        var respB2 = await store.RegisterRevisionAsync(
            game, new UploadRevisionRequest(deviceB, FileManifest.Empty, IsConflict: false, BasedOnRevision: 1),
            CancellationToken.None);
        Assert.Equal(3, respB2.Revision);

        // Geraet A loest denselben Konflikt erneut aus - der Gegenpart (B) muss jetzt auf die
        // AKTUELLE Kopf-Revision (3) nachgezogen werden, nicht auf der urspruenglichen (1) stehen bleiben.
        var respA2 = await store.RegisterRevisionAsync(
            game, new UploadRevisionRequest(deviceA, FileManifest.Empty, IsConflict: true, BasedOnRevision: 0),
            CancellationToken.None);

        var conflicts = await store.GetConflictsAsync(CancellationToken.None);
        var conflict = Assert.Single(conflicts.Conflicts);
        var participantA = conflict.Participants.Single(p => p.DeviceId == deviceA.Id);
        var participantB = conflict.Participants.Single(p => p.DeviceId == deviceB.Id);
        Assert.Equal(respA2.Revision, participantA.Revision);
        Assert.Equal(respB2.Revision, participantB.Revision);
    }
}
