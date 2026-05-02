using System.Net;
using System.Text.Json;
using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using Flashcards.Functions.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Flashcards.Functions.Functions;

public sealed class ModuleSyncFunction
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly ILogger<ModuleSyncFunction> _logger;
    private readonly string _containerName = Environment.GetEnvironmentVariable("ContainerName") ?? "flashcards";
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public ModuleSyncFunction(BlobServiceClient blobServiceClient, ILogger<ModuleSyncFunction> logger)
    {
        _blobServiceClient = blobServiceClient;
        _logger = logger;
    }

    [Function("SyncModule")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", "get", Route = "modules/sync")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        var request = await ReadRequestAsync(req, cancellationToken);
        if (request is null || request.ModuleId == Guid.Empty)
        {
            return req.CreateResponse(HttpStatusCode.BadRequest);
        }

        var container = _blobServiceClient.GetBlobContainerClient(_containerName);
        await container.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

        var blob = container.GetBlobClient(GetModuleCardsBlobName(request.ModuleId));
        var remoteCards = await ReadRemoteCardsAsync(blob, cancellationToken);

        if (request.LocalCards is { Count: > 0 })
        {
            remoteCards = MergeCards(remoteCards, request.LocalCards);
            await WriteRemoteCardsAsync(blob, remoteCards, cancellationToken);
        }

        DateTimeOffset? moduleLastUpdated = remoteCards.Count == 0 ? null : remoteCards.Max(card => card.LastUpdated);
        var responseCards = await EnrichAudioStateAsync(container, remoteCards, cancellationToken);

        _logger.LogInformation("Synced module {ModuleId}. Returned {CardCount} card(s).", request.ModuleId, responseCards.Count);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new ModuleSyncResponse(
            request.ModuleId,
            DateTimeOffset.UtcNow,
            moduleLastUpdated,
            responseCards), cancellationToken);
        return response;
    }

    private async Task<ModuleSyncRequest?> ReadRequestAsync(HttpRequestData req, CancellationToken cancellationToken)
    {
        if (req.Method.Equals("GET", StringComparison.OrdinalIgnoreCase))
        {
            var query = ParseQuery(req.Url.Query);
            return query.TryGetValue("moduleId", out var value) && Guid.TryParse(value, out var moduleId)
                ? new ModuleSyncRequest(moduleId)
                : null;
        }

        return await req.ReadFromJsonAsync<ModuleSyncRequest>(cancellationToken);
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        return query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(
                parts => Uri.UnescapeDataString(parts[0]),
                parts => Uri.UnescapeDataString(parts[1]),
                StringComparer.OrdinalIgnoreCase);
    }

    private async Task<List<ModuleCardSyncItem>> ReadRemoteCardsAsync(BlobClient blob, CancellationToken cancellationToken)
    {
        if (!await blob.ExistsAsync(cancellationToken))
        {
            return [];
        }

        await using var stream = await blob.OpenReadAsync(cancellationToken: cancellationToken);
        return await JsonSerializer.DeserializeAsync<List<ModuleCardSyncItem>>(stream, _jsonOptions, cancellationToken) ?? [];
    }

    private async Task WriteRemoteCardsAsync(
        BlobClient blob,
        IReadOnlyList<ModuleCardSyncItem> cards,
        CancellationToken cancellationToken)
    {
        await using var stream = new MemoryStream();
        await JsonSerializer.SerializeAsync(stream, cards, _jsonOptions, cancellationToken);
        stream.Position = 0;
        await blob.UploadAsync(stream, overwrite: true, cancellationToken);
    }

    private static List<ModuleCardSyncItem> MergeCards(
        IReadOnlyList<ModuleCardSyncItem> remoteCards,
        IReadOnlyList<ModuleCardSyncItem> localCards)
    {
        var merged = remoteCards.ToDictionary(card => card.Id);
        foreach (var localCard in localCards)
        {
            if (!merged.TryGetValue(localCard.Id, out var remoteCard) || localCard.LastUpdated >= remoteCard.LastUpdated)
            {
                merged[localCard.Id] = localCard;
            }
        }

        return merged.Values.OrderBy(card => card.LastUpdated).ToList();
    }

    private static string GetModuleCardsBlobName(Guid moduleId) => $"modules/{moduleId:N}/cards.json";

    private static async Task<List<ModuleCardSyncItem>> EnrichAudioStateAsync(
        BlobContainerClient container,
        IReadOnlyList<ModuleCardSyncItem> cards,
        CancellationToken cancellationToken)
    {
        var enrichedCards = new List<ModuleCardSyncItem>(cards.Count);
        foreach (var card in cards.OrderBy(card => card.LastUpdated))
        {
            var audioBlob = container.GetBlobClient(SpeechFunction.GetAudioBlobName(card.Id));
            if (!await audioBlob.ExistsAsync(cancellationToken))
            {
                enrichedCards.Add(card);
                continue;
            }

            var properties = await audioBlob.GetPropertiesAsync(cancellationToken: cancellationToken);
            enrichedCards.Add(card with
            {
                AudioStatus = "Ready",
                AudioUrl = CreateAudioUrl(audioBlob) ?? card.AudioUrl,
                AudioUpdatedAt = properties.Value.LastModified
            });
        }

        return enrichedCards;
    }

    private static string? CreateAudioUrl(BlobClient audioBlob)
    {
        if (!audioBlob.CanGenerateSasUri)
        {
            return null;
        }

        var builder = new BlobSasBuilder
        {
            BlobContainerName = audioBlob.BlobContainerName,
            BlobName = audioBlob.Name,
            Resource = "b",
            ExpiresOn = DateTimeOffset.UtcNow.AddHours(2)
        };
        builder.SetPermissions(BlobSasPermissions.Read);
        return audioBlob.GenerateSasUri(builder).ToString();
    }
}
