# the-last-piece

Remote movie and anime night that looks as good as watching alone, without Discord screen share.

## TLDR

- **Sync the players, don't stream the picture.** Everyone plays the real MKV on their own machine and [Syncplay](https://syncplay.pl) keeps play, pause and seek in step. Original quality, proper anime subtitles, and each viewer has their own volume, window size, subtitle and audio track. Discord stays for voice.
- **Two apps of our own, built around Syncplay:**
  - **Host app** (the host): a shelf of tonight's files, share links and magnets for them, automatic cleanup so disk doesn't fill up, and the Syncplay server. Runs in Docker (Linux or WSL), or natively on Windows or Linux.
  - **Viewer app** (friends, Windows): paste or click a movie night link, it downloads the files, then one Join button opens a bundled player already connected to the room.
- **Bundled player is mpv, set up to look like VLC.** Anime MKV subtitles are styled ASS, which VLC and mpv both draw with the same library (libass), so they look the same. Plain subtitles get VLC's default look. mpv instead of VLC because Syncplay's on-screen chat only works in mpv.
- **Phones:** Synkplay, an unofficial Syncplay app for Android and iOS. It's third-party and untested here, so it gets tested in the MVP before we rely on it.
- **Status:** the Syncplay server runs in Docker and passes an automated test. Host and viewer apps are planned, not started.

Labels: **PROVEN** (tested here or confirmed in a source), **INFERRED** (reasoned, not verified), **UNKNOWN**.

## What the group needs

From the host, 2026-11-02:

- Files are MKV with soft (not burned-in) subtitles, mostly Japanese anime.
- Everyone uses Windows PCs or phones. The group uses VLC today.
- Discord's problems: picture quality, and stream volume tied to voice volume. Voice and chat are fine.
- Getting files to people has to be easy: links or magnets they can import, and a tool to make those links.
- Hosting: ideally the host's Windows PC, possibly a Linux server. Support Docker/WSL, native Linux and native Windows. Storage cleanup has to be easy.
- Eventually public, so others can run their own movie nights.

## Design

```
                               Discord (voice, unchanged)

 ┌──────────────────────── Host (the host's PC or Linux server) ─────────────────────────┐
 │  Host app                                        Syncplay server                    │
 │  - shelf: files for upcoming nights              - rooms, shared playlist           │
 │  - movie night page + pack link                  - play/pause/seek sync             │
 │  - magnets: paste originals, or generate         - on-screen chat                   │
 │  - cleanup: keep N days, pin, disk usage                                            │
 └───────────▲───────────────────────────────────────────────▲───────────────────────────┘
             │ HTTPS: pack + file downloads (resume)         │ Syncplay protocol, TCP 8999
             │ or magnet → torrent                            │
 ┌───────────┴───────────────────────────────┐   ┌───────────┴───────────────┐
 │ Windows viewer app                         │   │ Phone: Synkplay            │
 │ 1. open pack link → downloads into folder  │   │ streams the file from the  │
 │ 2. Join → Syncplay + bundled mpv           │   │ shelf URL (UNKNOWN, test)  │
 │    (VLC-style subs, own volume/size/tracks)│   │                            │
 └────────────────────────────────────────────┘   └────────────────────────────┘
```

### Player: bundled mpv with a VLC look

- Syncplay supports VLC 3, so anyone can keep VLC (PROVEN, Syncplay 1.7.6 player list). But chat over the video is mpv-only (PROVEN, Syncplay client guide), so the viewer app bundles mpv.
- Styled anime subtitles: VLC 3 decodes ASS/SSA with its libass module (PROVEN, VLC source), mpv uses libass too, so styled subs should look identical (INFERRED: same library, versions may differ).
- Plain subtitles: VLC 3 defaults on Windows are Arial, white, black outline, 50% black shadow down-right, about 6.25% of video height (PROVEN, VLC 3.0 source). `viewer/mpv.conf` approximates that (INFERRED until compared side by side).

### Getting files to people

A **movie night pack** is one link per night. It lists each episode's exact filename, size and where to get it. The exact filename matters because Syncplay's shared playlist finds files by name (PROVEN, client guide).

Each file in a pack can have one or both sources:

1. **Shelf link (default).** The host drops the file in the shelf, and the host app serves it over HTTPS with resume. Simplest for friends, nothing to install. Costs the host upload once per friend, ahead of time.
2. **Magnet.** Best when the file came from a public torrent: The host pastes the magnet he already has, and friends download from the public swarm, not from the host.

Pushback on generating magnets for the host's own files: a bare magnet only holds a hash, and clients get the file details from other peers. If the host's PC is the only copy, a magnet adds a step without saving bandwidth (INFERRED from how magnets work). The useful version is a private group torrent: a `.torrent` with the shelf as an HTTPS web seed, so friends who finish early share with the rest. That needs the torrent engine inside the viewer app, so it's phase 2, not MVP.

### Hosting and cleanup

| How | Host app | Syncplay server |
|---|---|---|
| Docker (Linux server, or WSL on Windows) | container | container (PROVEN, built and tested here) |
| Native Windows | single `.exe` | `syncplayServer.exe` from the official Syncplay download (PROVEN it ships, per server guide), started by the host app |
| Native Linux | single binary + systemd unit | Syncplay from source, started by the host app |

