using RecallCraft.Application.Abstractions;
using RecallCraft.Domain.Entities;
using RecallCraft.Domain.Enums;

namespace RecallCraft.Application.Services;

public sealed class SyncService(
    ILocalStorageService storage,
    ICloudSyncClient cloudSyncClient,
    ISecureCredentialStore credentials,
    IAudioFileStore audioFileStore,
    IConnectivityService connectivity)
{
    public async Task<SyncResult> SyncAsync(CancellationToken cancellationToken)
    {
        await storage.InitializeAsync(cancellationToken);

        if (!await connectivity.IsInternetAvailableAsync(cancellationToken))
        {
            return new SyncResult(false, "Offline");
        }

        var functionKey = await credentials.GetApiKeyAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(functionKey))
        {
            return new SyncResult(false, "Missing Azure Function key");
        }

        var folders = await storage.GetFoldersAsync(cancellationToken);
        var syncedModules = 0;
        var changedCards = 0;
        var audioUpdated = 0;
        var lastSyncAt = await storage.GetLastSyncAtAsync(cancellationToken);

        foreach (var folder in folders)
        {
            var modules = await storage.GetModulesByFolderAsync(folder.Id, cancellationToken);
            foreach (var module in modules)
            {
                var localCards = await storage.GetCardsByModuleAsync(module.Id, cancellationToken);
                var snapshot = await cloudSyncClient.SyncModuleAsync(
                    functionKey,
                    module,
                    localCards,
                    lastSyncAt,
                    cancellationToken);

                var result = await ReconcileModuleCardsAsync(functionKey, module, localCards, snapshot.Cards, cancellationToken);
                changedCards += result.ChangedCards;
                audioUpdated += result.AudioUpdated;
                module.IsDirty = false;
                await storage.UpsertModuleAsync(module, cancellationToken);
                syncedModules++;
            }
        }

        await ClearCompletedSyncQueueAsync(cancellationToken);
        await storage.SetLastSyncAtAsync(DateTimeOffset.UtcNow, cancellationToken);
        return new SyncResult(true, $"Synced {syncedModules} modules, {changedCards} cards, {audioUpdated} audio files");
    }

    public async Task<SyncResult> PullRemoteHierarchyAsync(CancellationToken cancellationToken)
    {
        await storage.InitializeAsync(cancellationToken);

        if (!await connectivity.IsInternetAvailableAsync(cancellationToken))
        {
            return new SyncResult(false, "Offline");
        }

        var functionKey = await credentials.GetApiKeyAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(functionKey))
        {
            return new SyncResult(false, "Missing Azure Function key");
        }

        var lastSyncAt = await storage.GetLastSyncAtAsync(cancellationToken);
        var snapshot = await cloudSyncClient.PullHierarchyAsync(functionKey, lastSyncAt, cancellationToken);

        foreach (var cloudFolder in snapshot.Folders)
        {
            var folder = await storage.GetFolderAsync(cloudFolder.Id, cancellationToken) ?? new Folder { Id = cloudFolder.Id };
            folder.Name = cloudFolder.Name;
            folder.UpdatedAt = cloudFolder.LastUpdated;
            await storage.UpsertFolderAsync(folder, cancellationToken);
        }

        foreach (var cloudModule in snapshot.Modules)
        {
            var module = await storage.GetModuleAsync(cloudModule.Id, cancellationToken) ?? new Module { Id = cloudModule.Id };
            module.FolderId = cloudModule.FolderId;
            module.Name = cloudModule.Name;
            module.UpdatedAt = cloudModule.LastUpdated;
            await storage.UpsertModuleAsync(module, cancellationToken);
        }

        return new SyncResult(true, $"Pulled {snapshot.Folders.Count} folders and {snapshot.Modules.Count} modules");
    }

    private async Task<ReconcileResult> ReconcileModuleCardsAsync(
        string functionKey,
        Module module,
        IReadOnlyList<Card> localCards,
        IReadOnlyList<CloudCard> cloudCards,
        CancellationToken cancellationToken)
    {
        var changedCards = 0;
        var audioUpdated = 0;
        var localById = localCards.ToDictionary(card => card.Id);
        var cloudIds = cloudCards.Select(card => card.Id).ToHashSet();

        foreach (var oldLocalCard in localCards.Where(card => !cloudIds.Contains(card.Id)))
        {
            await storage.MarkDeletedAsync(oldLocalCard.Id, nameof(Card), cancellationToken);
            changedCards++;
        }

        foreach (var cloudCard in cloudCards)
        {
            localById.TryGetValue(cloudCard.Id, out var localCard);
            var shouldUpdateCard = localCard is null ||
                cloudCard.LastUpdated >= localCard.UpdatedAt ||
                localCard.IsDirty is false;

            var card = localCard ?? new Card
            {
                Id = cloudCard.Id,
                ModuleId = module.Id,
                CreatedAt = cloudCard.LastUpdated
            };

            if (shouldUpdateCard)
            {
                card.ModuleId = cloudCard.ModuleId;
                card.FrontText = cloudCard.FrontText;
                card.BackText = cloudCard.BackText;
                card.UpdatedAt = cloudCard.LastUpdated;
                card.IsDirty = false;
                card.IsDeleted = false;
                changedCards++;
            }

            if (cloudCard.AudioStatus.Equals("Ready", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(cloudCard.AudioUrl))
            {
                var shouldDownloadAudio = string.IsNullOrWhiteSpace(card.AudioLocalPath) ||
                    !await audioFileStore.ExistsAsync(card.AudioLocalPath, cancellationToken) ||
                    cloudCard.AudioUpdatedAt >= card.UpdatedAt;

                if (shouldDownloadAudio)
                {
                    await using var audioStream = await cloudSyncClient.DownloadAudioAsync(functionKey, cloudCard.AudioUrl, cancellationToken);
                    card.AudioLocalPath = await audioFileStore.SaveAsync(card.Id, audioStream, cancellationToken);
                    card.AudioStatus = AudioStatus.Ready;
                    audioUpdated++;
                }
            }
            else if (card.AudioStatus != AudioStatus.Ready)
            {
                await using var generatedAudio = await cloudSyncClient.GenerateSpeechAsync(functionKey, card.Id, card.FrontText, cancellationToken);
                card.AudioLocalPath = await audioFileStore.SaveAsync(card.Id, generatedAudio, cancellationToken);
                card.AudioStatus = AudioStatus.Ready;
                audioUpdated++;
            }

            await storage.UpsertCardAsync(card, cancellationToken);
        }

        return new ReconcileResult(changedCards, audioUpdated);
    }

    private async Task ClearCompletedSyncQueueAsync(CancellationToken cancellationToken)
    {
        var items = await storage.GetPendingSyncItemsAsync(cancellationToken);
        foreach (var item in items)
        {
            await storage.RemoveSyncItemAsync(item.Id, cancellationToken);
        }
    }
}

public sealed record SyncResult(bool Success, string Message);

sealed record ReconcileResult(int ChangedCards, int AudioUpdated);
