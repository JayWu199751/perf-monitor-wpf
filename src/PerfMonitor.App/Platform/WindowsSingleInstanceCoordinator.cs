using System.ComponentModel;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace PerfMonitor.App.Platform;

internal sealed class WindowsSingleInstanceCoordinator : IDisposable
{
    private const int ErrorAlreadyExists = 183;
    private const int PipeCommandTimeoutMilliseconds = 5_000;
    private const int PipeRequestTimeoutSeconds = 2;
    private const int ElevationHandoffTokenLifetimeMilliseconds = 5 * 60_000;
    private const uint MutexWaitMilliseconds = 30_000;
    private const uint WaitObject0 = 0;
    private const uint WaitAbandoned = 0x00000080;
    private const uint WaitTimeout = 0x00000102;
    private const uint PipeAccessDuplex = 0x00000003;
    private const uint FileFlagOverlapped = 0x40000000;
    private const uint PipeTypeByte = 0x00000000;
    private const uint PipeReadModeByte = 0x00000000;
    private const uint PipeWait = 0x00000000;
    private const uint PipeRejectRemoteClients = 0x00000008;
    private const uint PipeUnlimitedInstances = 255;
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint OpenExisting = 3;
    private const uint SecurityDescriptorRevision = 1;

    private readonly SafeWaitHandle _mutexHandle;
    private readonly NativeSecurityDescriptor _securityDescriptor;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _stopListening = new();
    private Thread? _listenerThread;
    private Action? _activateExistingInstance;
    private Action? _elevatedTakeoverRequested;
    private ElevationHandoffRegistration? _expectedElevationHandoff;
    private bool _ownsMutex;
    private int _disposed;

    private WindowsSingleInstanceCoordinator(
        SafeWaitHandle mutexHandle,
        NativeSecurityDescriptor securityDescriptor,
        string pipeName,
        bool ownsMutex)
    {
        _mutexHandle = mutexHandle;
        _securityDescriptor = securityDescriptor;
        _pipeName = pipeName;
        _ownsMutex = ownsMutex;
    }

    public bool IsPrimaryInstance => _ownsMutex;

    public static WindowsSingleInstanceCoordinator Acquire()
    {
        if (!ProcessIdToSessionId((uint)Environment.ProcessId, out var sessionId))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        var securityDescriptor = NativeSecurityDescriptor.CreateForInteractiveSession();
        var securityAttributes = new SecurityAttributes
        {
            Length = Marshal.SizeOf<SecurityAttributes>(),
            SecurityDescriptor = securityDescriptor.Pointer,
            InheritHandle = false
        };

        var mutexName = $"Local\\PerfMonitorWpf.SingleInstance.{sessionId}";
        var mutexValue = CreateMutexW(ref securityAttributes, initialOwner: true, mutexName);
        if (mutexValue == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            securityDescriptor.Dispose();
            throw new Win32Exception(error);
        }

        var lastError = Marshal.GetLastWin32Error();
        var mutexHandle = new SafeWaitHandle(mutexValue, ownsHandle: true);
        var pipeName = $"\\\\.\\pipe\\PerfMonitorWpf.SingleInstance.{sessionId}";
        return new WindowsSingleInstanceCoordinator(
            mutexHandle,
            securityDescriptor,
            pipeName,
            ownsMutex: lastError != ErrorAlreadyExists);
    }

    public void ExpectElevationHandoff(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        if (!_ownsMutex)
        {
            throw new InvalidOperationException("只有当前实例持有者可以登记提权交接。");
        }

        var registration = new ElevationHandoffRegistration(
            token,
            Environment.TickCount64 + ElevationHandoffTokenLifetimeMilliseconds);
        if (Interlocked.CompareExchange(ref _expectedElevationHandoff, registration, null) is not null)
        {
            throw new InvalidOperationException("当前实例已经登记了提权交接令牌。");
        }
    }

    public void ClearExpectedElevationHandoff(string token)
    {
        var registration = Volatile.Read(ref _expectedElevationHandoff);
        if (registration is not null && string.Equals(registration.Token, token, StringComparison.Ordinal))
        {
            Interlocked.CompareExchange(ref _expectedElevationHandoff, null, registration);
        }
    }

    public void StartListening(Action activateExistingInstance, Action elevatedTakeoverRequested)
    {
        ArgumentNullException.ThrowIfNull(activateExistingInstance);
        ArgumentNullException.ThrowIfNull(elevatedTakeoverRequested);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (!_ownsMutex)
        {
            throw new InvalidOperationException("只有当前实例持有者可以启动通知服务。");
        }

        if (_listenerThread is not null)
        {
            return;
        }

        _activateExistingInstance = activateExistingInstance;
        _elevatedTakeoverRequested = elevatedTakeoverRequested;
        var firstPipe = CreateServerPipe();
        _listenerThread = new Thread(() => Listen(firstPipe))
        {
            IsBackground = true,
            Name = "PerfMonitor single-instance IPC"
        };

        try
        {
            _listenerThread.Start();
        }
        catch
        {
            firstPipe.Dispose();
            _listenerThread = null;
            throw;
        }
    }

