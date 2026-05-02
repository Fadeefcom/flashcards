using Microsoft.JSInterop;
using RecallCraft.Application.Abstractions;

namespace RecallCraft.Pwa.Services;

public sealed class BrowserAudioPlayback(IJSRuntime js) : IAudioPlayback
{
    public async Task PlayAsync(Stream audioStream, CancellationToken cancellationToken)
    {
        using var memory = new MemoryStream();
        await audioStream.CopyToAsync(memory, cancellationToken);
        var dataUrl = $"data:audio/mp4;base64,{Convert.ToBase64String(memory.ToArray())}";
        await js.InvokeVoidAsync("recallCraft.playDataUrl", cancellationToken, dataUrl);
    }
}
