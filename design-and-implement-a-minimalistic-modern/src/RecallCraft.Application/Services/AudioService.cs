using RecallCraft.Application.Abstractions;
using RecallCraft.Domain.Enums;

namespace RecallCraft.Application.Services;

public sealed class AudioService(
    ILocalStorageService storage,
    ICloudSyncClient cloudSyncClient,
    ISecureCredentialStore credentials,
    IAudioFileStore audioFileStore,
    IAudioPlayback playback)
{
    public async Task<Stream?> GetAudioAsync(Guid cardId, CancellationToken cancellationToken)
    {
        var card = await storage.GetCardAsync(cardId, cancellationToken);
        if (card is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(card.AudioLocalPath) &&
            await audioFileStore.ExistsAsync(card.AudioLocalPath, cancellationToken))
        {
            return await audioFileStore.OpenReadAsync(card.AudioLocalPath, cancellationToken);
        }

        var apiKey = await credentials.GetApiKeyAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        await using (var remoteStream = await cloudSyncClient.GenerateSpeechAsync(apiKey, card.Id, card.FrontText, cancellationToken))
        {
            card.AudioLocalPath = await audioFileStore.SaveAsync(card.Id, remoteStream, cancellationToken);
            card.AudioStatus = AudioStatus.Ready;
            card.MarkDirty();
            await storage.UpsertCardAsync(card, cancellationToken);
        }

        return await audioFileStore.OpenReadAsync(card.AudioLocalPath, cancellationToken);
    }

    public async Task PlayAudioAsync(Guid cardId, CancellationToken cancellationToken)
    {
        var stream = await GetAudioAsync(cardId, cancellationToken);
        if (stream is null)
        {
            return;
        }

        await using (stream)
        {
            await playback.PlayAsync(stream, cancellationToken);
        }
    }
}
