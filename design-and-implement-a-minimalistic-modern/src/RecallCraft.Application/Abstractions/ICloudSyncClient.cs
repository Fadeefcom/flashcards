using RecallCraft.Domain.Entities;

namespace RecallCraft.Application.Abstractions;

public interface ICloudSyncClient
{
    Task<ModuleSyncSnapshot> SyncModuleAsync(
        string functionKey,
        Module module,
        IReadOnlyList<Card> localCards,
        DateTimeOffset? lastKnownUpdate,
        CancellationToken cancellationToken);

    Task<Stream> GenerateSpeechAsync(
        string functionKey,
        Guid cardId,
        string text,
        CancellationToken cancellationToken);

    Task<Stream> DownloadAudioAsync(
        string functionKey,
        string audioUrl,
        CancellationToken cancellationToken);

    public Task<HierarchySnapshot> PullHierarchyAsync(string functionKey, DateTimeOffset? lastKnownUpdate, CancellationToken cancellationToken);

    Task<HierarchySnapshot> SyncHierarchyAsync(
    string functionKey,
    IReadOnlyList<Folder> localFolders,
    IReadOnlyList<Module> localModules,
    CancellationToken cancellationToken);
}

public sealed record CloudFolder(
    Guid Id,
    string Name,
    Guid? ParentId,
    DateTimeOffset LastUpdated,
    bool IsDeleted);

public sealed record CloudModule(
    Guid Id,
    Guid FolderId,
    string Name,
    DateTimeOffset LastUpdated,
    bool IsDeleted);

public sealed record HierarchySnapshot(
    IReadOnlyList<CloudFolder> Folders,
    IReadOnlyList<CloudModule> Modules);

public sealed record ModuleSyncSnapshot(
    Guid ModuleId,
    DateTimeOffset ServerTimestamp,
    DateTimeOffset? ModuleLastUpdated,
    IReadOnlyList<CloudCard> Cards);

public sealed record CloudCard(
    Guid Id,
    Guid ModuleId,
    string FrontText,
    string BackText,
    DateTimeOffset LastUpdated,
    string AudioStatus,
    string? AudioUrl,
    DateTimeOffset? AudioUpdatedAt,
    bool IsDeleted);