using System.Text.Json;
using Microsoft.JSInterop;
using RecallCraft.Application.Abstractions;
using RecallCraft.Domain.Entities;

namespace RecallCraft.Pwa.Services;

public sealed class BrowserLocalStorageService(IJSRuntime js) : ILocalStorageService
{
    private const string DatabaseKey = "recallcraft.database";
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var db = await LoadAsync(cancellationToken);
        await SaveAsync(db, cancellationToken);
    }

    public async Task<IReadOnlyList<Folder>> GetFoldersAsync(CancellationToken cancellationToken)
    {
        var db = await LoadAsync(cancellationToken);
        return db.Folders.Where(x => !x.IsDeleted).OrderBy(x => x.Name).ToList();
    }

    public async Task<Module?> GetModuleAsync(Guid moduleId, CancellationToken cancellationToken)
    {
        var db = await LoadAsync(cancellationToken);
        return db.Modules.FirstOrDefault(x => x.Id == moduleId && !x.IsDeleted);
    }

    public async Task<IReadOnlyList<Module>> GetModulesByFolderAsync(Guid folderId, CancellationToken cancellationToken)
    {
        var db = await LoadAsync(cancellationToken);
        return db.Modules
            .Where(x => x.FolderId == folderId && !x.IsDeleted)
            .OrderBy(x => x.NextReviewDate)
            .ThenBy(x => x.Name)
            .ToList();
    }

    public async Task<IReadOnlyList<Card>> GetCardsByModuleAsync(Guid moduleId, CancellationToken cancellationToken)
    {
        var db = await LoadAsync(cancellationToken);
        return db.Cards.Where(x => x.ModuleId == moduleId && !x.IsDeleted).OrderBy(x => x.FrontText).ToList();
    }

    public async Task<Card?> GetCardAsync(Guid cardId, CancellationToken cancellationToken)
    {
        var db = await LoadAsync(cancellationToken);
        return db.Cards.FirstOrDefault(x => x.Id == cardId && !x.IsDeleted);
    }

    public Task UpsertFolderAsync(Folder folder, CancellationToken cancellationToken) =>
        MutateAsync(db => Upsert(db.Folders, folder), cancellationToken);

    public Task UpsertModuleAsync(Module module, CancellationToken cancellationToken) =>
        MutateAsync(db => Upsert(db.Modules, module), cancellationToken);

    public Task UpsertCardAsync(Card card, CancellationToken cancellationToken) =>
        MutateAsync(db => Upsert(db.Cards, card), cancellationToken);

    public Task MarkDeletedAsync(Guid entityId, string entityType, CancellationToken cancellationToken) =>
        MutateAsync(db =>
        {
            EntityBase? entity = entityType switch
            {
                nameof(Folder) => db.Folders.FirstOrDefault(x => x.Id == entityId),
                nameof(Module) => db.Modules.FirstOrDefault(x => x.Id == entityId),
                nameof(Card) => db.Cards.FirstOrDefault(x => x.Id == entityId),
                _ => null
            };

            if (entity is not null)
            {
                entity.IsDeleted = true;
                entity.MarkDirty();
            }
        }, cancellationToken);

    public async Task<IReadOnlyList<SyncQueueItem>> GetPendingSyncItemsAsync(CancellationToken cancellationToken)
    {
        var db = await LoadAsync(cancellationToken);
        return db.SyncQueue.OrderBy(x => x.CreatedAt).ToList();
    }

    public Task EnqueueSyncAsync(SyncQueueItem item, CancellationToken cancellationToken) =>
        MutateAsync(db => db.SyncQueue.Add(item), cancellationToken);

    public Task RemoveSyncItemAsync(Guid syncItemId, CancellationToken cancellationToken) =>
        MutateAsync(db => db.SyncQueue.RemoveAll(x => x.Id == syncItemId), cancellationToken);

    public Task UpdateSyncItemAsync(SyncQueueItem item, CancellationToken cancellationToken) =>
        MutateAsync(db => Upsert(db.SyncQueue, item), cancellationToken);

    public async Task<DateTimeOffset?> GetLastSyncAtAsync(CancellationToken cancellationToken)
    {
        var db = await LoadAsync(cancellationToken);
        return db.LastSyncAt;
    }

    public Task SetLastSyncAtAsync(DateTimeOffset value, CancellationToken cancellationToken) =>
        MutateAsync(db => db.LastSyncAt = value, cancellationToken);

    private async Task MutateAsync(Action<BrowserDatabase> mutation, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var db = await ReadAsync(cancellationToken);
            mutation(db);
            await WriteAsync(db, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<BrowserDatabase> LoadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await ReadAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<BrowserDatabase> ReadAsync(CancellationToken cancellationToken)
    {
        var json = await js.InvokeAsync<string?>("recallCraft.get", cancellationToken, DatabaseKey);
        return string.IsNullOrWhiteSpace(json)
            ? new BrowserDatabase()
            : JsonSerializer.Deserialize<BrowserDatabase>(json, _jsonOptions) ?? new BrowserDatabase();
    }

    private async Task SaveAsync(BrowserDatabase db, CancellationToken cancellationToken) =>
        await WriteAsync(db, cancellationToken);

    private async Task WriteAsync(BrowserDatabase db, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(db, _jsonOptions);
        await js.InvokeVoidAsync("recallCraft.set", cancellationToken, DatabaseKey, json);
    }

    private static void Upsert<T>(List<T> items, T value) where T : class
    {
        var id = (Guid)typeof(T).GetProperty("Id")!.GetValue(value)!;
        var index = items.FindIndex(x => (Guid)typeof(T).GetProperty("Id")!.GetValue(x)! == id);
        if (index >= 0)
        {
            items[index] = value;
        }
        else
        {
            items.Add(value);
        }
    }

    public async Task<Folder?> GetFolderAsync(Guid folderId, CancellationToken cancellationToken)
    {
        var db = await LoadAsync(cancellationToken);
        return db.Folders.FirstOrDefault(x => x.Id == folderId && !x.IsDeleted);
    }
}
