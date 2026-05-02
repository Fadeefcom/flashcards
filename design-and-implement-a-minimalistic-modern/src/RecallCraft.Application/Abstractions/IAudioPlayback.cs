namespace RecallCraft.Application.Abstractions;

public interface IAudioPlayback
{
    Task PlayAsync(Stream audioStream, CancellationToken cancellationToken);
}
