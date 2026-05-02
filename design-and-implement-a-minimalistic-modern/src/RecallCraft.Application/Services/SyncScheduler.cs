namespace RecallCraft.Application.Services;

public sealed class SyncScheduler(SyncService syncService, TimeSpan? interval = null)
{
    private readonly TimeSpan _interval = interval ?? TimeSpan.FromMinutes(15);
    private readonly SemaphoreSlim _syncGate = new(1, 1);
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private int _failureCount;

    public event EventHandler<SyncResult>? SyncCompleted;

    public void Start()
    {
        if (_loop is { IsCompleted: false })
        {
            return;
        }

        _cts = new CancellationTokenSource();
        _loop = RunLoopAsync(_cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
    }

    public Task<SyncResult> ForceSync(CancellationToken cancellationToken = default) =>
        RunSyncOnceAsync(cancellationToken);

    public Task<SyncResult> PullRemote(CancellationToken cancellationToken = default) =>
        PullRemoteOnceHierarchyAsync(cancellationToken);

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        await RunSyncOnceAsync(cancellationToken);

        while (!cancellationToken.IsCancellationRequested)
        {
            var delay = GetNextDelay();
            await Task.Delay(delay, cancellationToken);
            await RunSyncOnceAsync(cancellationToken);
        }
    }

    private TimeSpan GetNextDelay()
    {
        if (_failureCount == 0)
        {
            return _interval;
        }

        var backoffMinutes = Math.Min(60, Math.Pow(2, _failureCount));
        return TimeSpan.FromMinutes(backoffMinutes);
    }

    private async Task<SyncResult> RunSyncOnceAsync(CancellationToken cancellationToken)
    {
        if (!await _syncGate.WaitAsync(0, cancellationToken))
        {
            return new SyncResult(false, "Sync already running");
        }

        try
        {
            var result = await syncService.SyncAsync(cancellationToken);
            _failureCount = result.Success ? 0 : _failureCount + 1;
            SyncCompleted?.Invoke(this, result);
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _failureCount++;
            var result = new SyncResult(false, ex.Message);
            SyncCompleted?.Invoke(this, result);
            return result;
        }
        finally
        {
            _syncGate.Release();
        }
    }

    private async Task<SyncResult> PullRemoteOnceHierarchyAsync(CancellationToken cancellationToken)
    {
        if (!await _syncGate.WaitAsync(0, cancellationToken))
        {
            return new SyncResult(false, "Sync already running");
        }

        try
        {
            var result = await syncService.PullRemoteHierarchyAsync(cancellationToken);
            _failureCount = result.Success ? 0 : _failureCount + 1;
            SyncCompleted?.Invoke(this, result);
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _failureCount++;
            var result = new SyncResult(false, ex.Message);
            SyncCompleted?.Invoke(this, result);
            return result;
        }
        finally
        {
            _syncGate.Release();
        }
    }
}
