namespace PerfMonitor.Core.Shell;

public enum ShellMenuOrigin
{
    PerformanceBar,
    Tray
}

public enum ShellMenuAction
{
    OpenSettings,
    Exit
}

public sealed record ShellMenuItem(ShellMenuAction Action, string Label);

public sealed record StartupShellState(
    bool IsRunning,
    bool IsPerformanceBarVisible,
    bool IsTrayIconVisible,
    bool IsSettingsWindowCreated,
    bool IsSettingsWindowVisible);

public interface IStartupShellHost
{
    void SetPerformanceBarMoveRequestHandler(Action handler);

    void BeginPerformanceBarNativeMove();

    void ShowPerformanceBar(bool activate);

    void SetPerformanceBarVisible(bool visible, bool activate);

    void CreateTrayIcon(Action leftClick, Action rightClick);

    void ShowContextMenu(
        ShellMenuOrigin origin,
        IReadOnlyList<ShellMenuItem> items,
        Action<ShellMenuAction> selectItem);

    void ShowSettingsWindow();

    void HideSettingsWindow();

    void Shutdown();
}

public sealed class StartupShellController
{
    private static readonly IReadOnlyList<ShellMenuItem> BasicMenuItems = Array.AsReadOnly(
    [
        new ShellMenuItem(ShellMenuAction.OpenSettings, "打开设置"),
        new ShellMenuItem(ShellMenuAction.Exit, "退出")
    ]);

    private readonly IStartupShellHost _host;

    public StartupShellController(IStartupShellHost host)
    {
        _host = host;
        _host.SetPerformanceBarMoveRequestHandler(OnPerformanceBarNativeMoveRequested);
    }

    public StartupShellState State { get; private set; } = new(false, false, false, false, false);

    public void Start()
    {
        if (State.IsRunning)
        {
            return;
        }

        _host.ShowPerformanceBar(activate: false);
        _host.CreateTrayIcon(OnTrayLeftClick, OnTrayRightClick);
        State = new(true, true, true, false, false);
    }

    public void SelectMenuItem(ShellMenuAction action)
    {
        if (!State.IsRunning)
        {
            return;
        }

        switch (action)
        {
            case ShellMenuAction.OpenSettings:
                _host.ShowSettingsWindow();
                State = State with
                {
                    IsSettingsWindowCreated = true,
                    IsSettingsWindowVisible = true
                };
                break;
            case ShellMenuAction.Exit:
                State = new(false, false, false, false, false);
                _host.Shutdown();
                break;
        }
    }

    public void OnPerformanceBarRightClick()
    {
        ShowContextMenu(ShellMenuOrigin.PerformanceBar);
    }

    public void OnPerformanceBarNativeMoveRequested()
    {
        if (!State.IsRunning)
        {
            return;
        }

        _host.BeginPerformanceBarNativeMove();
    }

    public void OnSettingsWindowClosed()
    {
        if (!State.IsRunning || !State.IsSettingsWindowVisible)
        {
            return;
        }

        _host.HideSettingsWindow();
        State = State with { IsSettingsWindowVisible = false };
    }

    private void OnTrayLeftClick()
    {
        if (!State.IsRunning)
        {
            return;
        }

        var visible = !State.IsPerformanceBarVisible;
        _host.SetPerformanceBarVisible(visible, activate: visible);
        State = State with { IsPerformanceBarVisible = visible };
    }

    private void OnTrayRightClick()
    {
        ShowContextMenu(ShellMenuOrigin.Tray);
    }

    private void ShowContextMenu(ShellMenuOrigin origin)
    {
        if (!State.IsRunning)
        {
            return;
        }

        _host.ShowContextMenu(origin, BasicMenuItems, SelectMenuItem);
    }
}
