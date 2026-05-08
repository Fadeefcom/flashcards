using RecallCraft.Domain.Entities;

namespace RecallCraft.Application.Abstractions;

public interface ILocalStorageService
{
    Task InitializeAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<Folder>> GetFoldersAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<Folder>> GetAllFoldersAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<Module>> GetAllModulesAsync(CancellationToken ct);
    Task<Folder?> GetFolderAsync(Guid folderId, CancellationToken cancellationToken);
    Task<Module?> GetModuleAsync(Guid moduleId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Module>> GetModulesByFolderAsync(Guid folderId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Card>> GetCardsByModuleAsync(Guid moduleId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Card>> GetAllCardsByModuleAsync(Guid moduleId, CancellationToken cancellationToken);
    Task<Card?> GetCardAsync(Guid cardId, CancellationToken cancellationToken);
    Task UpsertFolderAsync(Folder folder, CancellationToken cancellationToken);
    Task UpsertModuleAsync(Module module, CancellationToken cancellationToken);
    Task UpsertCardAsync(Card card, CancellationToken cancellationToken);
    Task MarkDeletedAsync(Guid entityId, string entityType, CancellationToken cancellationToken);
    Task<IReadOnlyList<SyncQueueItem>> GetPendingSyncItemsAsync(CancellationToken cancellationToken);
    Task EnqueueSyncAsync(SyncQueueItem item, CancellationToken cancellationToken);
    Task RemoveSyncItemAsync(Guid syncItemId, CancellationToken cancellationToken);
    Task UpdateSyncItemAsync(SyncQueueItem item, CancellationToken cancellationToken);
    Task<DateTimeOffset?> GetLastSyncAtAsync(CancellationToken cancellationToken);
    Task SetLastSyncAtAsync(DateTimeOffset value, CancellationToken cancellationToken);
}
