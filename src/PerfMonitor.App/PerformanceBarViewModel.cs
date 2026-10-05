using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using PerfMonitor.Core.Metrics;

namespace PerfMonitor.App;

public sealed class PerformanceBarViewModel : INotifyPropertyChanged
{
    private long _metricGeneration;
    private string _cpuPercentageText = "--%";
    private string _memoryPercentageText = "--%";
    private string _gpuPercentageText = "--%";
    private string _gpuMemoryPercentageText = "--%";
    private string _gpuTemperatureText = "--°";
    private string _cpuTemperatureText = "--°";
    private string _networkDownloadSpeedText = "--";
    private string _networkUploadSpeedText = "--";

    public event PropertyChangedEventHandler? PropertyChanged;

    public string CpuPercentageText
    {
        get => _cpuPercentageText;
        private set => SetField(ref _cpuPercentageText, value);
    }

    public string MemoryPercentageText
    {
        get => _memoryPercentageText;
        private set => SetField(ref _memoryPercentageText, value);
    }

    public string GpuPercentageText
    {
        get => _gpuPercentageText;
        private set => SetField(ref _gpuPercentageText, value);
    }

    public string GpuMemoryPercentageText
    {
        get => _gpuMemoryPercentageText;
        private set => SetField(ref _gpuMemoryPercentageText, value);
    }

    public string GpuTemperatureText
    {
        get => _gpuTemperatureText;
        private set => SetField(ref _gpuTemperatureText, value);
    }

    public string CpuTemperatureText
    {
        get => _cpuTemperatureText;
        private set => SetField(ref _cpuTemperatureText, value);
    }

    public string NetworkDownloadSpeedText
    {
        get => _networkDownloadSpeedText;
        private set => SetField(ref _networkDownloadSpeedText, value);
    }

    public string NetworkUploadSpeedText
    {
        get => _networkUploadSpeedText;
        private set => SetField(ref _networkUploadSpeedText, value);
    }

    internal void SetMetricGeneration(long generation) => _metricGeneration = generation;

    internal void Apply(PerformanceMetricsSnapshot snapshot)
    {
        if (snapshot.Generation != _metricGeneration)
        {
            return;
        }

        CpuPercentageText = FormatPercentage(snapshot.CpuPercentage);
        MemoryPercentageText = FormatPercentage(snapshot.MemoryPercentage);
        GpuPercentageText = FormatPercentage(snapshot.GpuPercentage);
        GpuMemoryPercentageText = FormatPercentage(snapshot.GpuMemoryPercentage);
        GpuTemperatureText = FormatTemperature(snapshot.GpuTemperatureCelsius);
        CpuTemperatureText = FormatTemperature(snapshot.CpuTemperatureCelsius);
        NetworkDownloadSpeedText = FormatNetworkSpeed(snapshot.NetworkDownloadMegabytesPerSecond);
        NetworkUploadSpeedText = FormatNetworkSpeed(snapshot.NetworkUploadMegabytesPerSecond);
    }

    private static string FormatPercentage(int? value) =>
        value is { } percentage
            ? percentage.ToString(CultureInfo.InvariantCulture) + "%"
            : "--%";

    private static string FormatTemperature(int? value) =>
        value is { } temperature
            ? temperature.ToString(CultureInfo.InvariantCulture) + "°"
            : "--°";

    private static string FormatNetworkSpeed(double? value) =>
        value is { } speed
            ? speed.ToString("0.0", CultureInfo.InvariantCulture)
            : "--";

    private void SetField(ref string field, string value, [CallerMemberName] string? propertyName = null)
    {
        if (field == value)
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
