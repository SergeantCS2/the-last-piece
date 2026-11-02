using System.Security.Cryptography;
using Tlp.Core;

namespace Tlp.Viewer.Core;

public enum FileState
{
    Missing,
    /// <summary>A download was started and can resume.</summary>
    Partial,
    /// <summary>A file with this name exists but is the wrong size: not the same file.</summary>
    WrongSize,
    /// <summary>Right name and size; no hash to compare (or not checked yet).</summary>
    SizeMatches,
    /// <summary>SHA-256 matches the host's copy.</summary>
    Verified,
    /// <summary>SHA-256 differs: same size but a different file.</summary>
    HashMismatch,
}

public static class FileCheck
{
    public const string PartSuffix = ".tlp-part";

    public static string TargetPath(string folder, PackItem item) => Path.Combine(folder, item.FileName);

    /// <summary>Quick check by name and size only.</summary>
    public static FileState Quick(string folder, PackItem item)
    {
        var path = TargetPath(folder, item);
        if (!File.Exists(path)) return File.Exists(path + PartSuffix) ? FileState.Partial : FileState.Missing;
        var size = new FileInfo(path).Length;
        if (item.Size is { } expected && size != expected) return FileState.WrongSize;
        return FileState.SizeMatches;
    }

    /// <summary>Full check including SHA-256 when the pack has one.</summary>
    public static async Task<FileState> FullAsync(string folder, PackItem item, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var quick = Quick(folder, item);
        if (quick != FileState.SizeMatches || item.Sha256 is null) return quick;
        var hash = await Sha256Async(TargetPath(folder, item), progress, ct);
        return string.Equals(hash, item.Sha256, StringComparison.OrdinalIgnoreCase) ? FileState.Verified : FileState.HashMismatch;
    }

    public static async Task<string> Sha256Async(string path, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, useAsync: true);
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1 << 20];
        long done = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            sha.AppendData(buffer, 0, read);
            done += read;
            progress?.Report(stream.Length == 0 ? 1 : (double)done / stream.Length);
        }
        return Convert.ToHexStringLower(sha.GetHashAndReset());
    }
}
