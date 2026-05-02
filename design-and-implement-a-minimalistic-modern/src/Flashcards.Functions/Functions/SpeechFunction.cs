using System.Net;
using System.Security;
using System.Text;
using Azure.Storage.Blobs;
using Flashcards.Functions.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Flashcards.Functions.Functions;

public sealed class SpeechFunction
{
    private readonly HttpClient _httpClient;
    private readonly BlobServiceClient _blobServiceClient;
    private readonly ILogger<SpeechFunction> _logger;
    private readonly string _speechKey = Environment.GetEnvironmentVariable("SpeechKey") ?? string.Empty;
    private readonly string _speechRegion = Environment.GetEnvironmentVariable("SpeechRegion") ?? "francecentral";
    private readonly string _containerName = Environment.GetEnvironmentVariable("ContainerName") ?? "flashcards";

    public SpeechFunction(
        IHttpClientFactory httpClientFactory,
        BlobServiceClient blobServiceClient,
        ILogger<SpeechFunction> logger)
    {
        _httpClient = httpClientFactory.CreateClient();
        _blobServiceClient = blobServiceClient;
        _logger = logger;
    }

    [Function("GenerateSpeech")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        var data = await req.ReadFromJsonAsync<TtsRequest>(cancellationToken);
        if (data is null || data.CardId == Guid.Empty || string.IsNullOrWhiteSpace(data.Text))
        {
            return req.CreateResponse(HttpStatusCode.BadRequest);
        }

        if (string.IsNullOrWhiteSpace(_speechKey))
        {
            _logger.LogError("SpeechKey is not configured.");
            return req.CreateResponse(HttpStatusCode.InternalServerError);
        }

        var blobContainer = _blobServiceClient.GetBlobContainerClient(_containerName);
        await blobContainer.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

        var blobClient = blobContainer.GetBlobClient(GetAudioBlobName(data.CardId));
        if (await blobClient.ExistsAsync(cancellationToken))
        {
            var stream = await blobClient.OpenReadAsync(cancellationToken: cancellationToken);
            return await CreateAudioResponse(req, stream, cancellationToken);
        }

        var audioContent = await RequestTtsStreamAsync(data.Text, cancellationToken);
        await using (var uploadStream = new MemoryStream(audioContent))
        {
            await blobClient.UploadAsync(uploadStream, overwrite: true, cancellationToken);
        }

        return await CreateAudioResponse(req, new MemoryStream(audioContent), cancellationToken);
    }

    public static string GetAudioBlobName(Guid cardId) => $"audio/{cardId:N}.mp3";

    private async Task<byte[]> RequestTtsStreamAsync(string text, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, GetTtsEndpoint());
        request.Headers.Add("Ocp-Apim-Subscription-Key", _speechKey);
        request.Headers.Add("X-Microsoft-OutputFormat", "audio-16khz-128kbitrate-mono-mp3");
        request.Headers.UserAgent.ParseAdd("FlashcardsAzureFunction");

        var escapedText = SecurityElement.Escape(text);
        var ssml = $"""
        <speak version="1.0" xml:lang="pt-PT">
            <voice xml:lang="pt-PT" xml:gender="Male" name="pt-PT-DuarteNeural">
            <break time="200ms"/>        
                {escapedText}
            </voice>
        </speak>
        """;

        request.Content = new StringContent(ssml, Encoding.UTF8, "application/ssml+xml");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    private Uri GetTtsEndpoint() =>
        new($"https://{_speechRegion}.tts.speech.microsoft.com/cognitiveservices/v1");

    private static async Task<HttpResponseData> CreateAudioResponse(
        HttpRequestData req,
        Stream audioStream,
        CancellationToken cancellationToken)
    {
        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "audio/mpeg");
        await audioStream.CopyToAsync(response.Body, cancellationToken);
        return response;
    }
}
