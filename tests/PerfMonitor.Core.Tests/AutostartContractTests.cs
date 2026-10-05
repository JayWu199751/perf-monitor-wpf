using PerfMonitor.Core.Metrics;
using PerfMonitor.Core.Settings;
using PerfMonitor.Core.Shell;

namespace PerfMonitor.Core.Tests;

public sealed class AutostartContractTests
{
    [Fact(DisplayName = "开启自启经端口执行并按系统实际状态回写保存")]
    public void Enabling_autostart_goes_through_the_port_and_persists_the_actual_state()
    {
        var store = new RecordingSettingsStore(PerformanceSettings.Default);
        var port = new RecordingAutostartPort(AutostartRequestOutcome.Enabled);
        var host = new AutostartRecordingHost(store) { AutostartPort = port };
        var shell = new StartupShellController(host);
        shell.Start();

        var saved = shell.UpdateSettings(new SettingsPatch { Autostart = true });

        Assert.True(port.Requests.Single());
        Assert.True(saved.Autostart);
        Assert.Equal(saved, shell.Settings);
        Assert.True(store.LastSaved!.Autostart);
    }

    [Fact(DisplayName = "关闭自启删除任务后按查询到的实际状态回写")]
    public void Disabling_autostart_persists_the_state_reported_by_the_port()
    {
        var initial = PerformanceSettings.Default with { Autostart = true };
        var store = new RecordingSettingsStore(initial);
        var port = new RecordingAutostartPort(AutostartRequestOutcome.Disabled);
        var host = new AutostartRecordingHost(store) { AutostartPort = port };
        var shell = new StartupShellController(host);
        shell.Start();

        var saved = shell.UpdateSettings(new SettingsPatch { Autostart = false });

        Assert.False(port.Requests.Single());
        Assert.False(saved.Autostart);
        Assert.False(store.LastSaved!.Autostart);
    }

    [Fact(DisplayName = "端口报告失败时不保存并抛出异常避免显示成功")]
    public void A_failed_port_operation_throws_and_does_not_persist()
    {
        var store = new RecordingSettingsStore(PerformanceSettings.Default);
        var port = new RecordingAutostartPort(AutostartRequestOutcome.Failed);
        var host = new AutostartRecordingHost(store) { AutostartPort = port };
        var shell = new StartupShellController(host);
        shell.Start();

        Assert.Throws<InvalidOperationException>(
            () => shell.UpdateSettings(new SettingsPatch { Autostart = true }));

        Assert.Null(store.LastSaved);
        Assert.False(shell.Settings.Autostart);
    }

    [Fact(DisplayName = "端口返回的实际状态与请求不同时以实际状态为准")]
    public void The_actual_state_reported_by_the_port_wins_over_the_requested_value()
    {
        var store = new RecordingSettingsStore(PerformanceSettings.Default);
        var port = new RecordingAutostartPort(AutostartRequestOutcome.Disabled);
        var host = new AutostartRecordingHost(store) { AutostartPort = port };
        var shell = new StartupShellController(host);
        shell.Start();

        var saved = shell.UpdateSettings(new SettingsPatch { Autostart = true });

        Assert.True(port.Requests.Single());
        Assert.False(saved.Autostart);
        Assert.False(store.LastSaved!.Autostart);
    }

    [Fact(DisplayName = "没有端口时仅持久化请求值，不触碰系统自启")]
    public void Without_a_port_the_requested_value_is_persisted_without_touching_the_system()
    {
        var store = new RecordingSettingsStore(PerformanceSettings.Default);
        var host = new AutostartRecordingHost(store);
        var shell = new StartupShellController(host);
        shell.Start();

        var saved = shell.UpdateSettings(new SettingsPatch { Autostart = true });

        Assert.True(saved.Autostart);
        Assert.True(store.LastSaved!.Autostart);
    }

    private sealed class RecordingSettingsStore : ISettingsStore
    {
        private readonly PerformanceSettings _initial;

        public RecordingSettingsStore(PerformanceSettings initial)
        {
            _initial = initial;
        }

        public PerformanceSettings? LastSaved { get; private set; }

        public PerformanceSettings Load() => LastSaved ?? _initial;

        public void Save(PerformanceSettings settings)
        {
            LastSaved = settings;
        }
    }

    private sealed class RecordingAutostartPort : IAutostartPort
    {
        private readonly AutostartRequestOutcome _outcome;

        public RecordingAutostartPort(AutostartRequestOutcome outcome)
        {
            _outcome = outcome;
        }

        public List<bool> Requests { get; } = [];

        public AutostartRequestOutcome TrySetEnabled(bool enabled)
        {
            Requests.Add(enabled);
            return _outcome;
        }
    }

    private sealed class AutostartRecordingHost : IStartupShellHost
    {
        private readonly ISettingsStore _settingsStore;

        public AutostartRecordingHost(ISettingsStore settingsStore)
        {
            _settingsStore = settingsStore;
        }

        public IAutostartPort? AutostartPort { get; init; }

        public ISettingsStore? SettingsStore => _settingsStore;

        public ISystemMetricsSource? SystemMetricsSource => null;

        public PerformanceSettings? AppliedSettings { get; private set; }

        public void SetPerformanceBarMoveRequestHandler(Action handler)
        {
        }

        public void BeginPerformanceBarNativeMove()
        {
        }

        public void ShowPerformanceBar(bool activate)
        {
        }

        public void SetPerformanceBarVisible(bool visible, bool activate)
        {
        }

        public void CreateTrayIcon(Action leftClick, Action rightClick)
        {
        }

        public void ShowContextMenu(
            ShellMenuOrigin origin,
            IReadOnlyList<ShellMenuItem> items,
            Action<ShellMenuAction> selectItem)
        {
        }

        public void ShowSettingsWindow()
        {
        }

        public void ApplySettings(PerformanceSettings settings)
        {
            AppliedSettings = settings;
        }

        public void HideSettingsWindow()
        {
        }

        public void SetMetricGeneration(long generation)
        {
        }

        public void UpdatePerformanceMetrics(PerformanceMetricsSnapshot snapshot)
        {
        }

        public void Shutdown()
        {
        }
    }
}
