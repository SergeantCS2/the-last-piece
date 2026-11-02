namespace Tlp.Host.Services;

/// <summary>
/// Decides which shelf files can go. Rules, kept deliberately conservative:
/// - Pinned files are never deleted.
/// - A file that no movie night uses is never deleted automatically (it shows in the
///   host page so the host can delete it by hand).
/// - A file used by nights is deleted once every one of those nights is more than
///   RetentionDays in the past. A night without a date counts from when it was created.
/// </summary>
public static class CleanupPlanner
{
    public static IReadOnlyList<ShelfEntry> Describe(
        IReadOnlyList<ShelfFile> files, IReadOnlyList<Night> nights, IReadOnlySet<string> pins, int retentionDays)
    {
        return files.Select(f =>
        {
            var users = nights.Where(n => n.Items.Any(i => i.ShelfPath == f.Path)).ToList();
            DateTimeOffset? deleteAfter = null;
            if (users.Count > 0 && !pins.Contains(f.Path))
                deleteAfter = users.Max(NightTime).AddDays(retentionDays);
            return new ShelfEntry(f.Path, f.Size, f.Modified, pins.Contains(f.Path),
                users.Select(n => n.Title).ToList(), deleteAfter);
        }).ToList();
    }

    public static IReadOnlyList<ShelfEntry> Due(IReadOnlyList<ShelfEntry> entries, DateTimeOffset now) =>
        entries.Where(e => e.DeleteAfter is { } when_ && when_ <= now).ToList();

    private static DateTimeOffset NightTime(Night n) => n.StartsAt ?? n.CreatedAt;
}
