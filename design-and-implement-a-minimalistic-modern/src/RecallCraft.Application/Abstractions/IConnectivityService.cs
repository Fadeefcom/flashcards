namespace RecallCraft.Application.Abstractions;

public interface IConnectivityService
{
    Task<bool> IsInternetAvailableAsync(CancellationToken cancellationToken);
}
