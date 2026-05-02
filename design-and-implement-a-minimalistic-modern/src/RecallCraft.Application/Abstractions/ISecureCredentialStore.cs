namespace RecallCraft.Application.Abstractions;

public interface ISecureCredentialStore
{
    Task<string?> GetApiKeyAsync(CancellationToken cancellationToken);
    Task SaveApiKeyAsync(string apiKey, CancellationToken cancellationToken);
    Task ClearApiKeyAsync(CancellationToken cancellationToken);
}
