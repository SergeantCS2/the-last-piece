using Tlp.Core;

namespace Tlp.Viewer.Core;

public sealed record ItemStatus(PackItem Item, FileState State, string Message, double Fraction);

/// <summary>Gets every file for a movie night into the folder, by the best available route, and checks it.</summary>
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

    /// <summary>Picks the download route for an item, honouring the viewer's preference.</summary>
    public static PackSource? ChooseSource(PackItem item, SourcePreference preference)
    {
        var http = item.Sources.FirstOrDefault(s => s.Kind == PackSourceKind.Http);
        var magnet = item.Sources.FirstOrDefault(s => s.Kind == PackSourceKind.Magnet);
        return preference switch
        {
            SourcePreference.PreferTorrent => magnet ?? http,
            _ => http ?? magnet,
        };
    }

    public async Task<ItemStatus> FetchAsync(PackItem item, IProgress<ItemStatus>? progress = null, CancellationToken ct = default)
    {
        var state = FileCheck.Quick(Folder, item);
        if (state == FileState.WrongSize)
            return new ItemStatus(item, state, "A different file with this name is in your folder. Rename or delete it, then try again.", 0);

        if (state != FileState.SizeMatches)
        {
            var source = ChooseSource(item, settings.SourcePreference)
                         ?? throw new InvalidDataException($"No way to download {item.FileName}.");
            var p = new Progress<DownloadProgress>(d => progress?.Report(new ItemStatus(item, FileState.Partial,
                $"{d.Status} · {Human(d.Done)} of {Human(d.Total)} · {Human((long)d.BytesPerSecond)}/s",
                d.Total is > 0 ? (double)d.Done / d.Total.Value : 0)));
            if (source.Kind == PackSourceKind.Http)
                await http.DownloadAsync(new Uri(source.Uri), Folder, item, p, ct);
            else
                await torrents.DownloadAsync(source.Uri, Folder, item, p, ct);
        }

        progress?.Report(new ItemStatus(item, FileState.SizeMatches, "Checking it's the same file as everyone else…", 1));
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
