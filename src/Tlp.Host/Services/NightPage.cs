using System.Net;
using System.Text;
using Tlp.Core;

namespace Tlp.Host.Services;

/// <summary>The page a friend sees when they open a session link in a browser.</summary>
public static class NightPage
{
    /// <summary>Small version of the Neon logo (docs/logo/a-neon-small.svg).</summary>
    private const string LogoSvg = """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100" width="40" height="40" aria-hidden="true" class="logo"><defs><linearGradient id="lg" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#FF5C7A"/><stop offset="1" stop-color="#8E4BFF"/></linearGradient></defs><path d="M14,23 L23,14 H38 A12,12 0 1,1 62,14 H86 V38 A12,12 0 1,1 86,62 V77 L77,86 H14 V62 A12,12 0 1,0 14,38 Z" fill="url(#lg)"/><path d="M40,34 L66,50 L40,66 Z" fill="#FFFFFF"/></svg>""";

    public static string Render(Pack pack, string packUrl)
    {
        var h = (string? s) => WebUtility.HtmlEncode(s ?? "");
        var sb = new StringBuilder();
        sb.Append($$$"""
            <!doctype html><html lang="en"><head><meta charset="utf-8">
            <meta name="viewport" content="width=device-width,initial-scale=1">
            <meta name="robots" content="noindex">
            <title>{{{h(string.IsNullOrWhiteSpace(pack.Group) ? AppInfo.Name : pack.Group)}}}</title>
            <link rel="icon" href="data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 100 100'%3E%3Cdefs%3E%3ClinearGradient id='g' x1='0' y1='0' x2='1' y2='1'%3E%3Cstop offset='0' stop-color='%23FF5C7A'/%3E%3Cstop offset='1' stop-color='%238E4BFF'/%3E%3C/linearGradient%3E%3C/defs%3E%3Cpath d='M14,23 L23,14 H38 A12,12 0 1,1 62,14 H86 V38 A12,12 0 1,1 86,62 V77 L77,86 H14 V62 A12,12 0 1,0 14,38 Z' fill='url(%23g)'/%3E%3Cpath d='M40,34 L66,50 L40,66 Z' fill='%23FFFFFF'/%3E%3C/svg%3E">
            <style>
            :root{color-scheme:light dark;--bg:#fafafa;--fg:#1a1a1a;--muted:#666;--card:#fff;--line:#ddd;--accent:#c0392b}
            @media (prefers-color-scheme:dark){:root{--bg:#121212;--fg:#eee;--muted:#999;--card:#1e1e1e;--line:#333;--accent:#e74c3c}}
            body{font:16px/1.5 system-ui,sans-serif;background:var(--bg);color:var(--fg);margin:0;padding:16px}
            main{max-width:720px;margin:auto}
            .card{background:var(--card);border:1px solid var(--line);border-radius:10px;padding:14px 16px;margin:12px 0}
            .muted{color:var(--muted);font-size:14px}
            a.button{display:inline-block;background:var(--accent);color:#fff;padding:10px 16px;border-radius:8px;text-decoration:none;font-weight:600;margin:4px 8px 4px 0}
            a.secondary{background:transparent;color:var(--accent);border:1px solid var(--accent)}
            code{word-break:break-all;font-size:13px}
            .kv{display:grid;grid-template-columns:5.5em 1fr auto;gap:8px;align-items:center;margin:6px 0}
            .kv button{font:inherit;font-size:13px;padding:4px 10px;border-radius:6px;border:1px solid var(--accent);background:transparent;color:var(--accent)}
            ol{padding-left:20px;margin:0}li{margin:6px 0}
            .brand{display:flex;align-items:center;gap:10px;color:var(--muted);font-weight:600}h1{margin:8px 0 4px}
            </style></head><body><main>
            """);
        // The app's logo and, when the host set one, the group's name above the night's title.
        sb.Append("<div class=\"brand\">" + LogoSvg + (string.IsNullOrWhiteSpace(pack.Group) ? "" : $"<span>{h(pack.Group)}</span>") + "</div>");
        sb.Append($"<h1>{h(pack.Title)}</h1>");
        if (pack.StartsAt is { } at)
            sb.Append($"<p class=\"muted\">Starts <time datetime=\"{at:O}\">{at:ddd d MMM, HH:mm} (UTC{at:zzz})</time></p>");

        sb.Append($"""
            <div class="card"><strong>Easiest:</strong> open this in {h(AppInfo.Name)} viewer app. It downloads everything and joins the room for you.<br>
            <a class="button" href="{h(ViewerLinks.ForPack(packUrl))}">Open in {h(AppInfo.Name)}</a>
            <div class="muted">No app yet? Ask the host for the Windows viewer zip. Or download below and join with Syncplay yourself.</div></div>
            """);

        sb.Append("<h2>Files</h2>");
        foreach (var item in pack.Items)
        {
            sb.Append($"<div class=\"card\"><strong>{h(item.FileName)}</strong>");
            if (item.Size is { } size) sb.Append($" <span class=\"muted\">{size / 1024d / 1024 / 1024:0.00} GB</span>");
            sb.Append("<br>");
            foreach (var src in item.Sources)
            {
                if (src.Kind == PackSourceKind.Http)
                    sb.Append($"<a class=\"button\" href=\"{h(src.Uri)}\">Download</a>");
                else if (src.Kind == PackSourceKind.Magnet)
                    sb.Append($"<a class=\"button secondary\" href=\"{h(src.Uri)}\">Magnet</a>");
                else if (src.Kind == PackSourceKind.Torrent)
                    sb.Append($"<a class=\"button secondary\" href=\"{h(src.Uri)}\">Group torrent</a>");
            }
            if (item.Sha256 is not null) sb.Append($"<div class=\"muted\">SHA-256 <code>{h(item.Sha256)}</code></div>");
            sb.Append("<div class=\"muted\">Keep the file name exactly as shown so the shared playlist finds it.</div></div>");
        }

        sb.Append($"""
            <h2>Join</h2><div class="card">
            Syncplay server <code>{h(pack.Syncplay.Host)}:{pack.Syncplay.Port}</code>, room <code>{h(pack.Syncplay.Room)}</code>.
            <div class="muted">The viewer app fills this in for you.</div></div>
            <h2>On a phone</h2><div class="card">
            <ol>
            <li>Download each file above on your phone. Keep the name exactly as shown.</li>
            <li>Open Synkplay (a Syncplay app for Android) and join with these:
              <div class="kv"><span>Server</span><code>{h(pack.Syncplay.Host)}</code><button type="button" data-copy="{h(pack.Syncplay.Host)}">Copy</button></div>
              <div class="kv"><span>Port</span><code>{pack.Syncplay.Port}</code><button type="button" data-copy="{pack.Syncplay.Port}">Copy</button></div>
              <div class="kv"><span>Room</span><code>{h(pack.Syncplay.Room)}</code><button type="button" data-copy="{h(pack.Syncplay.Room)}">Copy</button></div>
            """);
        if (!string.IsNullOrEmpty(pack.Syncplay.Password))
            sb.Append($"""
              <div class="kv"><span>Password</span><code>{h(pack.Syncplay.Password)}</code><button type="button" data-copy="{h(pack.Syncplay.Password)}">Copy</button></div>
            """);
        sb.Append("""
            </li>
            <li>Pick the downloaded file in Synkplay. Play and pause then stay in step with everyone.</li>
            </ol>
            <div class="muted">Voice stays on Discord. Subtitles show as the file has them.</div></div>
            </main>
            <script>
            document.querySelectorAll('[data-copy]').forEach(b => b.addEventListener('click', async () => {
              try { await navigator.clipboard.writeText(b.dataset.copy); b.textContent = 'Copied'; }
              catch { b.textContent = 'Select and copy'; }
              setTimeout(() => b.textContent = 'Copy', 1500);
            }));
            </script>
            </body></html>
            """);
        return sb.ToString();
    }
}
