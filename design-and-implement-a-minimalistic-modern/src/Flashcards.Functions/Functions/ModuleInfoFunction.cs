using System.Net;
using System.Text.Json;
using Azure;
using Azure.Storage.Blobs;
using Flashcards.Functions.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Flashcards.Functions.Functions;

public sealed class ModuleInfoFunction
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly string _containerName = Environment.GetEnvironmentVariable("ContainerName") ?? "flashcards";
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public ModuleInfoFunction(BlobServiceClient blobServiceClient)
    {
        _blobServiceClient = blobServiceClient;
    }

    [Function("GetModuleInfo")]
    public async Task<HttpResponseData> Get(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "modules/{moduleId:guid}/info")] HttpRequestData req,
        Guid moduleId,
        CancellationToken cancellationToken)
    {
        var container = _blobServiceClient.GetBlobContainerClient(_containerName);
        var blob = container.GetBlobClient(GetModuleInfoBlobName(moduleId));
        var info = await ReadModuleInfoAsync(blob, moduleId, cancellationToken);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(info, cancellationToken);
        return response;
    }

    [Function("SaveModuleInfo")]
    public async Task<HttpResponseData> Save(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "modules/{moduleId:guid}/info")] HttpRequestData req,
        Guid moduleId,
        CancellationToken cancellationToken)
    {
        var info = await req.ReadFromJsonAsync<ModuleInfo>(cancellationToken);
        if (info is null || info.ModuleId != moduleId)
        {
            return req.CreateResponse(HttpStatusCode.BadRequest);
        }

        var container = _blobServiceClient.GetBlobContainerClient(_containerName);
        await container.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

        var blob = container.GetBlobClient(GetModuleInfoBlobName(moduleId));
        await WriteModuleInfoAsync(blob, info, cancellationToken);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(info, cancellationToken);
        return response;
    }

    public static string GetModuleInfoBlobName(Guid moduleId) => $"modules/{moduleId:N}/module.json";

    public static ModuleInfo CreateModuleInfo(Guid moduleId, IReadOnlyList<ModuleCardSyncItem> cards)
    {
        var activeCards = cards.Where(card => !card.IsDeleted).ToList();
        var allDates = cards.Select(card => card.LastUpdated).DefaultIfEmpty(DateTimeOffset.UtcNow).ToList();
        var createdAt = allDates.Min();
        var lastUpdated = allDates.Max();

        return new ModuleInfo(
            moduleId,
            createdAt,
            lastUpdated,
            cards.Count,
            activeCards.Count,
            cards.Count - activeCards.Count,
            activeCards.Sum(card => CountWords(card.FrontText) + CountWords(card.BackText)),
            lastUpdated.ToUnixTimeMilliseconds().ToString());
    }

    public static async Task WriteModuleInfoAsync(
        BlobClient blob,
        ModuleInfo info,
        CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();
        await JsonSerializer.SerializeAsync(stream, info, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }, cancellationToken);
        stream.Position = 0;
        await blob.UploadAsync(stream, overwrite: true, cancellationToken);
    }

    private async Task<ModuleInfo> ReadModuleInfoAsync(BlobClient blob, Guid moduleId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await blob.DownloadContentAsync(cancellationToken);
            return JsonSerializer.Deserialize<ModuleInfo>(response.Value.Content, _jsonOptions)
                   ?? new ModuleInfo(moduleId, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 0, 0, 0, 0, "0");
        }
        catch (RequestFailedException ex) when (ex.Status == (int)HttpStatusCode.NotFound)
        {
            return new ModuleInfo(moduleId, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 0, 0, 0, 0, "0");
        }
    }

    private static int CountWords(string value) =>
        value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
}
