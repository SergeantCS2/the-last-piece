using Microsoft.Extensions.Options;

namespace Tlp.Host.Services;

/// <summary>Read and delete files under the shelf folder, never outside it.</summary>
public sealed class Shelf(IOptions<TlpOptions> options)
{
    private readonly string _root = Path.GetFullPath(options.Value.ShelfDir);

    public string Root => _root;

    public IReadOnlyList<ShelfFile> List()
    {
        if (!Directory.Exists(_root)) return [];
        var files = new List<ShelfFile>();
        foreach (var full in Directory.EnumerateFiles(_root, "*", new EnumerationOptions
                 {
                     RecurseSubdirectories = true,
                     IgnoreInaccessible = true,
                     AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
                 }))
        {
            var info = new FileInfo(full);
            if (info.Name.StartsWith('.')) continue;
            files.Add(new ShelfFile(ToRelative(full), info.Length, info.LastWriteTimeUtc));
        }
        return files.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Full path for a shelf-relative path, or null if it escapes the shelf or doesn't exist.</summary>
    public string? Resolve(string? relative)
    {
        if (string.IsNullOrWhiteSpace(relative)) return null;
        var full = Path.GetFullPath(Path.Combine(_root, relative.Replace('\\', '/').TrimStart('/')));
        var rootWithSep = _root.EndsWith(Path.DirectorySeparatorChar) ? _root : _root + Path.DirectorySeparatorChar;
        if (!full.StartsWith(rootWithSep, StringComparison.Ordinal)) return null;
        return File.Exists(full) ? full : null;
    }

    public bool Delete(string relative)
    {
        var full = Resolve(relative);
        if (full is null) return false;
        File.Delete(full);
        return true;
    }

    public (long Total, long Free)? Disk()
    {
        try
        {
            var drive = new DriveInfo(_root);
            return (drive.TotalSize, drive.AvailableFreeSpace);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private string ToRelative(string full) => Path.GetRelativePath(_root, full).Replace('\\', '/');
}
