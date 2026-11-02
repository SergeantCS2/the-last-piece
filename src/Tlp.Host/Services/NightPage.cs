using System.Net;
using System.Text;
using Tlp.Core;

namespace Tlp.Host.Services;

/// <summary>The page a friend sees when they open a movie night link in a browser.</summary>
public static class NightPage
{
    public static string Render(Pack pack, string packUrl)
    {
        var h = (string? s) => WebUtility.HtmlEncode(s ?? "");
        var sb = new StringBuilder();
        sb.Append("""
            <!doctype html><html lang="en"><head><meta charset="utf-8">
            <meta name="viewport" content="width=device-width,initial-scale=1">
            <meta name="robots" content="noindex">
            <title>Movie night</title>
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
            </style></head><body><main>
            """);
        sb.Append($"<h1>{h(pack.Title)}</h1>");
        if (pack.StartsAt is { } at)
            sb.Append($"<p class=\"muted\">Starts <time datetime=\"{at:O}\">{at:ddd d MMM, HH:mm} (UTC{at:zzz})</time></p>");

        sb.Append($"""
            <div class="card"><strong>Easiest:</strong> open this in The Last Piece viewer app. It downloads everything and joins the room for you.<br>
            <a class="button" href="{h(ViewerLinks.ForPack(packUrl))}">Open in The Last Piece</a>
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
            }
            if (item.Sha256 is not null) sb.Append($"<div class=\"muted\">SHA-256 <code>{h(item.Sha256)}</code></div>");
            sb.Append("<div class=\"muted\">Keep the file name exactly as shown so the shared playlist finds it.</div></div>");
        }

        sb.Append($"""
            <h2>Join</h2><div class="card">
            Syncplay server <code>{h(pack.Syncplay.Host)}:{pack.Syncplay.Port}</code>, room <code>{h(pack.Syncplay.Room)}</code>.
            <div class="muted">The viewer app fills this in for you.</div></div>
            </main></body></html>
            """);
        return sb.ToString();
    }
}
