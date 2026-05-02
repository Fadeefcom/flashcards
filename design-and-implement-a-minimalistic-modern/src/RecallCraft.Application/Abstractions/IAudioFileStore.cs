namespace RecallCraft.Application.Abstractions;

public interface IAudioFileStore
{
    Task<bool> ExistsAsync(string localPath, CancellationToken cancellationToken);
    Task<string> SaveAsync(Guid cardId, Stream audioStream, CancellationToken cancellationToken);
    Task<Stream?> OpenReadAsync(string localPath, CancellationToken cancellationToken);
}
