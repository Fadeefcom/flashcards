using RecallCraft.Application.Abstractions;
using RecallCraft.Domain.Entities;
using RecallCraft.Domain.Enums;

namespace RecallCraft.Application.Services;

public sealed class LibraryService(ILocalStorageService storage)
{
    public Task<IReadOnlyList<Folder>> GetFoldersAsync(CancellationToken cancellationToken) =>
        storage.GetFoldersAsync(cancellationToken);

    public Task<IReadOnlyList<Module>> GetModulesAsync(Guid folderId, CancellationToken cancellationToken) =>
        storage.GetModulesByFolderAsync(folderId, cancellationToken);

    public Task<Module?> GetModuleAsync(Guid moduleId, CancellationToken cancellationToken) =>
        storage.GetModuleAsync(moduleId, cancellationToken);

    public Task<IReadOnlyList<Card>> GetCardsAsync(Guid moduleId, CancellationToken cancellationToken) =>
        storage.GetCardsByModuleAsync(moduleId, cancellationToken);

    public async Task<Folder> CreateFolderAsync(string name, CancellationToken cancellationToken)
    {
        var folder = new Folder { Name = name.Trim(), IsDirty = true };
        await storage.UpsertFolderAsync(folder, cancellationToken);
        await storage.EnqueueSyncAsync(new SyncQueueItem
        {
            EntityId = folder.Id,
            EntityType = SyncEntityType.Folder,
            Operation = SyncOperation.Create
        }, cancellationToken);
        return folder;
    }

    public async Task<Module> CreateModuleAsync(Guid folderId, string name, CancellationToken cancellationToken)
    {
        var module = new Module { FolderId = folderId, Name = name.Trim(), IsDirty = true };
        await storage.UpsertModuleAsync(module, cancellationToken);
        await storage.EnqueueSyncAsync(new SyncQueueItem
        {
            EntityId = module.Id,
            EntityType = SyncEntityType.Module,
            Operation = SyncOperation.Create
        }, cancellationToken);
        return module;
    }

    public async Task<Card> CreateCardAsync(Guid moduleId, string frontText, string backText, CancellationToken cancellationToken)
    {
        var card = new Card
        {
            ModuleId = moduleId,
            FrontText = frontText.Trim(),
            BackText = backText.Trim(),
            AudioStatus = AudioStatus.Pending,
            IsDirty = true
        };

        await storage.UpsertCardAsync(card, cancellationToken);
        await storage.EnqueueSyncAsync(new SyncQueueItem
        {
            EntityId = card.Id,
            EntityType = SyncEntityType.Card,
            Operation = SyncOperation.Create
        }, cancellationToken);
        return card;
    }

    public async Task SaveCardAsync(Card card, CancellationToken cancellationToken)
    {
        card.MarkDirty();
        await storage.UpsertCardAsync(card, cancellationToken);
        await storage.EnqueueSyncAsync(new SyncQueueItem
        {
            EntityId = card.Id,
            EntityType = SyncEntityType.Card,
            Operation = SyncOperation.Update
        }, cancellationToken);
    }

    public async Task SaveModuleAsync(Module module, CancellationToken cancellationToken)
    {
        module.MarkDirty();
        await storage.UpsertModuleAsync(module, cancellationToken);
        await storage.EnqueueSyncAsync(new SyncQueueItem
        {
            EntityId = module.Id,
            EntityType = SyncEntityType.Module,
            Operation = SyncOperation.Update
        }, cancellationToken);
    }

    public Task SaveModuleLearningProgressAsync(Module module, CancellationToken cancellationToken) =>
        storage.UpsertModuleAsync(module, cancellationToken);

    public async Task DeleteAsync(Guid entityId, SyncEntityType entityType, CancellationToken cancellationToken)
    {
        await storage.MarkDeletedAsync(entityId, entityType.ToString(), cancellationToken);
    }
}
