using PerfMonitor.Core.Shell;

namespace PerfMonitor.Core.Tests;

public sealed class SettingsIdleRecycleTimerTests
{
    [Fact(DisplayName = "回收定时器到期触发一次且不重复触发")]
    public async Task Elapses_once_when_the_scheduled_delay_expires()
    {
        using var timer = new SettingsIdleRecycleTimer();
        var elapsed = new ManualResetEventSlim(false);
        var fireCount = 0;
        timer.Elapsed += (_, _) =>
        {
            Interlocked.Increment(ref fireCount);
            elapsed.Set();
        };

        timer.Schedule(TimeSpan.FromMilliseconds(80));
        Assert.True(elapsed.Wait(TimeSpan.FromSeconds(5)), "排程的回收定时器未在时限内触发。");
        await Task.Delay(150);

        Assert.Equal(1, Volatile.Read(ref fireCount));
    }

    [Fact(DisplayName = "取消后不再触发回收")]
    public async Task Cancelled_timer_does_not_elapse()
    {
        using var timer = new SettingsIdleRecycleTimer();
        var fireCount = 0;
        timer.Elapsed += (_, _) => Interlocked.Increment(ref fireCount);

        timer.Schedule(TimeSpan.FromMilliseconds(100));
        timer.Cancel();
        await Task.Delay(300);

        Assert.Equal(0, Volatile.Read(ref fireCount));
    }

    [Fact(DisplayName = "重复排程替换前次待触发项")]
    public async Task Rescheduling_replaces_the_previous_pending_trigger()
    {
        using var timer = new SettingsIdleRecycleTimer();
        var elapsed = new ManualResetEventSlim(false);
        var fireCount = 0;
        timer.Elapsed += (_, _) =>
        {
            Interlocked.Increment(ref fireCount);
            elapsed.Set();
        };

        timer.Schedule(TimeSpan.FromSeconds(5));
        timer.Schedule(TimeSpan.FromMilliseconds(80));
        Assert.True(elapsed.Wait(TimeSpan.FromSeconds(5)), "第二次排程的回收定时器未在时限内触发。");
        await Task.Delay(150);

        Assert.Equal(1, Volatile.Read(ref fireCount));
    }

    [Fact(DisplayName = "释放后排程与触发都不再生效")]
    public async Task Disposed_timer_neither_elapses_nor_reschedules()
    {
        var timer = new SettingsIdleRecycleTimer();
        var fireCount = 0;
        timer.Elapsed += (_, _) => Interlocked.Increment(ref fireCount);

        timer.Schedule(TimeSpan.FromMilliseconds(80));
        timer.Dispose();
        await Task.Delay(300);

        Assert.Equal(0, Volatile.Read(ref fireCount));
        Assert.Throws<ObjectDisposedException>(() => timer.Schedule(TimeSpan.FromMilliseconds(10)));

        timer.Dispose();
    }

    [Fact(DisplayName = "负延迟被拒绝")]
    public void Negative_delay_is_rejected()
    {
        using var timer = new SettingsIdleRecycleTimer();

        Assert.Throws<ArgumentOutOfRangeException>(() => timer.Schedule(TimeSpan.FromMilliseconds(-5)));
    }
}
