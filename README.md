# the-last-piece

Remote movie and anime night that looks as good as watching alone, without Discord screen share.

## TLDR

- **Don't stream the picture. Sync the players.** Everyone plays the real file in their own mpv, and [Syncplay](https://syncplay.pl) keeps play, pause and seek in step. Quality is the original file, ASS subtitles render perfectly, and each viewer controls volume, size, subtitles and audio track.
- **Don't build the sync engine.** Syncplay already does it well and is maintained (1.7.6, Aug 2026). This repo is the glue: our own server, a "shelf" so friends get files from the host instead of torrenting, one-click setup, and Discord extras.
- **On-screen chat comes free.** Syncplay draws chat over the video in mpv, including a niconico-style scrolling mode.
- **Fallback for anyone who can't install:** OBS → MediaMTX → a browser link (WebRTC). Lower quality, burned-in subs, but zero setup.
- **Status:** the Syncplay server runs in Docker and passes a smoke test (join, pause sync, chat, wrong password). Nothing has been tried with real friends yet.

Labels used below: **PROVEN** (tested here or confirmed in a source), **INFERRED** (reasoned, not verified), **UNKNOWN**.

## Recommendation

**Primary: synced local playback with Syncplay + mpv, self-hosted.**

1. The host runs a private, password-protected Syncplay server (this repo's `server/`), on his workstation's Docker or a small VPS.
2. Each viewer runs Syncplay with mpv (Windows/Linux), IINA or mpv (macOS), or Synkplay (Android/iOS).
3. Files reach viewers one of two ways:
   - **Pre-download** (default): everyone has the file before the night, from the shelf or their own torrent client. Zero live bandwidth, perfect quality.
   - **Stream from the shelf**: the shared playlist points at an HTTPS URL on the host's shelf and mpv plays it directly. The original file, untouched, but it uses the host's upload live.

**Fallback: host streaming in the browser** for a viewer who can't or won't install anything: OBS sends WebRTC to MediaMTX on the host's machine, the friend opens a link.

Why this way round (full comparison in [docs/options.md](docs/options.md)):

- Host streaming costs the host's upload once per viewer anyway (INFERRED), and on top of that re-encodes the picture, burns in one subtitle style and fixes one audio track for everyone. Syncing local files has none of those losses.
- Anime subtitles are the hardest part to get right. mpv uses libass, the reference ASS renderer (PROVEN). Browser and Jellyfin paths have open reports of subtitle drift (PROVEN, see options doc).
- Pushback on building from scratch: a custom sync protocol or player would take months and still lose to Syncplay. Build only what Syncplay doesn't do.

## Architecture

```
                         Discord (voice + text, unchanged)
                                     │
                       ┌─────────────┴──────────────┐
                       │  later: movie night bot     │  /movienight, links,
                       │  (Syncplay client + Discord) │  chat bridge
                       └─────────────┬──────────────┘
                                     │ Syncplay protocol (JSON lines, TCP 8999)
 ┌────────────────── the host's box (WSL Docker) or VPS ──────────────────┐
 │  syncplay-server  ← rooms, shared playlist, play/pause/seek, chat    │
 │  shelf (Caddy)    ← HTTPS file server with a shared login            │
 └───────▲──────────────────────▲──────────────────────────▲───────────┘
         │ sync + chat           │ file bytes (pre-download  │
         │                       │ or live stream by URL)    │
 ┌───────┴───────┐      ┌────────┴──────┐           ┌────────┴──────┐
 │ Viewer PC      │      │ Viewer Mac     │           │ Viewer phone  │
 │ Syncplay + mpv │      │ Syncplay + IINA│           │ Synkplay      │
 │ own volume,    │      │                │           │               │
 │ subs, size     │      │                │           │               │
 └────────────────┘      └────────────────┘           └───────────────┘

 Fallback: OBS (the host) ─WHIP→ MediaMTX ─WebRTC→ friend's browser
```

Bandwidth if friends stream from the shelf live: a typical 1080p 24-minute episode of 1–1.5 GB averages about 5.5–8.5 Mbps, so five viewers need roughly 30–45 Mbps of steady upload, more at bitrate peaks (INFERRED from file sizes). Pre-downloading avoids this entirely.

## What's in this commit

| Path | What | Status |
|---|---|---|
| `server/Dockerfile`, `entrypoint.sh` | Syncplay 1.7.6 server image, configured by env vars | **PROVEN**: built and ran here |
| `server/docker-compose.yml` | Server, plus the optional shelf (`--profile shelf`) | **PROVEN**: both started here |
| `server/Caddyfile` | Shelf: file browsing, shared login, byte-range requests for seeking | **PROVEN**: 401 without login, 200 with, 206 on range requests |
| `tools/smoketest.py` | Fake two viewers: wrong password, join, pause sync, chat | **PROVEN**: passes against the container |
| `.github/workflows/server-ci.yml` | Builds the image and runs the smoke test on every push | **PROVEN**: green on GitHub Actions |
| `viewer/README.md`, `syncplay.ini.example`, `mpv.conf` | Setup guide and starter config for friends | **INFERRED**: keys match Syncplay 1.7.6 source, not tried on a real client |
| `docs/options.md` | Survey of every option and why | Sources linked |

Things learned while building it:

- Syncplay isn't on PyPI, so the image installs from the tagged GitHub release (PROVEN).
- `--isolate-rooms` silently disables the persistent rooms database in 1.7.6 (PROVEN, read in `server.py` and confirmed by testing). Isolation is off by default here; it's opt-in with `SYNCPLAY_ISOLATE_ROOMS=true`.

## Running the server

```bash
cd server
cp .env.example .env          # set SYNCPLAY_PASSWORD at least
docker compose up -d --build
python3 ../tools/smoketest.py --password 'your-password' --room movienight
```

Friends connect to `your-host:8999`. Port 8999/TCP has to be reachable from the internet (router port forward, or a VPS). Cloudflare's normal proxy doesn't carry raw TCP like this (INFERRED), so point a DNS-only record at it.

With the shelf:

```bash
docker run --rm caddy:2 caddy hash-password --plaintext 'shelf-password'   # paste into SHELF_PASSWORD_HASH
docker compose --profile shelf up -d
```

Set `SHELF_SITE` to a real hostname (e.g. `shelf.yourdomain`) and Caddy gets an HTTPS certificate on its own (INFERRED, standard Caddy behaviour, not tested here). Only put files on the shelf that you're fine sharing with the group.

## Roadmap

**Phase 0, done:** server in Docker, smoke test, CI, viewer configs, research.

**Phase 1, first real night (MVP)**
- Pick where the server lives and open port 8999.
- TLS for Syncplay (`SYNCPLAY_TLS_DIR`, certbot layout: `privkey.pem`, `cert.pem`, `chain.pem`).
- The host plus one friend: install Syncplay + mpv, drop in the configs, watch one episode. Note every point of friction.

**Phase 2, kill the friction**
- Windows "viewer kit": a zip built by GitHub Actions with portable Syncplay, mpv, our `mpv.conf` and a `Join movie night.bat` that connects straight to the room.
- Shelf on by default, with download links posted before the night.
- Phone path: test Synkplay on Android with an ASS-heavy episode.

**Phase 3, Discord and fun**
- A small bot that joins the Syncplay room as a silent viewer (the smoke test shows how simple the protocol is). It posts `/movienight` with the playlist and shelf links, shows who's connected and ready, and bridges Discord text into the on-screen chat and back.
- Overlay chat styling: tune Syncplay's scrolling mode (font, lines, margins) for a niconico feel.

**Phase 4, only if someone needs it**
- Zero-install path, two choices: (a) the OBS → MediaMTX browser stream, or (b) a web player that speaks Syncplay through a WebSocket bridge, renders ASS with JASSUB and plays from the shelf. (b) keeps quality but browsers can't play every MKV (HEVC 10-bit, FLAC/AC3 audio), so files may need a remux or transcode (INFERRED).

## Open questions for the host

1. **What does everyone watch on?** Windows PCs, Macs, phones, TVs? If anyone is TV-only, the browser fallback moves up.
2. **Your home upload speed, and should friends get files from you?** That decides between pre-download only, streaming from the shelf, or both.
3. **Where should the server live?** Your workstation (needs port 8999 forwarded and the PC on during movie night) or a small always-on VPS. And is your domain behind Cloudflare's proxy?
4. How many people on a typical night?
5. Do people usually have the exact same release (same group, same file)? Syncplay tolerates different files if durations match, but the shared playlist matches by filename.
6. Is the "remote location" you torrent from a seedbox? If so, the shelf could run there instead of at home.
