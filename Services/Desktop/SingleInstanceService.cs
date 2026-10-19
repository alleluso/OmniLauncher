using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OmniLauncher.Services.Desktop;

/// <summary>Keeps ownership on the startup thread and forwards subsequent launches over a user-only pipe.</summary>
internal sealed class SingleInstanceService : IDisposable
{
    private readonly Mutex _mutex;
    private readonly string _name;
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _listener;
    public bool IsPrimary { get; }

    public SingleInstanceService(string applicationName = "OmniLauncher")
    {
        // Separate users and interactive sessions must not activate each other's windows.
        var identity = $"{Environment.UserDomainName}\\{Environment.UserName}:{Process.GetCurrentProcess().SessionId}";
        _name = applicationName + "-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        _mutex = new Mutex(false, _name);
        try { IsPrimary = _mutex.WaitOne(0); }
        catch (AbandonedMutexException) { IsPrimary = true; }
    }

    public async Task ActivateExistingAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var pipe = new NamedPipeClientStream(".", _name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(timeout.Token);
        var processId = new byte[sizeof(int)];
        await pipe.ReadExactlyAsync(processId, timeout.Token);
        if (OperatingSystem.IsWindows())
            AllowSetForegroundWindow(BitConverter.ToInt32(processId));
        await pipe.WriteAsync(new byte[] { 1 }, timeout.Token);
        // Wait until the running instance has received the activation request.
        await pipe.ReadExactlyAsync(new byte[1], timeout.Token);
    }

    public void Listen(Action activate)
    {
        _listener = ListenAsync(activate);
    }

    private async Task ListenAsync(Action activate)
    {
        while (!_shutdown.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(_name, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_shutdown.Token).ConfigureAwait(false);
                using var request = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
                request.CancelAfter(TimeSpan.FromSeconds(5));
                await pipe.WriteAsync(BitConverter.GetBytes(Environment.ProcessId), request.Token).ConfigureAwait(false);
                var message = new byte[1];
                await pipe.ReadExactlyAsync(message, request.Token).ConfigureAwait(false);
                if (message[0] == 1) activate();
                await pipe.WriteAsync(new byte[] { 1 }, request.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (IOException ex) { Debug.WriteLine(ex); }
        }
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        _listener?.GetAwaiter().GetResult();
        _shutdown.Dispose();
        if (IsPrimary) _mutex.ReleaseMutex();
        _mutex.Dispose();
    }

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);
}
