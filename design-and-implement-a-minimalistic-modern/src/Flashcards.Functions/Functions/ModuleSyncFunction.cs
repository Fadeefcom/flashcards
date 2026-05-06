using System.Net.Http.Json;
using RecallCraft.Application.Abstractions;
using RecallCraft.Domain.Entities;

namespace RecallCraft.Pwa.Services;

public sealed class AzureFunctionCloudSyncClient(HttpClient httpClient, ICloudConfigurationStore configuration) : ICloudSyncClient
{
    public async Task<ModuleSyncSnapshot> SyncModuleAsync(
        string functionKey,
        Module module,
        IReadOnlyList<Card> localCards,
        DateTimeOffset? lastKnownUpdate,
        CancellationToken cancellationToken)
    {
        var request = new ModuleSyncRequest(
            module.Id,
            lastKnownUpdate,
            localCards.Select(ToCloudCard).ToList());

        using var message = new HttpRequestMessage(HttpMethod.Post, await BuildUrlAsync("modules/cards/sync", cancellationToken))
        {
            Content = JsonContent.Create(request)
        };
        AddFunctionKey(message, functionKey);

        using var response = await httpClient.SendAsync(message, cancellationToken);
        response.EnsureSuccessStatusCode();

        var snapshot = await response.Content.ReadFromJsonAsync<ModuleSyncSnapshot>(cancellationToken);
        return snapshot ?? throw new InvalidOperationException("Module sync returned an empty response.");
    }

    public async Task<HierarchySnapshot> SyncHierarchyAsync(
        string functionKey,
        IReadOnlyList<Folder> localFolders,
        IReadOnlyList<Module> localModules,
        CancellationToken cancellationToken)
    {
        var request = new HierarchySyncRequest(
            localFolders.Select(ToCloudFolder).ToList(),
            localModules.Select(ToCloudModule).ToList());

        using var message = new HttpRequestMessage(HttpMethod.Post, await BuildUrlAsync("hierarchy/sync", cancellationToken))
        {
            Content = JsonContent.Create(request)
        };
        AddFunctionKey(message, functionKey);

        using var response = await httpClient.SendAsync(message, cancellationToken);
        response.EnsureSuccessStatusCode();

        var snapshot = await response.Content.ReadFromJsonAsync<HierarchySnapshot>(cancellationToken);
        return snapshot ?? new HierarchySnapshot([], []);
    }

    public async Task<HierarchySnapshot> PullHierarchyAsync(string functionKey, DateTimeOffset? lastKnownUpdate, CancellationToken cancellationToken)
    {
        var queryString = lastKnownUpdate.HasValue ? $"?since={lastKnownUpdate.Value:O}" : string.Empty;
        var url = await BuildUrlAsync($"hierarchy/sync{queryString}", cancellationToken);

        using var message = new HttpRequestMessage(HttpMethod.Get, url);
        AddFunctionKey(message, functionKey);

        using var response = await httpClient.SendAsync(message, cancellationToken);
        response.EnsureSuccessStatusCode();

        var snapshot = await response.Content.ReadFromJsonAsync<HierarchySnapshot>(cancellationToken);
        return snapshot ?? new HierarchySnapshot([], []);
    }

    public async Task<Stream> GenerateSpeechAsync(
        string functionKey,
        Guid cardId,
        string text,
        CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, await BuildUrlAsync("GenerateSpeech", cancellationToken))
        {
            Content = JsonContent.Create(new TtsRequest(text, cardId))
        };
        AddFunctionKey(message, functionKey);

        var response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStreamAsync(cancellationToken);
    }

    public async Task<Stream> DownloadAudioAsync(
        string functionKey,
        string audioUrl,
        CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, audioUrl);

        var response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStreamAsync(cancellationToken);
    }

    private async Task<string> BuildUrlAsync(string path, CancellationToken cancellationToken)
    {
        var baseUrl = await configuration.GetFunctionBaseUrlAsync(cancellationToken);
        return $"{baseUrl.TrimEnd('/')}/{path.TrimStart('/')}";
    }

    private static CloudCard ToCloudCard(Card card) => new(
        card.Id,
        card.ModuleId,
        card.FrontText,
        card.BackText,
        card.UpdatedAt,
        card.AudioStatus.ToString(),
        null,
        null,
        card.IsDeleted);

    private static CloudFolder ToCloudFolder(Folder folder) => new(
        folder.Id,
        folder.Name,
        folder.ParentId,
        folder.UpdatedAt,
        folder.IsDeleted);

    private static CloudModule ToCloudModule(Module module) => new(
        module.Id,
        module.FolderId,
        module.Name,
        module.UpdatedAt,
        module.IsDeleted);

    private static void AddFunctionKey(HttpRequestMessage message, string functionKey)
    {
        if (!string.IsNullOrWhiteSpace(functionKey))
        {
            message.Headers.TryAddWithoutValidation("x-functions-key", functionKey);
        }
    }

    private sealed record ModuleSyncRequest(
        Guid ModuleId,
        DateTimeOffset? LastKnownUpdate,
        IReadOnlyList<CloudCard> LocalCards);

    private sealed record HierarchySyncRequest(
        IReadOnlyList<CloudFolder> LocalFolders,
        IReadOnlyList<CloudModule> LocalModules);

    private sealed record TtsRequest(string Text, Guid CardId);
}