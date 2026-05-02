namespace RecallCraft.Application.Abstractions;

public interface ICloudConfigurationStore
{
    Task<string> GetFunctionBaseUrlAsync(CancellationToken cancellationToken);
    Task SaveFunctionBaseUrlAsync(string baseUrl, CancellationToken cancellationToken);
}
