using System.Net;
using System.Net.Sockets;
using Tlp.Viewer.Core;

namespace Tlp.Tests;

public class NetCheckTests
{
    private static readonly Uri Link = new("http://movies.example.com:8080/n/abc");

    private static async Task<Exception> FailureFrom(string url)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        return await Assert.ThrowsAnyAsync<Exception>(() => http.GetStringAsync(url));
    }

    [Fact]
    public async Task Refused_connection_says_the_app_or_port_is_the_problem()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop(); // now nothing listens there
        var text = NetCheck.ExplainLinkFailure(await FailureFrom($"http://127.0.0.1:{port}/n/x"), new Uri($"http://127.0.0.1:{port}/n/x"));
        Assert.Contains("turned the connection away", text);
    }

    [Fact]
    public async Task Unknown_host_says_check_the_link()
    {
        var text = NetCheck.ExplainLinkFailure(await FailureFrom("http://no-such-host.invalid/n/x"), new Uri("http://no-such-host.invalid/n/x"));
        Assert.Contains("Couldn't find no-such-host.invalid", text);
    }

    [Fact]
    public void Old_link_and_timeouts_get_their_own_words()
    {
        Assert.Contains("doesn't work any more", NetCheck.ExplainLinkFailure(new HttpRequestException("nf", null, HttpStatusCode.NotFound), Link));
        Assert.Contains("movies.example.com:8080 didn't answer", NetCheck.ExplainLinkFailure(new TaskCanceledException(), Link));
        Assert.Contains("(503)", NetCheck.ExplainLinkFailure(new HttpRequestException("x", null, HttpStatusCode.ServiceUnavailable), Link));
    }

    [Fact]
    public async Task Port_check_sees_a_listener_and_its_absence()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        Assert.True(await NetCheck.PortAnswersAsync("127.0.0.1", port, TimeSpan.FromSeconds(3)));
        listener.Stop();
        Assert.False(await NetCheck.PortAnswersAsync("127.0.0.1", port, TimeSpan.FromSeconds(3)));
    }
}
