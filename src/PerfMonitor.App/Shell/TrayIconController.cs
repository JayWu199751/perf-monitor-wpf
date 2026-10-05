using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingIcon = System.Drawing.Icon;
using Forms = System.Windows.Forms;
using PerfMonitor.Windows.Shell;

namespace PerfMonitor.App.Shell;

/// <summary>
/// 托盘图标生命周期：创建 WinForms NotifyIcon、按有效主题与 DPI 选择物理像素资源、
/// 释放时先撤下图标再销毁 NotifyIcon 与 HICON，避免幽灵图标或句柄泄漏。
/// explorer 重启后 WinForms NotifyIcon 内部监听 TaskbarCreated 广播并自动重新注册。
/// </summary>
internal sealed class TrayIconController : IDisposable
{
    private readonly string _resourceDirectory;
    private readonly Action _leftClick;
    private readonly Action _rightClick;
    private Forms.NotifyIcon? _notifyIcon;
    private nint _currentHIcon;
    private bool _isDarkEffective;
    private double _dpiScale = 1.0;
    private bool _disposed;

    public TrayIconController(string resourceDirectory, Action leftClick, Action rightClick)
    {
        _resourceDirectory = resourceDirectory;
        _leftClick = leftClick;
        _rightClick = rightClick;
    }

    /// <summary>创建或更新托盘图标；重复调用只刷新图标资源。</summary>
    public void Show(bool isDarkEffective, double dpiScale)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _isDarkEffective = isDarkEffective;
        _dpiScale = dpiScale;
        if (_notifyIcon is null)
        {
            _notifyIcon = new Forms.NotifyIcon
            {
                Text = "性能小窗",
                Visible = true
            };
            _notifyIcon.MouseClick += OnMouseClick;
        }

        ApplyIcon();
    }

    /// <summary>主题或 DPI 变化时按新参数重选图标资源。</summary>
    public void UpdateIcon(bool isDarkEffective, double dpiScale)
    {
        if (_disposed || _notifyIcon is null)
        {
            return;
        }

        _isDarkEffective = isDarkEffective;
        _dpiScale = dpiScale;
        ApplyIcon();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_notifyIcon is not null)
        {
            _notifyIcon.MouseClick -= OnMouseClick;
            // 先撤下图标再释放，Explorer 托盘区不留幽灵图标。
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }

        ReleaseCurrentHIcon();
    }

    private void OnMouseClick(object? sender, Forms.MouseEventArgs e)
    {
        if (e.Button == Forms.MouseButtons.Left)
        {
            _leftClick();
        }
        else if (e.Button == Forms.MouseButtons.Right)
        {
            _rightClick();
        }
    }

    private void ApplyIcon()
    {
        if (_notifyIcon is null)
        {
            return;
        }

        var fileName = TrayIconCatalog.SelectFileName(_isDarkEffective, _dpiScale);
        var newHIcon = LoadHIcon(Path.Combine(_resourceDirectory, fileName));
        if (newHIcon == 0)
        {
            return;
        }

        var oldHIcon = _currentHIcon;
        _currentHIcon = newHIcon;
        _notifyIcon.Icon = DrawingIcon.FromHandle(newHIcon);
        if (oldHIcon != 0)
        {
            DestroyIcon(oldHIcon);
        }
    }

    private void ReleaseCurrentHIcon()
    {
        if (_currentHIcon != 0)
        {
            DestroyIcon(_currentHIcon);
            _currentHIcon = 0;
        }
    }

    /// <summary>PNG 转 HICON；调用方拥有返回句柄，须用 DestroyIcon 释放。</summary>
    private static nint LoadHIcon(string path)
    {
        try
        {
            using var bitmap = new DrawingBitmap(path);
            return bitmap.GetHicon();
        }
        catch (Exception exception) when (
            exception is IOException ||
            exception is ArgumentException ||
            exception is ExternalException ||
            exception is Win32Exception)
        {
            return 0;
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(nint hIcon);
}
