using System.Text;
using Tlp.Core;

namespace Tlp.Viewer.Core;

/// <summary>Tells the night's host who this viewer is and how far each file has got.</summary>
public static class StatusReporter
{
    public static ViewerFileReport FileReport(ItemStatus s, bool failed = false) => new()
    {
        FileName = s.Item.FileName,
        State = failed ? ViewerFileState.Failed
            : s.Checking ? ViewerFileState.Checking
            : s.State is FileState.SizeMatches or FileState.Verified ? ViewerFileState.Ready
            : s.State is FileState.Partial ? ViewerFileState.Downloading
            : s.State is FileState.WrongSize or FileState.HashMismatch ? ViewerFileState.Failed
            : ViewerFileState.Missing,
        Percent = (int)Math.Round(Math.Clamp(s.Fraction, 0, 1) * 100),
    };

    /// <summary>
    /// Posts the report if the pack has a status address. Never throws: the host being unreachable
    /// must not get in the way of downloading or watching. Returns whether the host took it.
    /// </summary>
    public static async Task<bool> SendAsync(HttpClient http, Pack pack, ViewerReport report, CancellationToken ct = default)
    {
        if (pack.StatusUri is null || !Uri.TryCreate(pack.StatusUri, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return false;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            using var body = new StringContent(PackJson.SerializeReport(report), Encoding.UTF8, "application/json");
            using var res = await http.PostAsync(uri, body, timeout.Token);
            return res.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            return false;
        }
    }
}
