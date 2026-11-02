namespace Tlp.Host;

public sealed class TlpOptions
{
    public const string Section = "Tlp";

    /// <summary>Folder holding the files to share. Mounted read-write so cleanup can delete.</summary>
    public string ShelfDir { get; set; } = "/shelf";

    /// <summary>Where nights, pins and the generated admin token are stored.</summary>
    public string DataDir { get; set; } = "/data";

    /// <summary>Admin password for the host page. If empty, one is generated and saved to DataDir/admin-token.txt.</summary>
    public string? AdminToken { get; set; }

    /// <summary>Public base URL friends use, e.g. https://movies.example.com. Used to build links.</summary>
    public string PublicBaseUrl { get; set; } = "http://localhost:8080";

    /// <summary>Shelf files are deleted this many days after the last movie night that uses them.</summary>
    public int RetentionDays { get; set; } = 7;

    /// <summary>How often the cleanup job runs.</summary>
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>When false, cleanup only reports what it would delete.</summary>
    public bool CleanupEnabled { get; set; } = true;

    /// <summary>Make private group torrents for shelf files so friends share pieces (host is the web seed and tracker).</summary>
    public bool GroupTorrents { get; set; } = true;

    /// <summary>
    /// Set when the host app sits behind a reverse proxy (e.g. the Caddy in docker-compose), so the
    /// tracker sees friends' real addresses from X-Forwarded-For instead of the proxy's.
    /// </summary>
    public bool BehindProxy { get; set; }

    /// <summary>Your group's name, shown as the big title in friends' viewer apps. Empty shows "Movie night".</summary>
    public string? GroupName { get; set; }

    /// <summary>Optional Discord webhook URL; the admin page can post a night's link there.</summary>
    public string? DiscordWebhookUrl { get; set; }

    public SyncplayOptions Syncplay { get; set; } = new();
}

public sealed class SyncplayOptions
{
    /// <summary>Host name friends' Syncplay connects to. Defaults to PublicBaseUrl's host.</summary>
    public string? PublicHost { get; set; }
    public int Port { get; set; } = 8999;
    public string? Password { get; set; }
    public string DefaultRoom { get; set; } = "movienight";

    /// <summary>
    /// Optional: path to a Syncplay server to start and keep running (native installs).
    /// e.g. "C:\Program Files\Syncplay\syncplayServer.exe" or "syncplay-server".
    /// Leave empty when Syncplay runs in its own container.
    /// </summary>
    public string? ServerCommand { get; set; }
    public string? ServerArguments { get; set; }

    /// <summary>
    /// The room helper: a quiet Syncplay connection that fills each night's shared playlist and
    /// shows who's in the room on the admin page.
    /// </summary>
    public bool RoomBot { get; set; } = true;
    /// <summary>Where the host app itself reaches Syncplay. "syncplay" in docker-compose, 127.0.0.1 for native installs.</summary>
    public string BotHost { get; set; } = "127.0.0.1";
    public string BotName { get; set; } = "TheLastPiece";
    public string BotRoom { get; set; } = "_thelastpiece";
    public TimeSpan BotPollInterval { get; set; } = TimeSpan.FromSeconds(15);
}
