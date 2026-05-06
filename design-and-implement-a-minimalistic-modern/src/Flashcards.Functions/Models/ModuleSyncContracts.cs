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
    IReadOnlyList<ModuleCardSyncItem> Cards);

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