    public bool NotifyExistingInstance()
    {
        return SendCommand("SHOW") == "OK";
    }

    public bool TryTakeOverAfterElevation(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        if (_ownsMutex)
        {
            return true;
        }

        if (SendCommand($"TAKEOVER|{token}") != "OK")
        {
            return false;
        }

        var waitResult = WaitForSingleObject(_mutexHandle, MutexWaitMilliseconds);
        if (waitResult is WaitObject0 or WaitAbandoned)
        {
            _ownsMutex = true;
            return true;
        }

        System.Diagnostics.Trace.WriteLine(waitResult == WaitTimeout
            ? "等待普通权限实例释放单实例令牌超时。"
            : $"等待单实例令牌失败，错误码 {Marshal.GetLastWin32Error()}。");
        return false;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _stopListening.Cancel();
        var listenerStopped = _listenerThread?.Join(millisecondsTimeout: PipeRequestTimeoutSeconds * 1000 + 1000) ?? true;
        _listenerThread = null;
        if (!listenerStopped)
        {
            System.Diagnostics.Trace.WriteLine("单实例 IPC 监听线程未在有界等待内退出；监听线程退出后会释放安全描述符。");
        }

        if (_ownsMutex)
        {
            if (!ReleaseMutex(_mutexHandle))
            {
                System.Diagnostics.Trace.WriteLine($"释放单实例互斥体失败，错误码 {Marshal.GetLastWin32Error()}。");
            }

            _ownsMutex = false;
        }

        _mutexHandle.Dispose();
        if (listenerStopped)
        {
            _securityDescriptor.Dispose();
        }

        if (listenerStopped)
        {
            _stopListening.Dispose();
        }
    }

    private void Listen(NamedPipeServerStream pendingPipe)
    {
        NamedPipeServerStream? pipe = pendingPipe;
        try
        {
            while (Volatile.Read(ref _disposed) == 0)
            {
                try
                {
                    pipe ??= CreateServerPipe();
                    pipe.WaitForConnectionAsync(_stopListening.Token).GetAwaiter().GetResult();
                    var connectedPipe = pipe;
                    pipe = null;
                    HandleClient(connectedPipe);
                }
                catch (OperationCanceledException) when (Volatile.Read(ref _disposed) != 0)
                {
                    pipe?.Dispose();
                    pipe = null;
                }
                catch (OperationCanceledException)
                {
                    pipe?.Dispose();
                    pipe = null;
                }
                catch (Exception exception) when (exception is IOException or ObjectDisposedException or Win32Exception or InvalidOperationException)
                {
                    pipe?.Dispose();
                    pipe = null;
                    if (Volatile.Read(ref _disposed) == 0)
                    {
                        System.Diagnostics.Trace.WriteLine($"单实例 IPC 本轮失败：{exception.Message}");
                        Thread.Sleep(50);
                    }
                }
            }
        }
        finally
        {
            pipe?.Dispose();
            if (Volatile.Read(ref _disposed) != 0)
            {
                _securityDescriptor.Dispose();
            }
        }
    }

