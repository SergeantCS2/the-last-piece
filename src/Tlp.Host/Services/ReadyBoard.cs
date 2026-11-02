using System.Collections.Concurrent;
using Tlp.Core;

namespace Tlp.Host.Services;

/// <summary>
/// Who's ready for a night: the latest report from each viewer app that opened its link.
/// Kept in memory only; viewer apps report again every minute while open, so a host restart
/// fills it back up on its own.
/// </summary>
public sealed class ReadyBoard(TimeProvider? clock = null)
{
    public const int MaxViewersPerNight = 64;
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, Entry>> _nights = new();

    public sealed record Entry(ViewerReport Report, DateTimeOffset LastSeen);

    /// <summary>
    /// Stores a report after cleaning it: names trimmed and capped, only the night's own files kept.
    /// Returns an error message when the report can't be used.
    /// </summary>
    public string? Record(Night night, ViewerReport report)
    {
        var name = Clean(report.Name);
        if (name is null) return "A name is required.";
        var known = night.Items.Select(i => i.FileName).ToHashSet(StringComparer.Ordinal);
        var files = (report.Files ?? [])
            .Where(f => f is not null && known.Contains(f.FileName))
            .DistinctBy(f => f.FileName)
            .Select(f => f with { Percent = Math.Clamp(f.Percent, 0, 100) })
            .ToList();
        var app = report.App is { Length: > 0 and <= 32 } a ? a : null;

        var viewers = _nights.GetOrAdd(night.Id, _ => new(StringComparer.OrdinalIgnoreCase));
        if (!viewers.ContainsKey(name) && viewers.Count >= MaxViewersPerNight) return "Too many viewers for this night.";
        viewers[name] = new Entry(new ViewerReport { Name = name, App = app, Files = files }, _clock.GetUtcNow());
        return null;
    }

    public IReadOnlyList<Entry> For(string nightId) =>
        _nights.TryGetValue(nightId, out var v) ? v.Values.OrderBy(e => e.Report.Name, StringComparer.OrdinalIgnoreCase).ToList() : [];

    public void Forget(string nightId) => _nights.TryRemove(nightId, out _);

    /// <summary>Printable name, 1 to 32 characters, no control characters.</summary>
    public static string? Clean(string? name)
    {
        if (name is null) return null;
        var s = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (s.Length > ViewerReport.MaxNameLength) s = s[..ViewerReport.MaxNameLength].Trim();
        return s.Length == 0 ? null : s;
    }
}
