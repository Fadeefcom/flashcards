using Microsoft.JSInterop;
using RecallCraft.Application.Abstractions;

namespace RecallCraft.Pwa.Services;

public sealed class BrowserCredentialStore(IJSRuntime js) : ISecureCredentialStore
{
    private const string ApiKeyName = "recallcraft.apiKey";

    public async Task<string?> GetApiKeyAsync(CancellationToken cancellationToken) =>
        await js.InvokeAsync<string?>("recallCraft.get", cancellationToken, ApiKeyName);

    public async Task SaveApiKeyAsync(string apiKey, CancellationToken cancellationToken) =>
        await js.InvokeVoidAsync("recallCraft.set", cancellationToken, ApiKeyName, apiKey.Trim());

    public async Task ClearApiKeyAsync(CancellationToken cancellationToken) =>
        await js.InvokeVoidAsync("recallCraft.remove", cancellationToken, ApiKeyName);
}