    private void HandleClient(NamedPipeServerStream pipe)
    {
        using (pipe)
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stopListening.Token))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(PipeRequestTimeoutSeconds));
            var request = ReadLineAsync(pipe, timeout.Token).GetAwaiter().GetResult();
            if (request is null)
            {
                return;
            }

            var response = HandleRequest(request, out var callback);
            if (response is not null)
            {
                WriteLineAsync(pipe, response, timeout.Token).GetAwaiter().GetResult();
            }

            callback?.Invoke();
        }
    }

    private string? HandleRequest(string? request, out Action? callback)
    {
        callback = null;
        if (request == "SHOW")
        {
            callback = _activateExistingInstance;
            return "OK";
        }

        const string handoffPrefix = "TAKEOVER|";
        if (request?.StartsWith(handoffPrefix, StringComparison.Ordinal) == true)
        {
            var providedToken = request[handoffPrefix.Length..];
            var registration = Volatile.Read(ref _expectedElevationHandoff);
            if (registration is not null
                && Environment.TickCount64 <= registration.ExpiresAt
                && string.Equals(providedToken, registration.Token, StringComparison.Ordinal)
                && ReferenceEquals(Interlocked.CompareExchange(ref _expectedElevationHandoff, null, registration), registration))
            {
                callback = _elevatedTakeoverRequested;
                return "OK";
            }

            if (registration is not null && Environment.TickCount64 > registration.ExpiresAt)
            {
                Interlocked.CompareExchange(ref _expectedElevationHandoff, null, registration);
            }
        }

        return "REJECT";
    }

    private string? SendCommand(string command)
    {
        using var timeout = new CancellationTokenSource(PipeCommandTimeoutMilliseconds);
        while (!timeout.IsCancellationRequested && Volatile.Read(ref _disposed) == 0)
        {
            using var client = CreatePipeClient();
            if (client is not null)
            {
                try
                {
                    return SendCommandAsync(client, command, timeout.Token).GetAwaiter().GetResult();
                }
                catch (OperationCanceledException) when (timeout.IsCancellationRequested)
                {
                    return null;
                }
                catch (IOException exception)
                {
                    System.Diagnostics.Trace.WriteLine($"单实例 IPC 请求失败：{exception.Message}");
                }
            }

            Thread.Sleep(50);
        }

        return null;
    }

    private NamedPipeClientStream? CreatePipeClient()
    {
        _ = WaitNamedPipeW(_pipeName, 100);
        var handleValue = CreateFileW(
            _pipeName,
            GenericRead | GenericWrite,
            shareMode: 0,
            securityAttributes: IntPtr.Zero,
            creationDisposition: OpenExisting,
            flagsAndAttributes: FileFlagOverlapped,
            templateFile: IntPtr.Zero);

        var handle = new SafePipeHandle(handleValue, ownsHandle: true);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            return null;
        }

        try
        {
            return new NamedPipeClientStream(PipeDirection.InOut, isAsync: true, isConnected: true, handle);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    private static async Task<string?> SendCommandAsync(
        NamedPipeClientStream pipe,
        string command,
        CancellationToken cancellationToken)
    {
        await WriteLineAsync(pipe, command, cancellationToken).ConfigureAwait(false);
        return await ReadLineAsync(pipe, cancellationToken).ConfigureAwait(false);
    }

    private NamedPipeServerStream CreateServerPipe()
    {
        var securityAttributes = new SecurityAttributes
        {
            Length = Marshal.SizeOf<SecurityAttributes>(),
            SecurityDescriptor = _securityDescriptor.Pointer,
            InheritHandle = false
        };
        var handleValue = CreateNamedPipeW(
            _pipeName,
            PipeAccessDuplex | FileFlagOverlapped,
            PipeTypeByte | PipeReadModeByte | PipeWait | PipeRejectRemoteClients,
            PipeUnlimitedInstances,
            outBufferSize: 512,
            inBufferSize: 512,
            defaultTimeout: 0,
            ref securityAttributes);

        var pipeHandle = new SafePipeHandle(handleValue, ownsHandle: true);
        if (pipeHandle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            pipeHandle.Dispose();
            throw new Win32Exception(error);
        }

        try
        {
            return new NamedPipeServerStream(PipeDirection.InOut, isAsync: true, isConnected: false, pipeHandle);
        }
        catch
        {
            pipeHandle.Dispose();
            throw;
        }
    }

    private static async Task<string?> ReadLineAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[256];
        var length = 0;
        while (length < buffer.Length)
        {
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(length, 1), cancellationToken).ConfigureAwait(false);
            if (bytesRead == 0 || buffer[length] == '\n')
            {
                break;
            }

            if (buffer[length] != '\r')
            {
                length++;
            }
        }

        return length == 0 ? null : Encoding.ASCII.GetString(buffer, 0, length);
    }

    private static async Task WriteLineAsync(Stream stream, string value, CancellationToken cancellationToken)
    {
        var bytes = Encoding.ASCII.GetBytes(value + "\n");
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public int Length;
        public IntPtr SecurityDescriptor;
        [MarshalAs(UnmanagedType.Bool)]
        public bool InheritHandle;
    }

    private sealed record ElevationHandoffRegistration(string Token, long ExpiresAt);

    private sealed class NativeSecurityDescriptor : IDisposable
    {
        private IntPtr _pointer;

        private NativeSecurityDescriptor(IntPtr pointer)
        {
            _pointer = pointer;
        }

        public IntPtr Pointer => _pointer;

        public static NativeSecurityDescriptor CreateForInteractiveSession()
        {
            // 仅交互式用户 SID 可访问，Low 标签允许 Medium/High 完成双向通知与交接。
            // NW 表示禁止低完整性进程写入；对象名称还限定在当前会话。
            var sddl = "D:(A;;GA;;;IU)S:(ML;;NW;;;LW)";
            if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(
                    sddl,
                    SecurityDescriptorRevision,
                    out var descriptor,
                    out _))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            return new NativeSecurityDescriptor(descriptor);
        }

        public void Dispose()
        {
            var pointer = Interlocked.Exchange(ref _pointer, IntPtr.Zero);
            if (pointer != IntPtr.Zero)
            {
                _ = LocalFree(pointer);
            }
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateMutexW(
        ref SecurityAttributes mutexAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool initialOwner,
        string name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReleaseMutex(SafeWaitHandle mutexHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(SafeWaitHandle handle, uint milliseconds);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateNamedPipeW(
        string name,
        uint openMode,
        uint pipeMode,
        uint maxInstances,
        uint outBufferSize,
        uint inBufferSize,
        uint defaultTimeout,
        ref SecurityAttributes securityAttributes);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WaitNamedPipeW(string name, uint timeoutMilliseconds);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFileW(
        string name,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ProcessIdToSessionId(uint processId, out uint sessionId);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptorW(
        string stringSecurityDescriptor,
        uint stringSdRevision,
        out IntPtr securityDescriptor,
        out uint securityDescriptorSize);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
