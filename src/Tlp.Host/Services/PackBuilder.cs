using Tlp.Core;

namespace Tlp.Host.Services;

public static class PackBuilder
{
    public static Pack Build(Night night, TlpOptions opts, Shelf shelf, HashCache hashes, GroupTorrents? torrents = null)
    {
        var baseUrl = opts.PublicBaseUrl.TrimEnd('/');
        var items = night.Items.Select((item, i) =>
        {
            var sources = new List<PackSource>();
            long? size = null;
            string? sha = null;
            if (item.ShelfPath is not null && shelf.Resolve(item.ShelfPath) is { } full)
            {
                size = new FileInfo(full).Length;
                sha = hashes.Get(item.ShelfPath);
                if (torrents?.Get(item.ShelfPath) is not null)
                    sources.Add(new PackSource { Kind = PackSourceKind.Torrent, Uri = $"{baseUrl}/n/{night.Token}/files/{i}/group.torrent" });
                sources.Add(new PackSource
                {
                    Kind = PackSourceKind.Http,
                    Uri = $"{baseUrl}/n/{night.Token}/files/{i}/{Uri.EscapeDataString(item.FileName)}",
                });
            }
            if (item.Magnet is not null)
            {
                sources.Add(new PackSource { Kind = PackSourceKind.Magnet, Uri = item.Magnet });
                if (size is null && MagnetInfo.TryParse(item.Magnet, out var m)) size = m?.ExactLength;
            }
            return new PackItem { FileName = item.FileName, Size = size, Sha256 = sha, Sources = sources };
        }).ToList();

        return new Pack
        {
            Title = night.Title,
            StartsAt = night.StartsAt,
            Syncplay = new SyncplayInfo
            {
                Host = opts.Syncplay.PublicHost ?? new Uri(baseUrl).Host,
                Port = opts.Syncplay.Port,
                Room = night.Room,
                Password = opts.Syncplay.Password,
            },
            Items = items,
            StatusUri = $"{baseUrl}/n/{night.Token}/status",
        };
    }
}

public sealed record NightView(string Id, string Title, DateTimeOffset? StartsAt, string Room, IReadOnlyList<NightItem> Items, string ShareUrl, string ViewerLink)
{
    public static NightView From(Night n, TlpOptions opts)
    {
        var share = $"{opts.PublicBaseUrl.TrimEnd('/')}/n/{n.Token}";
        return new NightView(n.Id, n.Title, n.StartsAt, n.Room, n.Items, share, ViewerLinks.ForPack(share + "/pack.json"));
    }
}

public static class ViewerLinks
{
    /// <summary>tlp://open?pack=... opens the Windows viewer app straight on this night.</summary>
    public static string ForPack(string packUrl) => "tlp://open?pack=" + Uri.EscapeDataString(packUrl);
}

public static class AdminToken
{
    public static string Resolve(TlpOptions opts, ILogger log)
    {
        if (!string.IsNullOrWhiteSpace(opts.AdminToken)) return opts.AdminToken;
        Directory.CreateDirectory(opts.DataDir);
        var path = Path.Combine(opts.DataDir, "admin-token.txt");
        if (File.Exists(path)) return File.ReadAllText(path).Trim();
        var token = Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
        File.WriteAllText(path, token);
        log.LogWarning("No admin token configured. Generated one and saved it to {Path}", path);
        return token;
    }
}
