using Tlp.Core;

namespace Tlp.Viewer.Core;

public sealed class PackClient(HttpClient http)
{
    public async Task<Pack> FetchAsync(Uri packUri, CancellationToken ct = default)
    {
        var pack = PackJson.DeserializePack(await http.GetStringAsync(packUri, ct))
                   ?? throw new InvalidDataException("The movie night link returned nothing.");
        Validate(pack);
        return pack;
    }

    /// <summary>Rejects packs that could write outside the folder or that we can't download.</summary>
    public static void Validate(Pack pack)
    {
        if (pack.Version > Pack.CurrentVersion)
            throw new InvalidDataException("This movie night needs a newer version of the viewer app.");
        foreach (var item in pack.Items)
        {
            if (!SafeFileName.IsSafe(item.FileName))
                throw new InvalidDataException($"Unsafe file name in movie night: '{item.FileName}'.");
            foreach (var s in item.Sources)
            {
                var ok = s.Kind switch
                {
                    PackSourceKind.Http or PackSourceKind.Torrent => Uri.TryCreate(s.Uri, UriKind.Absolute, out var u) && u.Scheme is "http" or "https",
                    PackSourceKind.Magnet => MagnetInfo.TryParse(s.Uri, out _),
                    _ => true, // future kinds are skipped, not fatal
                };
                if (!ok) throw new InvalidDataException($"Bad download source for '{item.FileName}'.");
            }
        }
    }
}
