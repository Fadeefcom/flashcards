namespace Flashcards.Functions.Models;

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

public sealed record HierarchySyncRequest(
    IReadOnlyList<CloudFolder> LocalFolders,
    IReadOnlyList<CloudModule> LocalModules);

public sealed record ModuleSyncRequest(
    Guid ModuleId,
    DateTimeOffset? LastKnownUpdate = null,
    IReadOnlyList<ModuleCardSyncItem>? LocalCards = null);

public sealed record ModuleSyncResponse(
    Guid ModuleId,
    DateTimeOffset ServerTimestamp,
    DateTimeOffset? ModuleLastUpdated,
    ModuleInfo ModuleInfo,
    IReadOnlyList<ModuleCardSyncItem> Cards);

public sealed record ModuleInfo(
    Guid ModuleId,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastUpdated,
    int TotalCards,
    int ActiveCards,
    int DeletedCards,
    int TotalWords,
    string SyncVersion);

public sealed record ModuleCardSyncItem(
    Guid Id,
    Guid ModuleId,
    string FrontText,
    string BackText,
    DateTimeOffset LastUpdated,
    string AudioStatus,
    string? AudioUrl,
    DateTimeOffset? AudioUpdatedAt,
    bool IsDeleted);
