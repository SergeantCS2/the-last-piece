using System.Net;
using System.Net.Http.Headers;
using Tlp.Core;

namespace Tlp.Viewer.Core;

public sealed record DownloadProgress(long Done, long? Total, double BytesPerSecond, string Status);

/// <summary>Downloads a shelf file with resume: bytes go to name.tlp-part and are renamed when complete.</summary>
public sealed class HttpDownloader(HttpClient http)
{
    /// <summary>A small download held in memory (a .torrent file), refused if over <paramref name="maxBytes"/>.</summary>
    public async Task<byte[]> GetSmallAsync(Uri source, int maxBytes, CancellationToken ct = default)
    {
        using var response = await http.GetAsync(source, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > maxBytes) throw new InvalidDataException("That file is too big to be a .torrent.");
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > maxBytes) throw new InvalidDataException("That file is too big to be a .torrent.");
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    public async Task DownloadAsync(Uri source, string folder, PackItem item, IProgress<DownloadProgress>? progress = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(folder);
        var target = FileCheck.TargetPath(folder, item);
        var part = target + FileCheck.PartSuffix;
        var have = File.Exists(part) ? new FileInfo(part).Length : 0;
        if (item.Size is { } size && have > size) { File.Delete(part); have = 0; }

        if (item.Size is { } known && have == known)
        {
            File.Move(part, target, overwrite: true);
            progress?.Report(new DownloadProgress(have, known, 0, "Downloaded"));
            return;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, source);
        if (have > 0) request.Headers.Range = new RangeHeaderValue(have, null);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        if (have > 0 && response.StatusCode != HttpStatusCode.PartialContent)
            have = 0; // server ignored the range: start over

        var total = item.Size ?? (response.Content.Headers.ContentLength is { } len ? len + have : null);
        await using (var input = await response.Content.ReadAsStreamAsync(ct))
        await using (var output = new FileStream(part, have > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, useAsync: true))
        {
            var buffer = new byte[1 << 20];
            var started = DateTime.UtcNow;
            long thisSession = 0;
            var lastReport = DateTime.MinValue;
            int read;
            while ((read = await input.ReadAsync(buffer, ct)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), ct);
                have += read;
                thisSession += read;
                if (DateTime.UtcNow - lastReport > TimeSpan.FromMilliseconds(250))
                {
                    lastReport = DateTime.UtcNow;
                    var secs = Math.Max(0.001, (DateTime.UtcNow - started).TotalSeconds);
                    progress?.Report(new DownloadProgress(have, total, thisSession / secs, "Downloading from host"));
                }
            }
        }

        if (item.Size is { } expected && new FileInfo(part).Length != expected)
            throw new IOException($"Download of {item.FileName} stopped early ({new FileInfo(part).Length} of {expected} bytes). Try again to resume.");

        File.Move(part, target, overwrite: true);
        progress?.Report(new DownloadProgress(have, total, 0, "Downloaded"));
    }
}
