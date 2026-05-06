using System.Net;
using System.Text.Json;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Flashcards.Functions.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Flashcards.Functions.Functions;

public sealed class HierarchySyncFunction
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly ILogger<HierarchySyncFunction> _logger;
    private readonly string _containerName = Environment.GetEnvironmentVariable("ContainerName") ?? "flashcards";
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public HierarchySyncFunction(BlobServiceClient blobServiceClient, ILogger<HierarchySyncFunction> logger)
    {
        _blobServiceClient = blobServiceClient;
        _logger = logger;
    }

    [Function("SyncHierarchy")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", "get", Route = "hierarchy/sync")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        var container = _blobServiceClient.GetBlobContainerClient(_containerName);
        await container.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

        var blob = container.GetBlobClient("hierarchy.json");
        HierarchySnapshot remoteHierarchy;

        var request = req.Method.Equals("POST", StringComparison.OrdinalIgnoreCase)
            ? await req.ReadFromJsonAsync<HierarchySyncRequest>(cancellationToken)
            : null;

        while (true)
        {
            var (currentHierarchy, eTag) = await ReadRemoteHierarchyAsync(blob, cancellationToken);
            remoteHierarchy = currentHierarchy;

            if (request is not null)
            {
                var mergedFolders = MergeFolders(remoteHierarchy.Folders, request.LocalFolders ?? []);
                var mergedModules = MergeModules(remoteHierarchy.Modules, request.LocalModules ?? []);

                remoteHierarchy = new HierarchySnapshot(mergedFolders, mergedModules);
                try
                {
                    await WriteRemoteHierarchyAsync(blob, remoteHierarchy, eTag, cancellationToken);
                }
                catch (RequestFailedException ex) when (ex.Status == (int)HttpStatusCode.PreconditionFailed)
                {
                    continue;
                }
            }
            break;
        }

        _logger.LogInformation("Synced hierarchy. Returned {FolderCount} folders, {ModuleCount} modules.", remoteHierarchy.Folders.Count, remoteHierarchy.Modules.Count);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(remoteHierarchy, cancellationToken);
        return response;
    }

    private async Task<(HierarchySnapshot Snapshot, ETag? ETag)> ReadRemoteHierarchyAsync(BlobClient blob, CancellationToken cancellationToken)
    {
        try
        {
            var response = await blob.DownloadContentAsync(cancellationToken);
            var snapshot = JsonSerializer.Deserialize<HierarchySnapshot>(response.Value.Content, _jsonOptions)
                           ?? new HierarchySnapshot([], []);
            return (snapshot, response.Value.Details.ETag);
        }
        catch (RequestFailedException ex) when (ex.Status == (int)HttpStatusCode.NotFound)
        {
            return (new HierarchySnapshot([], []), null);
        }
    }

    private async Task WriteRemoteHierarchyAsync(
        BlobClient blob,
        HierarchySnapshot hierarchy,
        ETag? eTag,
        CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();
        await JsonSerializer.SerializeAsync(stream, hierarchy, _jsonOptions, cancellationToken);
        stream.Position = 0;

        var conditions = eTag.HasValue
            ? new BlobRequestConditions { IfMatch = eTag.Value }
            : new BlobRequestConditions { IfNoneMatch = ETag.All };

        var options = new BlobUploadOptions { Conditions = conditions };

        await blob.UploadAsync(stream, options, cancellationToken);
    }

    private static List<CloudFolder> MergeFolders(
        IReadOnlyList<CloudFolder> remoteFolders,
        IReadOnlyList<CloudFolder> localFolders)
    {
        var merged = remoteFolders.ToDictionary(f => f.Id);
        foreach (var localFolder in localFolders)
        {
            if (!merged.TryGetValue(localFolder.Id, out var remoteFolder) || localFolder.LastUpdated >= remoteFolder.LastUpdated)
            {
                merged[localFolder.Id] = localFolder;
            }
        }

        return merged.Values.OrderBy(f => f.LastUpdated).ToList();
    }

    private static List<CloudModule> MergeModules(
        IReadOnlyList<CloudModule> remoteModules,
        IReadOnlyList<CloudModule> localModules)
    {
        var merged = remoteModules.ToDictionary(m => m.Id);
        foreach (var localModule in localModules)
        {
            if (!merged.TryGetValue(localModule.Id, out var remoteModule) || localModule.LastUpdated >= remoteModule.LastUpdated)
            {
                merged[localModule.Id] = localModule;
            }
        }

        return merged.Values.OrderBy(m => m.LastUpdated).ToList();
    }
}