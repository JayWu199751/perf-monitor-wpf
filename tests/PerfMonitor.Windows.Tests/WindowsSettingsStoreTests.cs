using PerfMonitor.Core.Settings;
using PerfMonitor.Windows.Settings;

namespace PerfMonitor.Windows.Tests;

public sealed class WindowsSettingsStoreTests
{
    [Fact(DisplayName = "设置文件写入 schemaVersion 并在重启后保留指标和外观")]
    public void Settings_survive_a_store_restart_with_their_schema_version()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        var store = new WindowsSettingsStore(path);
        var expected = PerformanceSettings.Default with
        {
            Metrics = PerformanceSettings.Default.Metrics with { Cpu = false, Time = false },
            FastRefreshMilliseconds = 5000,
            SlowRefreshMilliseconds = 5000,
            CenterInTaskbarRow = true,
            Opacity = 0.85,
            FontSize = 17,
            Theme = BarTheme.Dark,
            Widget = new WidgetPlacement { X = 100, Y = 1040, Docked = DockedEdges.Bottom, InTaskbarRow = true }
        };

        store.Save(expected);
        var restored = new WindowsSettingsStore(path).Load();
        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));

        Assert.Equal(expected, restored);
        Assert.Equal(PerformanceSettings.CurrentSchemaVersion, document.RootElement.GetProperty("schemaVersion").GetInt32());
    }

    [Fact(DisplayName = "缺少行内标志的旧设置文件按不在行内加载")]
    public void Legacy_settings_without_the_in_row_flag_load_as_not_in_row()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        File.WriteAllText(path, "{\"schemaVersion\":1,\"widget\":{\"x\":100,\"y\":1050,\"docked\":\"bottom\"}}");

        var loaded = new WindowsSettingsStore(path).Load();

        Assert.False(loaded.Widget.InTaskbarRow);
        Assert.Equal(DockedEdges.Bottom, loaded.Widget.Docked);
    }

    [Theory(DisplayName = "损坏或越界设置会备份原文件并恢复默认")]
    [InlineData("{\"schemaVersion\":1,\"fastRefreshMilliseconds\":1500}")]
    [InlineData("{\"schemaVersion\":1,\"opacity\":0.83}")]
    public void Invalid_settings_are_preserved_as_a_backup_and_replaced_with_defaults(string invalidJson)
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        File.WriteAllText(path, invalidJson);

        var loaded = new WindowsSettingsStore(path).Load();
        var backup = Directory.GetFiles(directory.Path, "settings.json.corrupt-*").Single();

        Assert.Equal(PerformanceSettings.Default, loaded);
        Assert.Equal(invalidJson, File.ReadAllText(backup));
        Assert.Equal(PerformanceSettings.Default, new WindowsSettingsStore(path).Load());
    }

    [Fact(DisplayName = "默认配置目录使用独立的本地 PerfMonitorWpf 命名空间")]
    public void Default_settings_path_uses_an_isolated_user_local_directory()
    {
        var path = WindowsSettingsStore.GetDefaultPath();

        Assert.Equal("PerfMonitorWpf", Path.GetFileName(Path.GetDirectoryName(path)));
        Assert.Equal("settings.json", Path.GetFileName(path));
        Assert.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), path, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PerfMonitorWpf-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
