namespace Tlp.Host.Services;

public sealed record Night
{
    public required string Id { get; init; }
    /// <summary>Secret in the share link. Anyone with it can download the night's files.</summary>
    public required string Token { get; init; }
    public required string Title { get; init; }
    public DateTimeOffset? StartsAt { get; init; }
    public required string Room { get; init; }
    public required IReadOnlyList<NightItem> Items { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed record NightItem
{
    /// <summary>Name viewers save the file as.</summary>
    public required string FileName { get; init; }
    /// <summary>Path relative to the shelf, when the host serves the file.</summary>
    public string? ShelfPath { get; init; }
    /// <summary>Magnet link, when viewers can fetch it from a torrent swarm.</summary>
    public string? Magnet { get; init; }
}

public sealed record ShelfFile(string Path, long Size, DateTimeOffset Modified);

public sealed record ShelfEntry(string Path, long Size, DateTimeOffset Modified, bool Pinned, IReadOnlyList<string> UsedBy, DateTimeOffset? DeleteAfter);
