using System.IO;
using SaveVault.Core.Api;
using SaveVault.Core.Models;

namespace SaveVault.Client.Tests;

/// <summary>
/// Minimale Test-Attrappe für <see cref="ISaveVaultApi"/>: nur die drei Methoden, die ein
/// Download-Zyklus (<see cref="SyncEngine.RunCycleAsync"/> → Fall „Download") tatsächlich
/// aufruft, sind konfigurierbar; alles andere wirft <see cref="NotImplementedException"/>, damit
/// ein Test sofort auffällt, falls er unerwartet einen anderen Pfad auslöst.
/// </summary>
public sealed class FakeSaveVaultApi : ISaveVaultApi
{
    public RevisionHead Head { get; set; } = new(GameKey.FromName("Test"), 0);
    public RevisionDownload? Revision { get; set; }
    public Func<string, Stream>? ContentFor { get; set; }

    public Task<RevisionHead> GetHeadAsync(GameKey game, BucketScope scope = BucketScope.Private, CancellationToken ct = default)
        => Task.FromResult(Head);

    public Task<RevisionDownload> GetRevisionAsync(GameKey game, long revision, BucketScope scope = BucketScope.Private, CancellationToken ct = default)
        => Task.FromResult(Revision ?? throw new InvalidOperationException("Revision nicht konfiguriert."));

    public Task<Stream> DownloadContentAsync(GameKey game, string sha256, BucketScope scope = BucketScope.Private, CancellationToken ct = default)
        => Task.FromResult(ContentFor?.Invoke(sha256) ?? new MemoryStream(System.Text.Encoding.UTF8.GetBytes("server-inhalt")));

    // --- nicht von den Tests dieses Projekts genutzt -----------------------------

    public Task<PairResponse> PairAsync(PairRequest request, CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task<HeartbeatResponse> HeartbeatAsync(HeartbeatRequest request, CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task<GamesResponse> GetGamesAsync(CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task<RevisionListResponse> GetRevisionsAsync(GameKey game, BucketScope scope = BucketScope.Private, CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task<byte[]?> GetCoverAsync(GameKey game, CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task<UploadRevisionResponse> UploadRevisionAsync(GameKey game, UploadRevisionRequest request, BucketScope scope = BucketScope.Private, CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task UploadContentAsync(GameKey game, string sha256, Stream content, BucketScope scope = BucketScope.Private, CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task<ConflictListResponse> GetConflictsAsync(CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task<ResolveConflictResponse> ResolveConflictAsync(string conflictId, ResolveConflictRequest request, CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task<RestoreResponse> RestoreAsync(GameKey game, RestoreRequest request, CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task<CommandListResponse> GetCommandsAsync(string deviceId, CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task<AckResponse> AckCommandAsync(string commandId, CancellationToken ct = default)
        => throw new NotImplementedException();
}
