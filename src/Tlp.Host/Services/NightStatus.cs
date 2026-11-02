using Tlp.Core;

namespace Tlp.Host.Services;

/// <summary>
/// One line per person for the admin page: what their viewer app reported about downloads,
/// and whether they're in the night's Syncplay room. People are matched by name, which is the
/// same in both places when they use the viewer app.
/// </summary>
public sealed record NightStatus(
    string Room,
    IReadOnlyList<string> Playlist,
    bool HelperConnected,
    string? HelperError,
    DateTimeOffset? CheckedAt,
    IReadOnlyList<PersonStatus> People)
{
    /// <summary>A viewer that hasn't reported for this long is shown as gone quiet (app closed).</summary>
    public static readonly TimeSpan Quiet = TimeSpan.FromMinutes(3);

    public static NightStatus Build(Night night, IReadOnlyList<ReadyBoard.Entry> reports, RoomSnapshot room, DateTimeOffset now)
    {
        var inRoom = room.UsersIn(night.Room).ToDictionary(u => u.Name, StringComparer.OrdinalIgnoreCase);
        var people = new List<PersonStatus>();
        foreach (var e in reports)
        {
            var files = night.Items.Select(i =>
                e.Report.Files.FirstOrDefault(f => f.FileName == i.FileName)
                ?? new ViewerFileReport { FileName = i.FileName, State = ViewerFileState.Missing }).ToList();
            inRoom.Remove(e.Report.Name, out var user);
            people.Add(new PersonStatus(
                e.Report.Name,
                e.Report.App,
                files,
                files.Count(f => f.State == ViewerFileState.Ready),
                files.Count,
                e.LastSeen,
                now - e.LastSeen <= Quiet,
                user is not null,
                user?.File));
        }
        // In the room but no report: someone using their own Syncplay, or a viewer with reporting off.
        foreach (var user in inRoom.Values)
            people.Add(new PersonStatus(user.Name, null, [], null, night.Items.Count, null, false, true, user.File));

        return new NightStatus(
            night.Room,
            night.Items.Select(i => i.FileName).ToList(),
            room.Connected,
            room.Error,
            room.At,
            people.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList());
    }
}

public sealed record PersonStatus(
    string Name,
    string? App,
    IReadOnlyList<ViewerFileReport> Files,
    int? ReadyCount,
    int Total,
    DateTimeOffset? LastSeen,
    bool AppOpen,
    bool InRoom,
    string? Playing);
