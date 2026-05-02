using Microsoft.JSInterop;
using RecallCraft.Application.Abstractions;

namespace RecallCraft.Pwa.Services;

public sealed class BrowserCloudConfigurationStore(IJSRuntime js) : ICloudConfigurationStore
{
    private const string FunctionBaseUrlKey = "flashcards.functionBaseUrl";
    private const string DefaultFunctionBaseUrl = "http://localhost:7071/api";

    public async Task<string> GetFunctionBaseUrlAsync(CancellationToken cancellationToken)
    {
        var value = await js.InvokeAsync<string?>("recallCraft.get", cancellationToken, FunctionBaseUrlKey);
        return string.IsNullOrWhiteSpace(value) ? DefaultFunctionBaseUrl : value.Trim();
    }

    public async Task SaveFunctionBaseUrlAsync(string baseUrl, CancellationToken cancellationToken) =>
        await js.InvokeVoidAsync("recallCraft.set", cancellationToken, FunctionBaseUrlKey, baseUrl.Trim());
}
