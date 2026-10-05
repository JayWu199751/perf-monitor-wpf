namespace PerfMonitor.Core.Shell;

/// <summary>
/// 位置防抖持久化：重复调度只保留最后一次，静默期满后执行一次保存；
/// 支持退出/销毁时立即 flush。线程安全。
/// </summary>
public sealed class PlacementPersistence : IDisposable
{
    private readonly Action _save;
    private readonly TimeSpan _debounce;
    private readonly Timer _timer;
    private readonly object _sync = new();
    private bool _pending;
    private bool _disposed;

    public PlacementPersistence(Action save, int debounceMilliseconds = 500)
    {
        ArgumentNullException.ThrowIfNull(save);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(debounceMilliseconds);
        _save = save;
        _debounce = TimeSpan.FromMilliseconds(debounceMilliseconds);
        _timer = new Timer(OnTimer, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public void Schedule()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _pending = true;
            _timer.Change(_debounce, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>取消未保存的挂起项（位置已随其他设置一并持久化时使用）。</summary>
    public void ClearPending()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _pending = false;
            _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>有挂起项时立即保存，并取消已排定的防抖回调。</summary>
    public void Flush()
    {
        var shouldSave = false;
        lock (_sync)
        {
            if (!_disposed && _pending)
            {
                _pending = false;
                shouldSave = true;
                _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            }
        }

        if (shouldSave)
        {
            _save();
        }
    }

    private void OnTimer(object? state)
    {
        var shouldSave = false;
        lock (_sync)
        {
            if (!_disposed && _pending)
            {
                _pending = false;
                shouldSave = true;
            }
        }

        if (shouldSave)
        {
            _save();
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _pending = false;
        }

        _timer.Dispose();
    }
}
