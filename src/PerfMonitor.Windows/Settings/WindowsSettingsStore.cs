using System.Text.Json;
using System.Text.Json.Serialization;
using PerfMonitor.Core.Settings;

namespace PerfMonitor.Windows.Settings;

public sealed class WindowsSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();
    private readonly string _settingsPath;
    private readonly object _sync = new();

    public bool RecoveredInvalidSettingsOnLastLoad { get; private set; }

    public WindowsSettingsStore(string settingsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        _settingsPath = Path.GetFullPath(settingsPath);
    }

    public static string GetDefaultPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PerfMonitorWpf",
        "settings.json");

    public PerformanceSettings Load()
    {
        lock (_sync)
        {
            if (!File.Exists(_settingsPath))
            {
                return PerformanceSettings.Default;
            }

            try
            {
                var settings = JsonSerializer.Deserialize<PerformanceSettings>(File.ReadAllBytes(_settingsPath), SerializerOptions)
                    ?? throw new InvalidDataException("设置文件内容为空。");
                return settings.Validate();
            }
            catch (Exception exception) when (
                exception is JsonException
                    or InvalidDataException
                    or ArgumentException
                    or NotSupportedException)
            {
                RecoveredInvalidSettingsOnLastLoad = true;
                BackupCorruptedFile();
                var defaults = PerformanceSettings.Default;
                SaveCore(defaults);
                return defaults;
            }
        }
    }

    public void Save(PerformanceSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();

        lock (_sync)
        {
            SaveCore(settings);
        }
    }

    private void SaveCore(PerformanceSettings settings)
    {
        var directory = Path.GetDirectoryName(_settingsPath)
            ?? throw new InvalidOperationException("设置文件必须位于目录中。");
        Directory.CreateDirectory(directory);

        var temporaryPath = _settingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var serialized = JsonSerializer.SerializeToUtf8Bytes(settings, SerializerOptions);
            using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.WriteThrough))
            {
                stream.Write(serialized);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(_settingsPath))
            {
                File.Replace(temporaryPath, _settingsPath, destinationBackupFileName: null);
            }
            else
            {
                File.Move(temporaryPath, _settingsPath);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private void BackupCorruptedFile()
    {
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff", System.Globalization.CultureInfo.InvariantCulture);
        var backupPath = _settingsPath + ".corrupt-" + timestamp;
        var suffix = 0;
        while (File.Exists(backupPath))
        {
            backupPath = _settingsPath + ".corrupt-" + timestamp + "-" + ++suffix;
        }

        File.Move(_settingsPath, backupPath);
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        return options;
    }
}
