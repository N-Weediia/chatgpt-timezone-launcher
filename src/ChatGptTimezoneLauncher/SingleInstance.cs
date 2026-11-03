using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace ChatGptTimezoneLauncher;

internal sealed class SingleInstance : IDisposable
{
    private readonly NamedPipeServerStream _pipe;
    private readonly CancellationTokenSource _stop = new();
    private Task? _listener;

    private SingleInstance(NamedPipeServerStream pipe) => _pipe = pipe;

    public static SingleInstance? StartOrActivate(string? testDirectory = null)
    {
        using var identity = WindowsIdentity.GetCurrent();
        using var process = Process.GetCurrentProcess();
        var pipeName = $"ChatGPTTimezoneLauncher-{identity.User!.Value}-{process.SessionId}";
        if (testDirectory is not null)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(testDirectory.ToUpperInvariant()));
            pipeName += "-test-" + Convert.ToHexString(hash.AsSpan(0, 8));
        }

        try
        {
            // A single server instance is both the lock and the activation channel.
            var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            return new SingleInstance(pipe);
        }
        catch (IOException)
        {
            ActivateExistingAsync(pipeName).GetAwaiter().GetResult();
            return null;
        }
    }

    public void Listen(MainForm form)
    {
        // Create the handle before accepting requests, even if another launch arrives during startup.
        _ = form.Handle;
        var context = SynchronizationContext.Current as WindowsFormsSynchronizationContext
            ?? throw new InvalidOperationException("无法初始化启动器窗口消息循环。");
        _listener = ListenAsync(form, context, _stop.Token);
    }

    private async Task ListenAsync(MainForm form, SynchronizationContext context, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await _pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                await _pipe.WriteAsync(BitConverter.GetBytes(Environment.ProcessId), cancellationToken).ConfigureAwait(false);
                var message = new byte[1];
                await _pipe.ReadExactlyAsync(message, cancellationToken).ConfigureAwait(false);
                if (message[0] == 1)
                    context.Post(_ => { if (!form.IsDisposed) form.ShowFromTray(); }, null);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (IOException) { /* A client may disconnect while the launcher is exiting. */ }
            catch (InvalidOperationException) when (form.IsDisposed || cancellationToken.IsCancellationRequested) { break; }
            catch (Exception ex) { StartupDiagnostics.Write(ex); break; }
            finally
            {
                if (_pipe.IsConnected) _pipe.Disconnect();
            }
        }
    }

    private static async Task ActivateExistingAsync(string pipeName)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await client.ConnectAsync(timeout.Token).ConfigureAwait(false);
            var processId = new byte[sizeof(int)];
            await client.ReadExactlyAsync(processId, timeout.Token).ConfigureAwait(false);
            // Explorer grants foreground rights to the new process; pass them to the existing one.
            AllowSetForegroundWindow(BitConverter.ToInt32(processId));
            await client.WriteAsync(new byte[] { 1 }, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (timeout.IsCancellationRequested)
        {
            throw new TimeoutException("已有启动器未能响应。请从系统托盘退出后重试。", ex);
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener?.GetAwaiter().GetResult();
        _pipe.Dispose();
        _stop.Dispose();
    }

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);
}
