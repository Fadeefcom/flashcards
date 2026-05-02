using Microsoft.JSInterop;
using RecallCraft.Application.Abstractions;

namespace RecallCraft.Pwa.Services;

public sealed class BrowserConnectivityService(IJSRuntime js) : IConnectivityService
{
    public async Task<bool> IsInternetAvailableAsync(CancellationToken cancellationToken) =>
        await js.InvokeAsync<bool>("recallCraft.online", cancellationToken);
}
