using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Tlp.Core;

namespace Tlp.Host.Services;

/// <summary>Nights and pinned files, kept as small JSON files in DataDir.</summary>
public sealed class NightStore
{
    private readonly string _nightsPath;
    private readonly string _pinsPath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private List<Night> _nights;
    private HashSet<string> _pins;

    public NightStore(IOptions<TlpOptions> options)
    {
        var dir = options.Value.DataDir;
        Directory.CreateDirectory(dir);
        _nightsPath = Path.Combine(dir, "nights.json");
        _pinsPath = Path.Combine(dir, "pins.json");
        _nights = Load<List<Night>>(_nightsPath) ?? [];
        _pins = new HashSet<string>(Load<List<string>>(_pinsPath) ?? [], StringComparer.Ordinal);
    }

    public IReadOnlyList<Night> Nights => _nights;
    public IReadOnlySet<string> Pins => _pins;

    public Night? FindByToken(string token) =>
        _nights.FirstOrDefault(n => CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(n.Token), System.Text.Encoding.UTF8.GetBytes(token)));

    public async Task<Night> AddAsync(string title, DateTimeOffset? startsAt, string room, IReadOnlyList<NightItem> items)
    {
        var night = new Night
        {
            Id = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(6)),
            Token = Base64Url(RandomNumberGenerator.GetBytes(18)),
            Title = title,
            StartsAt = startsAt,
            Room = room,
            Items = items,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await MutateAsync(() => _nights = [.. _nights, night]);
        return night;
    }

    public async Task<bool> RemoveAsync(string id)
    {
        var removed = false;
        await MutateAsync(() =>
        {
            removed = _nights.Any(n => n.Id == id);
            _nights = _nights.Where(n => n.Id != id).ToList();
        });
        return removed;
    }

    public Task SetPinnedAsync(string path, bool pinned) => MutateAsync(() =>
    {
        var copy = new HashSet<string>(_pins, StringComparer.Ordinal);
        if (pinned) copy.Add(path); else copy.Remove(path);
        _pins = copy;
    });

    /// <summary>
    /// Rewrites night items: <paramref name="change"/> returns a new item, or null to leave it.
    /// Returns the shelf paths of the items that changed.
    /// </summary>
    public async Task<IReadOnlyList<string>> UpdateItemsAsync(Func<NightItem, NightItem?> change)
    {
        var changed = new List<string>();
        await MutateAsync(() =>
        {
            _nights = _nights.Select(n =>
            {
                var touched = false;
                var items = n.Items.Select(i =>
                {
                    var next = change(i);
                    if (next is null) return i;
                    touched = true;
                    if (next.ShelfPath is not null) changed.Add(next.ShelfPath);
                    return next;
                }).ToList();
                return touched ? n with { Items = items } : n;
            }).ToList();
        });
        return changed.Distinct(StringComparer.Ordinal).ToList();
    }

    private async Task MutateAsync(Action change)
    {
        await _lock.WaitAsync();
        try
        {
            change();
            await SaveAsync(_nightsPath, _nights);
            await SaveAsync(_pinsPath, _pins.Order(StringComparer.Ordinal).ToList());
        }
        finally
        {
            _lock.Release();
        }
    }

    private static T? Load<T>(string path) =>
        File.Exists(path) ? PackJson.Deserialize<T>(File.ReadAllText(path)) : default;

    private static async Task SaveAsync<T>(string path, T value)
    {
        var tmp = path + ".tmp";
        await File.WriteAllTextAsync(tmp, PackJson.Serialize(value));
        File.Move(tmp, path, overwrite: true);
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
