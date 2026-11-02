using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace Tlp.Host.Services;

/// <summary>
/// Posts a movie night's link to a Discord channel through a webhook (Server Settings,
/// Integrations, Webhooks). A webhook needs no bot account and can only post to its own channel.
/// </summary>
public sealed class DiscordAnnouncer(HttpClient http, IOptions<TlpOptions> options)
{
    private readonly TlpOptions _opts = options.Value;

    public bool Configured => IsWebhookUrl(_opts.DiscordWebhookUrl);

    /// <summary>Only real Discord webhook addresses, so a typo can't send night links somewhere else.</summary>
    public static bool IsWebhookUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u)
        && u.Scheme == Uri.UriSchemeHttps
        && u.Host is "discord.com" or "discordapp.com" or "ptb.discord.com" or "canary.discord.com"
        && u.AbsolutePath.StartsWith("/api/webhooks/", StringComparison.Ordinal);

    public static string Message(Night night, TlpOptions opts)
    {
        var view = NightView.From(night, opts);
        var lines = new List<string> { $"**{night.Title}**" };
        // Discord shows <t:…:F> in each reader's own time zone.
        if (night.StartsAt is { } at) lines.Add($"Starts <t:{at.ToUnixTimeSeconds()}:F> (<t:{at.ToUnixTimeSeconds()}:R>)");
        lines.Add($"{night.Items.Count} file(s). Open this in {Tlp.Core.AppInfo.Name} to download and join:");
        lines.Add(view.ShareUrl);
        return string.Join("\n", lines);
    }

    public async Task AnnounceAsync(Night night, CancellationToken ct)
    {
        if (!Configured) throw new InvalidOperationException("No Discord webhook configured.");
        var payload = new
        {
            username = "Movie night",
            content = Message(night, _opts),
            allowed_mentions = new { parse = Array.Empty<string>() }, // never ping @everyone by accident
        };
        using var res = await http.PostAsJsonAsync(_opts.DiscordWebhookUrl, payload, ct);
        res.EnsureSuccessStatusCode();
    }
}
