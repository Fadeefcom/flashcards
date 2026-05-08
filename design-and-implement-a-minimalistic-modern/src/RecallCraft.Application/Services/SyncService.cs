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
        try
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

            var initialFolders = await storage.GetAllFoldersAsync(cancellationToken);
            var initialModules = await storage.GetAllModulesAsync(cancellationToken);

            var hierarchySnapshot = await cloudSyncClient.SyncHierarchyAsync(functionKey, initialFolders, initialModules, cancellationToken);

            foreach (var cloudFolder in hierarchySnapshot.Folders)
            {
                if (cloudFolder.IsDeleted)
                {
                    await storage.MarkDeletedAsync(cloudFolder.Id, nameof(Folder), cancellationToken);
                }
                else
                {
                    var folder = await storage.GetFolderAsync(cloudFolder.Id, cancellationToken) ?? new Folder { Id = cloudFolder.Id };
                    folder.Name = cloudFolder.Name;
                    folder.ParentId = cloudFolder.ParentId;
                    folder.UpdatedAt = cloudFolder.LastUpdated;
                    folder.IsDirty = false;
                    folder.IsDeleted = false;
                    await storage.UpsertFolderAsync(folder, cancellationToken);
                }
            }

            foreach (var cloudModule in hierarchySnapshot.Modules)
            {
                if (cloudModule.IsDeleted)
                {
                    await storage.MarkDeletedAsync(cloudModule.Id, nameof(Module), cancellationToken);
                }
                else
                {
                    var module = await storage.GetModuleAsync(cloudModule.Id, cancellationToken) ?? new Module { Id = cloudModule.Id };
                    module.FolderId = cloudModule.FolderId;
                    module.Name = cloudModule.Name;
                    module.UpdatedAt = cloudModule.LastUpdated;
                    module.IsDirty = false;
                    module.IsDeleted = false;
                    await storage.UpsertModuleAsync(module, cancellationToken);
                }
            }

            var syncedModules = 0;
            var changedCards = 0;
            var audioUpdated = 0;
            var lastSyncAt = await storage.GetLastSyncAtAsync(cancellationToken);

            var updatedFolders = await storage.GetFoldersAsync(cancellationToken);

            foreach (var folder in updatedFolders)
            {
                var modules = await storage.GetModulesByFolderAsync(folder.Id, cancellationToken);
                foreach (var module in modules)
                {
                    var localCards = await storage.GetAllCardsByModuleAsync(module.Id, cancellationToken);

                    var snapshot = await cloudSyncClient.SyncModuleAsync(
                        functionKey,
                        module,
                        localCards,
                        lastSyncAt,
                        cancellationToken);

                    var result = await ReconcileModuleCardsAsync(functionKey, module, localCards, snapshot.Cards, cancellationToken);
                    changedCards += result.ChangedCards;
                    audioUpdated += result.AudioUpdated;
                    syncedModules++;
                }
            }

            await storage.SetLastSyncAtAsync(DateTimeOffset.UtcNow, cancellationToken);
            return new SyncResult(true, $"Synced {syncedModules} modules, {changedCards} cards, {audioUpdated} audio files");
        }
        catch (Exception globalEx)
        {
            var shortTrace = globalEx.StackTrace?.Split('\n').FirstOrDefault()?.Trim();
            return new SyncResult(false, $"CRASH: {globalEx.Message} | {shortTrace}");
        }
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
            if (cloudFolder.IsDeleted)
            {
                await storage.MarkDeletedAsync(cloudFolder.Id, nameof(Folder), cancellationToken);
            }
            else
            {
                var folder = await storage.GetFolderAsync(cloudFolder.Id, cancellationToken) ?? new Folder { Id = cloudFolder.Id };
                folder.Name = cloudFolder.Name;
                folder.ParentId = cloudFolder.ParentId;
                folder.UpdatedAt = cloudFolder.LastUpdated;
                folder.IsDirty = false;
                await storage.UpsertFolderAsync(folder, cancellationToken);
            }
        }

        foreach (var cloudModule in snapshot.Modules)
        {
            if (cloudModule.IsDeleted)
            {
                await storage.MarkDeletedAsync(cloudModule.Id, nameof(Module), cancellationToken);
            }
            else
            {
                var module = await storage.GetModuleAsync(cloudModule.Id, cancellationToken) ?? new Module { Id = cloudModule.Id };
                module.FolderId = cloudModule.FolderId;
                module.Name = cloudModule.Name;
                module.UpdatedAt = cloudModule.LastUpdated;
                module.IsDirty = false;
                await storage.UpsertModuleAsync(module, cancellationToken);
            }
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

        foreach (var cloudCard in cloudCards)
        {
            try
            {
                if (cloudCard.IsDeleted)
                {
                    if (localById.ContainsKey(cloudCard.Id))
                    {
                        await storage.MarkDeletedAsync(cloudCard.Id, nameof(Card), cancellationToken);
                        changedCards++;
                    }

                    continue;
                }

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
                        try
                        {
                            await using var audioStream = await cloudSyncClient.DownloadAudioAsync(functionKey, cloudCard.AudioUrl, cancellationToken);
                            card.AudioLocalPath = await audioFileStore.SaveAsync(card.Id, audioStream, cancellationToken);
                            card.AudioStatus = AudioStatus.Ready;
                            audioUpdated++;
                        }
                        catch (Exception audioEx)
                        {
                            Console.WriteLine($"Audio failed for {card.Id}: {audioEx.Message}");
                        }
                    }
                }
                else if (card.AudioStatus != AudioStatus.Ready)
                {
                    try
                    {
                        await using var generatedAudio = await cloudSyncClient.GenerateSpeechAsync(functionKey, card.Id, card.FrontText, cancellationToken);
                        card.AudioLocalPath = await audioFileStore.SaveAsync(card.Id, generatedAudio, cancellationToken);
                        card.AudioStatus = AudioStatus.Ready;
                        audioUpdated++;
                    }
                    catch (Exception audioEx)
                    {
                        Console.WriteLine($"Audio failed for {card.Id}: {audioEx.Message}");
                    }
                }

                await storage.UpsertCardAsync(card, cancellationToken);
            }
            catch (Exception)
            {
                continue;
            }
        }

        return new ReconcileResult(changedCards, audioUpdated);
    }
}

public sealed record SyncResult(bool Success, string Message);

sealed record ReconcileResult(int ChangedCards, int AudioUpdated);