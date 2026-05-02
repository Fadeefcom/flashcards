using Microsoft.JSInterop;
using RecallCraft.Application.Abstractions;

namespace RecallCraft.Pwa.Services;

public sealed class BrowserAudioFileStore(IJSRuntime js) : IAudioFileStore
{
    private const string Prefix = "recallcraft.audio.";

    public async Task<bool> ExistsAsync(string localPath, CancellationToken cancellationToken) =>
        await js.InvokeAsync<string?>("recallCraft.get", cancellationToken, localPath) is not null;

    public async Task<string> SaveAsync(Guid cardId, Stream audioStream, CancellationToken cancellationToken)
    {
        using var memory = new MemoryStream();
        await audioStream.CopyToAsync(memory, cancellationToken);
        var path = $"{Prefix}{cardId:N}";
        await js.InvokeVoidAsync("recallCraft.set", cancellationToken, path, Convert.ToBase64String(memory.ToArray()));
        return path;
    }

    public async Task<Stream?> OpenReadAsync(string localPath, CancellationToken cancellationToken)
    {
        var value = await js.InvokeAsync<string?>("recallCraft.get", cancellationToken, localPath);
        return string.IsNullOrWhiteSpace(value) ? null : new MemoryStream(Convert.FromBase64String(value));
    }
}
