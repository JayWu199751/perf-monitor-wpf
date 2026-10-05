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

    internal void SetMetricGeneration(long generation) => _metricGeneration = generation;

    internal void Apply(PerformanceMetricsSnapshot snapshot)
    {
        if (snapshot.Generation != _metricGeneration)
        {
            return;
        }

        CpuPercentageText = FormatPercentage(snapshot.CpuPercentage);
        MemoryPercentageText = FormatPercentage(snapshot.MemoryPercentage);
    }

    private static string FormatPercentage(int? value) =>
        value is { } percentage
            ? percentage.ToString(CultureInfo.InvariantCulture) + "%"
            : "--%";

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
