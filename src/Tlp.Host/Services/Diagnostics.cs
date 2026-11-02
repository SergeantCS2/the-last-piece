using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;

namespace Tlp.Host.Services;

/// <summary>One line on the admin page's health check.</summary>
/// <param name="State">ok, warn, fail or info.</param>
/// <param name="Fix">What to do about a warn or fail, in plain words.</param>
public sealed record DiagCheck(string Area, string Name, string State, string Detail, string? Fix = null);

/// <summary>
/// The admin page's "Check setup": the things most likely to stop a movie night, each with what
/// to do about it. Reads only the host's own settings and state; the only network calls are to
/// the host's own public link and its Syncplay server.
/// </summary>
public sealed class Diagnostics(
    IOptions<TlpOptions> options,
    Shelf shelf,
    NightStore nights,
    ReadyBoard board,
    RoomBot bot,
    GroupTorrents torrents,
    DiscordAnnouncer discord,
    IHttpClientFactory httpFactory,
    TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly ConcurrentQueue<(IPAddress Address, DateTimeOffset At)> _peers = new();

    /// <summary>Remembers where a group torrent announce came from (the last 200), to spot NAT problems.</summary>
    public void RecordPeer(IPAddress address)
    {
        _peers.Enqueue((address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address, _clock.GetUtcNow()));
        while (_peers.Count > 200) _peers.TryDequeue(out _);
    }

    public async Task<IReadOnlyList<DiagCheck>> RunAsync(CancellationToken ct)
    {
        var o = options.Value;
        var checks = new List<DiagCheck>();
        var env = HostEnvironment.Detect();
        checks.Add(new("Host", "Running on", "info", env.Describe()));

        checks.Add(FolderCheck("Shelf folder", shelf.Root, needWrite: o.CleanupEnabled));
        checks.Add(FolderCheck("Data folder", Path.GetFullPath(o.DataDir), needWrite: true));
        if (shelf.Disk() is { } disk)
        {
            var freeGb = disk.Free / 1024d / 1024 / 1024;
            checks.Add(freeGb < 5
                ? new("Host", "Free space", "warn", $"{freeGb:0.0} GB free on the shelf's disk.", "Run cleanup or free some space; one episode is often 1 to 2 GB.")
                : new("Host", "Free space", "ok", $"{freeGb:0.0} GB free on the shelf's disk."));
        }

        checks.Add(PublicLinkCheck(o.PublicBaseUrl, env));
        if (env.Wsl) checks.Add(WslCheck(o.WslNetwork));
        checks.Add(await PublicLinkAnswersAsync(o.PublicBaseUrl, ct));

        var syncplayHost = o.Syncplay.PublicHost ?? (Uri.TryCreate(o.PublicBaseUrl, UriKind.Absolute, out var u) ? u.Host : "localhost");
        checks.Add(AddressCheck("Syncplay", "Address friends use", $"{syncplayHost}:{o.Syncplay.Port}", syncplayHost, env));
        checks.Add(await PortCheckAsync("Syncplay", "Server answers", o.Syncplay.BotHost, o.Syncplay.Port, ct));
        if (o.Syncplay.RoomBot)
        {
            var snap = bot.Snapshot;
            checks.Add(snap.Connected
                ? new("Syncplay", "Room helper", "ok", $"Connected{(snap.At is { } at ? $", last checked {Ago(at)}" : "")}.")
                : new("Syncplay", "Room helper", "warn", $"Not connected{(snap.Error is { } e ? $": {e}" : ".")}",
                    "Check the Syncplay server is running and Tlp__Syncplay__Password matches its password. Playlists won't fill themselves until it connects."));
        }

        checks.Add(TorrentCheck(o, env));
        checks.AddRange(NightChecks());
        checks.Add(FriendsCheck());
        checks.Add(discord.Configured
            ? new("Extras", "Discord post", "ok", "A webhook is set; the admin page can post a night's link.")
            : new("Extras", "Discord post", "info", "No webhook set, so links are shared by hand.", "Optional: set Tlp__DiscordWebhookUrl."));
        checks.Add(new("Extras", "Cleanup", o.CleanupEnabled ? "ok" : "info",
            o.CleanupEnabled ? $"Files go {o.RetentionDays} days after the last night that uses them, unless pinned." : "Cleanup only reports; nothing is deleted."));
        return checks;
    }

    private static DiagCheck FolderCheck(string name, string path, bool needWrite)
    {
        if (!Directory.Exists(path))
            return new("Host", name, "fail", $"{path} doesn't exist.", "Create it, or fix the folder mapping (Docker: the volume in docker-compose.yml).");
        if (!needWrite) return new("Host", name, "ok", path);
        try
        {
            var probe = Path.Combine(path, $".tlp-write-check-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return new("Host", name, "ok", $"{path} (writable)");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new("Host", name, "fail", $"{path} isn't writable: {ex.Message}", "Give the host app write access to this folder.");
        }
    }

    /// <summary>
    /// In WSL, other PCs reach the host only if Windows passes ports 8080 and 8999 on and its
    /// firewall lets them in. The host can't see Windows' settings from in here, so this says what
    /// the installer set up and how to confirm it from a second PC.
    /// </summary>
    public static DiagCheck WslCheck(string? mode) => mode?.Trim().ToLowerInvariant() switch
    {
        "mirrored" => new("Links", "Windows to WSL", "ok",
            "The installer set up WSL's mirrored networking and the Windows Firewall rules, so other PCs reach this host at the Windows PC's own address.",
            "To confirm, open the public link's /healthz page on a second PC, or open a night there."),
        "portproxy" => new("Links", "Windows to WSL", "ok",
            "The installer set up Windows port forwarding into WSL and the Windows Firewall rules. Every viewer will look like the Windows PC to the host, so viewers can't swap pieces with each other; downloads still work.",
            "WSL's address can change after a restart: re-run server/windows-network.ps1 (or the installer) if a second PC stops connecting."),
        _ => new("Links", "Windows to WSL", "warn",
            "This host runs in WSL. Other PCs can reach it only after Windows passes ports 8080 and 8999 into WSL and Windows Firewall lets them in.",
            "Run the installer again and say yes to the Windows network step, or run server/windows-network.ps1 in Windows PowerShell as administrator."),
    };

    /// <summary>Checks the public link looks like something a friend elsewhere can open. No network.</summary>
    public static DiagCheck PublicLinkCheck(string publicBaseUrl, HostEnvironment env)
    {
        if (!Uri.TryCreate(publicBaseUrl, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
            return new("Links", "Public link", "fail", $"'{publicBaseUrl}' isn't a web address.", "Set Tlp__PublicBaseUrl to the address friends use, like http://203.0.113.5:8080.");
        var check = AddressCheck("Links", "Public link", publicBaseUrl, uri.Host, env);
        if (check.State == "ok" && uri.Scheme == "http")
            return check with { Detail = check.Detail + " Plain http works; https needs a domain." };
        return check;
    }

    /// <summary>Whether a host name or IP is reachable by friends outside the house. No DNS lookups.</summary>
    public static DiagCheck AddressCheck(string area, string name, string shown, string host, HostEnvironment env)
    {
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
            return new(area, name, "fail", $"{shown} only works on this machine.", "Set it to your public IP or domain (Tlp__PublicBaseUrl, and Tlp__Syncplay__PublicHost if Syncplay is elsewhere).");
        if (IPAddress.TryParse(host.Trim('[', ']'), out var ip))
        {
            var kind = Classify(ip);
            if (kind == AddressKind.Loopback)
                return new(area, name, "fail", $"{shown} only works on this machine.", "Set it to your public IP or domain.");
            if (kind == AddressKind.Private)
            {
                var b = ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4().GetAddressBytes() : ip.GetAddressBytes();
                if ((env.Wsl || env.Docker) && b.Length == 4 && b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                    return new(area, name, "fail", $"{shown} looks like an address inside {(env.Wsl ? "WSL" : "Docker")}, which other PCs can't reach.",
                        env.Wsl
                            ? "Use the Windows PC's own network address instead (in Windows, run ipconfig and take the IPv4 Address, like 192.168.1.20). Re-running the installer sets this for you."
                            : "Use this machine's own network address or your public IP or domain instead.");
                var fix = env.Wsl
                    ? "Fine for testing with PCs at home, once Windows passes the ports into WSL. Friends elsewhere need your public IP or a domain, with the ports forwarded on the router."
                    : "Fine for testing with PCs at home. Friends elsewhere need your public IP or a domain, with the ports forwarded on the router.";
                return new(area, name, "warn", $"{shown} works for PCs on your home network only.", fix);
            }
        }
        return new(area, name, "ok", $"{shown}.");
    }

    public enum AddressKind { Loopback, Private, Public }

    public static AddressKind Classify(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip)) return AddressKind.Loopback;
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            if (b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168)
                || (b[0] == 169 && b[1] == 254) || (b[0] == 100 && b[1] >= 64 && b[1] <= 127))
                return AddressKind.Private;
            return AddressKind.Public;
        }
        if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6UniqueLocal) return AddressKind.Private;
        return AddressKind.Public;
    }

    private async Task<DiagCheck> PublicLinkAnswersAsync(string publicBaseUrl, CancellationToken ct)
    {
        if (!Uri.TryCreate(publicBaseUrl, UriKind.Absolute, out var uri)) return new("Links", "Public link answers", "info", "Skipped: the public link isn't valid.");
        try
        {
            using var http = httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(5);
            using var res = await http.GetAsync(new Uri(uri, "/healthz"), ct);
            return res.IsSuccessStatusCode
                ? new("Links", "Public link answers", "ok", $"{uri.GetLeftPart(UriPartial.Authority)} answered from this machine.")
                : new("Links", "Public link answers", "warn", $"{uri.GetLeftPart(UriPartial.Authority)} answered {(int)res.StatusCode}.", "Something else may be answering on that address, like a proxy that isn't set up yet.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            var why = ex is TaskCanceledException ? "no answer in 5 seconds" : ex.Message;
            return new("Links", "Public link answers", "warn", $"Couldn't open {uri.GetLeftPart(UriPartial.Authority)} from this machine ({why}).",
                "Many home routers can't loop back to their own public address, so this can fail even when friends get through. Check by opening the link on a phone with Wi-Fi off.");
        }
    }

    private static async Task<DiagCheck> PortCheckAsync(string area, string name, string host, int port, CancellationToken ct)
    {
        using var tcp = new TcpClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            await tcp.ConnectAsync(host, port, timeout.Token);
            return new(area, name, "ok", $"{host}:{port} accepts connections.");
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException)
        {
            return new(area, name, "fail", $"Nothing answers at {host}:{port}.",
                "Start the Syncplay server (Docker: the syncplay container; native: Tlp__Syncplay__ServerCommand), and check Tlp__Syncplay__BotHost and Port.");
        }
    }

    private DiagCheck TorrentCheck(TlpOptions o, HostEnvironment env)
    {
        if (!o.GroupTorrents)
            return new("Torrents", "Group torrents", "info", "Off: friends download from your copy or the public torrent only.");
        var recent = _peers.Where(p => p.At > _clock.GetUtcNow().AddDays(-2)).Select(p => p.Address).Distinct().ToList();
        var detail = $"On, {torrents.Count} file(s) ready to share.";
        if (recent.Count == 0) return new("Torrents", "Group torrents", "ok", detail + " No friend has announced yet.");
        // Behind Docker's port mapping every friend can show up as the same private gateway address,
        // and then friends can't find each other (the host's copy still works).
        var masked = recent.Where(a => Classify(a) == AddressKind.Private).ToList();
        if (!o.BehindProxy && masked.Count > 0 && masked.Count == recent.Count && (env.Docker || env.Wsl))
            return new("Torrents", "Group torrents", "warn",
                $"{detail} Friends show up as {string.Join(", ", masked.Take(3))}, an address inside this machine, so they can't share pieces with each other. Downloads still work from your copy.",
                "Usually Docker or WSL hiding friends' real addresses. Run the host container with network_mode: host, or with WSL mirrored networking. If a reverse proxy sits in front, set Tlp__BehindProxy=true.");
        return new("Torrents", "Group torrents", "ok", $"{detail} Seen {recent.Count} address(es) in the last two days.");
    }

    private IEnumerable<DiagCheck> NightChecks()
    {
        var now = _clock.GetUtcNow();
        var upcoming = nights.Nights.Where(n => n.StartsAt is null || n.StartsAt > now.AddHours(-12)).ToList();
        if (upcoming.Count == 0)
        {
            yield return new("Nights", "Upcoming nights", "info", "None yet.");
            yield break;
        }
        foreach (var night in upcoming)
        {
            var missing = night.Items.Where(i => i.ShelfPath is not null && shelf.Resolve(i.ShelfPath) is null).Select(i => i.FileName).ToList();
            yield return missing.Count == 0
                ? new("Nights", night.Title, "ok", $"All {night.Items.Count} file(s) available.")
                : new("Nights", night.Title, "fail", $"Missing from the shelf: {string.Join(", ", missing)}.",
                    "Put the files back on the shelf, or make a new night. Friends who haven't downloaded yet can only use the public torrent for these.");
        }
    }

    private DiagCheck FriendsCheck()
    {
        var last = nights.Nights.SelectMany(n => board.For(n.Id)).Select(e => (DateTimeOffset?)e.LastSeen).Max();
        return last is { } at
            ? new("Links", "Heard from a friend's app", "ok", $"Last {Ago(at)}, so friends can reach this host.")
            : new("Links", "Heard from a friend's app", "info", "Not yet. Once a friend opens a night link, their app reports here and this turns green.");
    }

    private string Ago(DateTimeOffset at)
    {
        var span = _clock.GetUtcNow() - at;
        return span.TotalMinutes < 1 ? "just now"
            : span.TotalHours < 1 ? $"{(int)span.TotalMinutes} min ago"
            : span.TotalDays < 1 ? $"{(int)span.TotalHours} h ago"
            : $"{(int)span.TotalDays} days ago";
    }
}

/// <summary>Where the host app is running, as far as it can tell.</summary>
public sealed record HostEnvironment(bool Docker, bool Wsl, string Os)
{
    public static HostEnvironment Detect()
    {
        var docker = File.Exists("/.dockerenv");
        var wsl = false;
        try { wsl = File.Exists("/proc/version") && File.ReadAllText("/proc/version").Contains("microsoft", StringComparison.OrdinalIgnoreCase); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return new(docker, wsl, System.Runtime.InteropServices.RuntimeInformation.OSDescription);
    }

    public string Describe() =>
        $"{Tlp.Core.AppInfo.Name} host on {Os}"
        + (Docker ? ", in Docker" : "") + (Wsl ? ", in WSL" : "") + ".";
}
