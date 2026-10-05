namespace PerfMonitor.Core.Shell;

/// <summary>设置窗闲置回收的默认参数。</summary>
public static class SettingsIdleRecycleDefaults
{
    /// <summary>设置窗隐藏后的闲置回收延迟。</summary>
    public static readonly TimeSpan Delay = TimeSpan.FromMinutes(5);
}

/// <summary>
/// <see cref="ISettingsIdleRecycleTimer"/> 的默认实现：单次 System.Threading.Timer，
/// 重复排程替换前次待触发项，取消与释放幂等；到期回调在 ThreadPool 线程触发。
/// </summary>
public sealed class SettingsIdleRecycleTimer : ISettingsIdleRecycleTimer
{
    private readonly object _sync = new();
    private Timer? _timer;
    private EventHandler? _elapsed;
    private bool _disposed;

    public event EventHandler? Elapsed
    {
        add
        {
            lock (_sync)
            {
                _elapsed += value;
            }
        }
        remove
        {
            lock (_sync)
            {
                _elapsed -= value;
            }
        }
    }

    public void Schedule(TimeSpan idleDelay)
    {
        if (idleDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(idleDelay), "闲置回收延迟不能为负。");
        }

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _timer?.Dispose();
            _timer = new Timer(OnTimerTick, null, idleDelay, Timeout.InfiniteTimeSpan);
        }
    }

    public void Cancel()
    {
        lock (_sync)
        {
            _timer?.Dispose();
            _timer = null;
        }
    }

    private void OnTimerTick(object? state)
    {
        EventHandler? handlers;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            // 单次触发：到期即丢弃定时器，之后 Cancel 无操作。
            _timer?.Dispose();
            _timer = null;
            handlers = _elapsed;
        }

        handlers?.Invoke(this, EventArgs.Empty);
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
            _timer?.Dispose();
            _timer = null;
        }
    }
}
