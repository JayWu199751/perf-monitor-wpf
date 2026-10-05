using System.Diagnostics;
using System.Globalization;
using System.Management;
using PerfMonitor.Core.Metrics;

namespace PerfMonitor.Windows.Metrics;

public sealed class WindowsSlowMetricsSource : ISlowMetricsSource
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan WmiTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan WmiCancellationWait = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ProcessReapTimeout = TimeSpan.FromSeconds(2);
    private readonly SemaphoreSlim _wmiQueryGate = new(1, 1);

    public Task<GpuMetricsReading?> ReadGpuMetricsAsync(CancellationToken cancellationToken) =>
        ReadGpuMetricsCoreAsync(cancellationToken);

    public Task<int?> ReadCpuTemperatureCelsiusAsync(CancellationToken cancellationToken) =>
        ReadAcpiTemperatureCoreAsync(cancellationToken);

    private static async Task<GpuMetricsReading?> ReadGpuMetricsCoreAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CommandTimeout);

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "nvidia-smi",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        process.StartInfo.ArgumentList.Add("--query-gpu=utilization.gpu,memory.used,memory.total,temperature.gpu");
        process.StartInfo.ArgumentList.Add("--format=csv,noheader,nounits");

        Task<string>? standardOutputTask = null;
        Task<string>? standardErrorTask = null;
        try
        {
            if (!process.Start())
            {
                return null;
            }

            using var processCancellation = timeout.Token.Register(
                static state => TryKill((Process)state!),
                process);
            standardOutputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            standardErrorTask = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            await Task.WhenAll(standardOutputTask, standardErrorTask).ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                return null;
            }

            return TryParseGpuMetrics(standardOutputTask.Result);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            await KillAndReapAsync(process).ConfigureAwait(false);
            await ObserveOutputAsync(standardOutputTask, standardErrorTask).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            return null;
        }
        catch (Exception)
        {
            await KillAndReapAsync(process).ConfigureAwait(false);
            await ObserveOutputAsync(standardOutputTask, standardErrorTask).ConfigureAwait(false);
            return null;
        }
        finally
        {
            await KillAndReapAsync(process).ConfigureAwait(false);
        }
    }

    private async Task<int?> ReadAcpiTemperatureCoreAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(WmiTimeout);
        bool acquired;
        try
        {
            acquired = await _wmiQueryGate.WaitAsync(WmiTimeout, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            return null;
        }

        if (!acquired)
        {
            return null;
        }

        var temperatures = new List<int>();
        var completed = new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var ownsGate = 1;
        var operationStarted = false;
        var cancellationWaitAttempted = false;

        void ReleaseGate()
        {
            if (Interlocked.Exchange(ref ownsGate, 0) == 1)
            {
                _wmiQueryGate.Release();
            }
        }

        var observer = new ManagementOperationObserver();
        observer.ObjectReady += (_, eventArgs) =>
        {
            try
            {
                using var instance = eventArgs.NewObject;
                if (instance is not null && TryReadAcpiTemperature(instance["CurrentTemperature"], out var temperature))
                {
                    lock (temperatures)
                    {
                        temperatures.Add(temperature);
                    }
                }
            }
            catch (Exception)
            {
            }
        };
        observer.Completed += (_, eventArgs) =>
        {
            try
            {
                if (eventArgs.Status != ManagementStatus.NoError)
                {
                    completed.TrySetResult(null);
                    return;
                }

                lock (temperatures)
                {
                    completed.TrySetResult(temperatures.Count == 0 ? null : temperatures.Max());
                }
            }
            finally
            {
                ReleaseGate();
            }
        };

        try
        {
            using var searcher = new ManagementObjectSearcher(
                new ManagementScope(@"\\.\root\WMI"),
                new SelectQuery("SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature"));
            searcher.Get(observer);
            operationStarted = true;

            using var registration = timeout.Token.Register(static state => CancelWmiObserver((ManagementOperationObserver)state!), observer);
            try
            {
                return await completed.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                CancelWmiObserver(observer);
                cancellationWaitAttempted = true;
                await WaitForWmiCompletionAsync(completed.Task).ConfigureAwait(false);
                if (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException(cancellationToken);
                }

                return null;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            CancelWmiObserver(observer);
            if (!cancellationWaitAttempted)
            {
                await WaitForWmiCompletionAsync(completed.Task).ConfigureAwait(false);
            }

            throw;
        }
        catch (Exception)
        {
            CancelWmiObserver(observer);
            if (operationStarted && !cancellationWaitAttempted)
            {
                cancellationWaitAttempted = true;
                await WaitForWmiCompletionAsync(completed.Task).ConfigureAwait(false);
            }

            if (!operationStarted)
            {
                ReleaseGate();
            }

            return null;
        }
    }

    private static GpuMetricsReading? TryParseGpuMetrics(string output)
    {
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var fields = line.Split(',');
            if (fields.Length != 4 ||
                !TryParseDecimal(fields[0], out var gpuUtilization) ||
                !TryParseDecimal(fields[1], out var memoryUsed) ||
                !TryParseDecimal(fields[2], out var memoryTotal) ||
                !TryParseDecimal(fields[3], out var temperature) ||
                gpuUtilization is < 0 or > 100 ||
                memoryUsed < 0 || memoryTotal <= 0 || memoryUsed > memoryTotal ||
                temperature < 0 || temperature > int.MaxValue)
            {
                continue;
            }

            var gpuPercentage = decimal.Round(gpuUtilization, 0, MidpointRounding.AwayFromZero);
            var memoryPercentage = decimal.Round(memoryUsed / memoryTotal * 100m, 0, MidpointRounding.AwayFromZero);
            var temperatureCelsius = decimal.Round(temperature, 0, MidpointRounding.AwayFromZero);
            return new GpuMetricsReading((int)gpuPercentage, (int)memoryPercentage, (int)temperatureCelsius);
        }

        return null;
    }

    private static bool TryParseDecimal(string value, out decimal parsed) =>
        decimal.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed);

    private static bool TryReadAcpiTemperature(object? rawValue, out int temperatureCelsius)
    {
        temperatureCelsius = 0;
        if (rawValue is null)
        {
            return false;
        }

        decimal rawTemperature;
        try
        {
            rawTemperature = Convert.ToDecimal(rawValue, CultureInfo.InvariantCulture);
        }
        catch (Exception)
        {
            return false;
        }

        var celsius = rawTemperature / 10m - 273.15m;
        if (celsius <= 0 || celsius >= 120)
        {
            return false;
        }

        temperatureCelsius = (int)decimal.Round(celsius, 0, MidpointRounding.AwayFromZero);
        return true;
    }

    private static void CancelWmiObserver(ManagementOperationObserver observer)
    {
        try
        {
            observer.Cancel();
        }
        catch (Exception)
        {
        }
    }

    private static async Task WaitForWmiCompletionAsync(Task<int?> completion)
    {
        try
        {
            await completion.WaitAsync(WmiCancellationWait).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
        }
    }

    private static async Task KillAndReapAsync(Process process)
    {
        TryKill(process);

        try
        {
            await process.WaitForExitAsync()
                .WaitAsync(ProcessReapTimeout)
                .ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception)
        {
        }
    }

    private static async Task ObserveOutputAsync(params Task<string>?[] outputTasks)
    {
        foreach (var outputTask in outputTasks)
        {
            if (outputTask is null)
            {
                continue;
            }

            try
            {
                _ = await outputTask.ConfigureAwait(false);
            }
            catch (Exception)
            {
            }
        }
    }
}
