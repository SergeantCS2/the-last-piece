using Tlp.Core;

namespace Tlp.Viewer.Core;

/// <param name="Checking">True while the finished file is being compared with the host's copy.</param>
public sealed record ItemStatus(PackItem Item, FileState State, string Message, double Fraction, bool Checking = false);

/// <summary>Gets every file for a session into the folder, by the best available route, and checks it.</summary>
public sealed class NightSession(Pack pack, ViewerSettings settings, HttpDownloader http, TorrentDownloader torrents)
{
    public Pack Pack => pack;
    public string Folder => settings.MovieNightFolder;

    public IReadOnlyList<ItemStatus> QuickStatus() =>
        pack.Items.Select(i =>
        {
            var state = FileCheck.Quick(Folder, i);
            return new ItemStatus(i, state, Describe(state), state is FileState.SizeMatches ? 1 : 0);
        }).ToList();

    /// <summary>Picks the first download route for an item, honouring the viewer's preference.</summary>
    public static PackSource? ChooseSource(PackItem item, SourcePreference preference) => OrderSources(item, preference).FirstOrDefault();

    /// <summary>
    /// Every download route for an item, best first. If one fails or stalls the next is tried.
    /// Auto: the group torrent (friends share, the host fills gaps), then the host's copy, then
    /// the public torrent. Prefer torrent: the two torrents, then the host's copy. Prefer host:
    /// the host's copy first.
    /// </summary>
    public static IReadOnlyList<PackSource> OrderSources(PackItem item, SourcePreference preference)
    {
        PackSourceKind[] order = preference switch
        {
            SourcePreference.PreferTorrent => [PackSourceKind.Torrent, PackSourceKind.Magnet, PackSourceKind.Http],
            SourcePreference.PreferShelf => [PackSourceKind.Http, PackSourceKind.Torrent, PackSourceKind.Magnet],
            _ => [PackSourceKind.Torrent, PackSourceKind.Http, PackSourceKind.Magnet],
        };
        return order.SelectMany(k => item.Sources.Where(s => s.Kind == k)).ToList();
    }

    public static string SourceName(PackSourceKind kind) => kind switch
    {
        PackSourceKind.Http => "the host",
        PackSourceKind.Torrent => "the group torrent",
        _ => "the public torrent",
    };

    public async Task<ItemStatus> FetchAsync(PackItem item, IProgress<ItemStatus>? progress = null, CancellationToken ct = default)
    {
        var state = FileCheck.Quick(Folder, item);
        if (state == FileState.WrongSize)
            return new ItemStatus(item, state, "A different file with this name is in your folder. Rename or delete it, then try again.", 0);

        if (state != FileState.SizeMatches)
        {
            var sources = OrderSources(item, settings.SourcePreference);
            if (sources.Count == 0) throw new InvalidDataException($"No way to download {item.FileName}.");
            var problems = new List<string>();
            var done = false;
            foreach (var source in sources)
            {
                var name = SourceName(source.Kind);
                var p = new Progress<DownloadProgress>(d => progress?.Report(new ItemStatus(item, FileState.Partial,
                    $"{d.Status} ({name}) · {Human(d.Done)} of {Human(d.Total)} · {Human((long)d.BytesPerSecond)}/s",
                    d.Total is > 0 ? (double)d.Done / d.Total.Value : 0)));
                try
                {
                    switch (source.Kind)
                    {
                        case PackSourceKind.Http:
                            await http.DownloadAsync(new Uri(source.Uri), Folder, item, p, ct);
                            break;
                        case PackSourceKind.Torrent:
                            var file = await http.GetSmallAsync(new Uri(source.Uri), 10 * 1024 * 1024, ct);
                            await torrents.DownloadTorrentAsync(file, Folder, item, p, ct);
                            break;
                        default:
                            await torrents.DownloadAsync(source.Uri, Folder, item, p, ct);
                            break;
                    }
                    done = true;
                    break;
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    // Stalled, unreachable or a different release: note why and try the next route.
                    problems.Add($"From {name}: {ex.Message}");
                }
            }
            if (!done) throw new IOException(string.Join(" ", problems));
        }

        progress?.Report(new ItemStatus(item, FileState.SizeMatches, "Checking it's the same file as everyone else…", 1, Checking: true));
        var verified = await FileCheck.FullAsync(Folder, item, null, ct);
        return new ItemStatus(item, verified, Describe(verified), 1);
    }

    public static string Describe(FileState s) => s switch
    {
        FileState.Missing => "Not downloaded",
        FileState.Partial => "Partly downloaded, will resume",
        FileState.WrongSize => "Different file with the same name",
        FileState.SizeMatches => "Ready",
        FileState.Verified => "Ready, matches the host's file",
        FileState.HashMismatch => "Same size but different contents: download again",
        _ => s.ToString(),
    };

    public static string Human(long? bytes) => bytes switch
    {
        null => "?",
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.00} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.0} MB",
        >= 1L << 10 => $"{bytes / 1024.0:0} KB",
        _ => $"{bytes} B",
    };
}
