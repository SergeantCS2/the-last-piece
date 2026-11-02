using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Authentication;

namespace Tlp.Viewer.Core;

/// <summary>Turns network failures into sentences a friend can act on, and checks a port answers.</summary>
public static class NetCheck
{
    /// <summary>Why opening a movie night link failed, in plain words.</summary>
    public static string ExplainLinkFailure(Exception ex, Uri link)
    {
        var host = link.IsDefaultPort ? link.Host : $"{link.Host}:{link.Port}";
        if (ex is InvalidDataException) return ex.Message.Length > 0 ? ex.Message : "That link answered, but not with a movie night.";
        if (ex is System.Text.Json.JsonException) return $"{host} answered, but not with a movie night. Check the whole link was copied.";
        if (ex is TaskCanceledException or TimeoutException)
            return $"{host} didn't answer. The host's computer may be off, or the port isn't open on their router. Ask the host to check their setup page.";
        if (ex is HttpRequestException http)
        {
            if (http.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
                return "This movie night link doesn't work any more. Ask the host for the new one.";
            if (http.StatusCode is { } code)
                return $"{host} answered with an error ({(int)code}). Try again in a minute, or ask the host.";
            if (Find<AuthenticationException>(ex) is not null)
                return $"The secure connection to {host} failed (a certificate problem on the host's side). Ask the host to check their HTTPS setup.";
            if (Find<SocketException>(ex) is { } socket)
            {
                return socket.SocketErrorCode switch
                {
                    SocketError.HostNotFound or SocketError.NoData or SocketError.TryAgain =>
                        $"Couldn't find {link.Host}. Check the link was copied whole, or that you're online.",
                    SocketError.ConnectionRefused =>
                        $"{host} turned the connection away. The host app may not be running, or the port isn't forwarded.",
                    SocketError.TimedOut or SocketError.HostUnreachable or SocketError.NetworkUnreachable =>
                        $"{host} didn't answer. The host's computer may be off, or the port isn't open on their router.",
                    _ => $"Couldn't reach {host} ({socket.SocketErrorCode}).",
                };
            }
            return $"Couldn't reach {host}: {http.Message}";
        }
        return ex.Message;
    }

    /// <summary>True if something accepts a TCP connection at host:port within the timeout.</summary>
    public static async Task<bool> PortAnswersAsync(string host, int port, TimeSpan timeout, CancellationToken ct = default)
    {
        using var tcp = new TcpClient();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            await tcp.ConnectAsync(host, port, cts.Token);
            return true;
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException)
        {
            return false;
        }
    }

    private static T? Find<T>(Exception? ex) where T : Exception
    {
        for (; ex is not null; ex = ex.InnerException)
            if (ex is T match) return match;
        return null;
    }
}