Cleanup rule for the MVP: files are deleted a set number of days after their movie night (default 7) unless pinned. The host page shows disk used and free space, with a delete button per file.

Network: friends need to reach TCP 8999 (Syncplay) and 443 (shelf) on the host. At home that means two router port forwards. If the domain is on Cloudflare, these records need to be DNS-only (grey cloud), since Cloudflare's normal proxy doesn't carry raw TCP like Syncplay's (INFERRED).

### Tech choice

**.NET (C#) for both apps**, with Syncplay kept as a separate process:

- One language for host and viewer, sharing the pack format.
- Builds a single native `.exe` for Windows and a single binary for Linux, plus a Docker image, all from GitHub Actions (INFERRED, standard `dotnet publish`).
- Viewer UI in Avalonia, which also runs on macOS and Linux for the public release (INFERRED).
- MonoTorrent (MIT) for magnets and group torrents in phase 2 (INFERRED: not yet checked that it does web seeds the way we need).
- The host already writes C# for CounterStrikeSharp plugins.

The alternative is Python/FastAPI for the host, which is quick to write but awkward to ship as a Windows app.

## MVP (v0.1): the first real movie night

Done when the host and one friend watch an episode this way and it beats Discord.

Host app:
- [ ] Shelf folder scanned on start and on change; files listed with size and date.
- [ ] Create a movie night: pick files, optionally paste a magnet per file, set the date. Produces a pack link and a simple web page with download buttons.
- [ ] Shelf downloads over HTTPS with resume, protected by the night's link (no account system yet).
- [ ] Cleanup: keep N days after the night, pin, delete, disk usage.
- [ ] Starts and supervises the Syncplay server natively, or sits next to it in Docker Compose.
- [ ] Ships as a Docker image, a Windows `.exe` and a Linux binary, built in GitHub Actions.

Viewer app (Windows):
- [ ] Zip or installer containing the app, portable mpv with our `mpv.conf`, and Syncplay.
- [ ] First run: your name, movie night folder.
- [ ] Paste or click a pack link: downloads with progress and resume, checks sizes. Magnet entries open in the user's torrent client for now.
- [ ] Join: writes Syncplay's config (server, room, password, folder, mpv path, shelf as trusted domain, skip the setup dialog) and launches it.

Phone:
- [ ] The host tests Synkplay on Android: connect to our server, play an ASS-heavy MKV from a shelf link, check sync and subtitles.

Already done (phase 0):

| Path | What | Status |
|---|---|---|
| `server/` | Syncplay 1.7.6 Docker image, compose file, optional Caddy shelf | PROVEN: built, ran, tested |
| `tools/smoketest.py` | Two fake viewers: wrong password, join, pause sync, chat | PROVEN: passes locally and in CI |
| `.github/workflows/server-ci.yml` | Builds the image and runs the smoke test on every push | PROVEN: green on GitHub Actions |
| `viewer/` | Setup guide, `syncplay.ini` starter, `mpv.conf` with the VLC look | INFERRED: not tried on a real client |
| `docs/options.md` | Survey of every option and why | Sources linked |

The Caddy shelf in `server/` is a stopgap. The host app replaces it in the MVP.

## Roadmap after the MVP

**Phase 2: files without the host's upload**
- Torrent engine in the viewer app: magnets download inside the app.
- Group torrents generated by the host app from shelf files, with the shelf as web seed, so friends share pieces with each other.
- Discord bot: posts the night's pack link, shows who's downloaded and who's in the room, bridges Discord text into the on-screen chat.

**Phase 3: going public**
- Accounts or invite links, several groups per host, per-room passwords.
- Setup wizard for hosts (ports, domain, HTTPS).
- macOS and Linux viewer builds.
- License and third-party notices for bundled mpv and Syncplay (Syncplay is Apache-2.0, PROVEN; mpv's license depends on the build, UNKNOWN which we'd bundle).
- The tool stays content-neutral: no built-in search or index of torrents.

**Phase 4: only if needed**
- Our own Android app if Synkplay falls short.
- Zero-install browser fallback: OBS → MediaMTX stream, or a web player using JASSUB for subtitles.

## Running the Syncplay server today

```bash
cd server
cp .env.example .env          # set SYNCPLAY_PASSWORD at least
docker compose up -d --build
python3 ../tools/smoketest.py --password 'your-password' --room movienight
```

Notes from building it:

- Syncplay isn't on PyPI, so the image installs from the tagged GitHub release (PROVEN).
- `--isolate-rooms` silently disables the saved-rooms database in 1.7.6 (PROVEN, read in `server.py` and confirmed by testing). Isolation is off by default; opt in with `SYNCPLAY_ISOLATE_ROOMS=true`.
- Docker's port proxy accepts connections before Syncplay is listening, so the smoke test waits for a real handshake (`--wait`).

## Open decisions

1. **.NET for both apps?** Default if no answer: yes.
2. **Where does the first real night run:** your Windows PC (Docker in WSL) or the Linux server? Both get supported. This decides which one gets tested first.
3. **Do your friends have a torrent client installed?** If not, MVP magnets mean installing one, and shelf links stay the main path until phase 2.
4. **Project name and license** for the public release. MIT or Apache-2.0 for our own code is the usual choice; the bundled players keep their own licenses.
