using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Tlp.Host.Services;

/// <summary>
/// Fetches a web page the host pasted (e.g. a torrent site's page) so magnets can be
/// pulled out of it. Guarded so the host app can't be used to reach private addresses
/// (localhost, LAN, cloud metadata), which matters once the app is public.
/// </summary>
public sealed class PageFetcher
{
    public const int MaxBytes = 2 * 1024 * 1024;
    private readonly HttpClient _http;

    public PageFetcher() : this(allowPrivateAddresses: false) { }

    public PageFetcher(bool allowPrivateAddresses)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            ConnectCallback = async (ctx, ct) =>
            {
                var addresses = await Dns.GetHostAddressesAsync(ctx.DnsEndPoint.Host, ct);
                var allowed = addresses.Where(a => allowPrivateAddresses || IsPublic(a)).ToArray();
                if (allowed.Length == 0)
                    throw new HttpRequestException($"Refusing to fetch {ctx.DnsEndPoint.Host}: not a public address.");
                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    await socket.ConnectAsync(allowed, ctx.DnsEndPoint.Port, ct);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            },
        };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("the-last-piece/0.1 (+magnet finder)");
    }

    public async Task<(string Html, Uri FinalUri)> FetchAsync(Uri uri, CancellationToken ct)
    {
        if (uri.Scheme is not ("http" or "https"))
            throw new ArgumentException("Only http and https links are supported.");
        using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var buffer = new byte[MaxBytes];
        var total = 0;
        int read;
        while (total < MaxBytes && (read = await stream.ReadAsync(buffer.AsMemory(total, MaxBytes - total), ct)) > 0)
            total += read;
        var html = Encoding.UTF8.GetString(buffer, 0, total);
        return (html, response.RequestMessage?.RequestUri ?? uri);
    }

    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address)) return false;
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return !(b[0] == 10
                || b[0] == 0
                || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)   // carrier-grade NAT
                || (b[0] == 169 && b[1] == 254)                  // link-local, cloud metadata
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                || (b[0] == 192 && b[1] == 168)
                || b[0] >= 224);                                  // multicast, reserved
        }
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast) return false;
            var b = address.GetAddressBytes();
            if ((b[0] & 0xfe) == 0xfc) return false;                       // unique local fc00::/7
            if (address.Equals(IPAddress.IPv6None) || address.Equals(IPAddress.IPv6Any)) return false;
            return true;
        }
        return false;
    }
}
