using System.IO.Pipes;
using System.Text;

namespace Tlp.Viewer.Core;

/// <summary>
/// Keeps one viewer window per Windows user. When a second copy starts (for example the
/// browser opening another tlp:// link), it hands its link to the running copy over a
/// named pipe and exits. The pipe only accepts connections from the same user.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _stop = new();

    private SingleInstance(Mutex mutex, string pipeName)
    {
        _mutex = mutex;
        _pipeName = pipeName;
    }

    public static string DefaultName => "TheLastPiece-" + Environment.UserName;

    /// <summary>
    /// Becomes the running copy and calls <paramref name="onMessage"/> (on a background thread)
    /// with each link another copy hands over. Returns null if another copy is already running.
    /// </summary>
    public static SingleInstance? TryStart(string name, Action<string> onMessage)
    {
        var mutex = new Mutex(initiallyOwned: true, @"Local\" + name, out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            return null;
        }
        var instance = new SingleInstance(mutex, name);
        _ = Task.Run(() => instance.ListenAsync(onMessage));
        return instance;
    }

    /// <summary>Hands a link (or "" to just bring the window forward) to the running copy.</summary>
    public static bool SendToRunning(string name, string message, TimeSpan timeout)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", name, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect((int)timeout.TotalMilliseconds);
            var bytes = Encoding.UTF8.GetBytes(message.Length > 4096 ? message[..4096] : message);
            client.Write(bytes);
            client.Flush();
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private async Task ListenAsync(Action<string> onMessage)
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(_pipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(_stop.Token);
                using var reader = new StreamReader(server, Encoding.UTF8);
                var buffer = new char[4096];
                var read = await reader.ReadBlockAsync(buffer, _stop.Token);
                onMessage(new string(buffer, 0, read).Trim());
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (IOException)
            {
                // A copy that disconnected half way; keep listening.
            }
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        try { _mutex.ReleaseMutex(); } catch (ApplicationException) { }
        _mutex.Dispose();
    }
}
