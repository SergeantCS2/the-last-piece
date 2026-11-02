# the-last-piece

Remote movie and anime night that looks as good as watching alone, without Discord screen share.

## TLDR

- **Sync the players, don't stream the picture.** Everyone plays the real MKV on their own machine and [Syncplay](https://syncplay.pl) keeps play, pause and seek in step. You get original quality and proper anime subtitles, and each viewer has their own volume, window size, subtitle and audio track. Discord stays for voice.
- **Two apps of our own, built around Syncplay (MVP code is in this repo):**
  - **Host app** (`src/Tlp.Host`, for the host):
    - A shelf of episodes and movie night links.
    - A magnet finder that takes a page link and pulls the magnets off it.
    - Automatic cleanup.
    - Runs in Docker (Linux or WSL), or natively on Windows or Linux.
  - **Viewer app** (`src/Tlp.Viewer`, for friends on Windows):
    - Click a movie night link and it downloads each file, either from the host or by torrent with its built-in engine.
    - It checks every file against the host's copy.
    - Then one button opens the bundled player, already in the room.
- **Bundled player is mpv, set up to look like VLC.** Anime subtitles are styled ASS, which VLC and mpv both draw with libass. mpv rather than VLC because Syncplay's on-screen chat only works in mpv.
- **No more uTorrent needed.** The viewer app has an open-source torrent engine built in (MonoTorrent, MIT).
- **Status:**
  - Host and viewer logic are tested on Linux, including a real torrent download between two peers (47 automated tests).
  - The host runs in Docker.
  - The Windows zip with mpv and Syncplay builds in GitHub Actions, but **nobody has run it on a Windows PC yet**.

![Viewer app preview](docs/viewer-preview.png)

*Viewer window rendered off-screen on Linux with sample data. The real Windows look is UNKNOWN until someone runs it.*

Labels: **PROVEN** (tested here or confirmed in a source), **INFERRED** (reasoned, not verified), **UNKNOWN**.

## What the group needs

From the host, 2026-11-02:

- Files are MKV with soft subtitles, mostly Japanese anime, from torrents.
- Everyone uses Windows PCs or phones. The group uses VLC and mostly uTorrent today.
- Discord's problems:
  - Picture quality.
  - Stream volume is tied to voice volume.
  - Voice and chat are fine as they are.
- Getting files to people has to be easy:
  - Links or magnets they can import.
  - A generator that takes a page link and grabs the magnet from it.
- Two ways to get files, both supported:
  - Friends download from the original source themselves, for best quality.
  - Or they download from the host's copy.
  - Either way, the app checks they have the same file.
- P2P between friends wanted.
- Hosting is Linux or WSL: the host's Windows PC (Docker/WSL) or a Linux server. Native Windows and Linux are nice to have. Cleanup has to be easy.
- A text chat that pops up on screen would be fun.
- A custom Android app later is fine.
- Eventually public.

## Design

```
                               Discord (voice, unchanged)

 ┌──────────────────────── Host (the host's PC in WSL, or Linux server) ──────────────────┐
 │  Host app (:8080, HTTPS via Caddy)              Syncplay server (:8999)             │
 │  - admin page: shelf, nights, cleanup           - rooms, shared playlist            │
 │  - movie night link → pack.json + web page      - play/pause/seek sync              │
 │  - files served with resume                     - on-screen chat (mpv)              │
 │  - "find magnets on this page"                                                      │
 └───────────▲───────────────────────────────────────────────▲──────────────────────────┘
             │ pack + shelf downloads                         │ Syncplay protocol
             │                                                │
 ┌───────────┴───────────────────────────────┐   ┌───────────┴───────────────┐
 │ Windows viewer app                         │   │ Phone: Synkplay            │
 │ 1. tlp:// link → reads the pack            │   │ (third-party, untested)    │
 │ 2. each file: torrent swarm or host copy   │   └───────────────────────────┘
 │    → size + SHA-256 check against host     │
 │ 3. Join → bundled Syncplay + mpv           │◀──── torrent swarm (public, and
 └────────────────────────────────────────────┘      friends seeding to each other)
```

### Movie night pack

One link per night. The host app turns it into a page with an "Open in The Last Piece" button, and into `pack.json` for the app:

- Title and start time.
- Syncplay server, room and password, so friends never type them.
- For each episode:
  - The exact filename. Syncplay's shared playlist matches files by name (PROVEN, client guide).
  - Size, and SHA-256 if the host has the file.
  - Sources: a shelf download link, a magnet, or both.

### Getting files: both ways, verified

| Source | When | Cost to the host |
|---|---|---|
| **Magnet** (built-in torrent) | The episode came from a public torrent. The host pastes the magnet, or pastes the page and the host app finds it. | None: friends download from the public swarm. |
| **Shelf** (download from host) | The host has the file and there's no torrent, or the swarm is slow. | Upload once per friend, ahead of time. |

- **Picking a source.** Each friend chooses Auto, Prefer torrent, or Prefer host in settings. Auto takes the host's copy when there is one, because it's the most reliable, and the torrent otherwise. If the torrent fails, the app doesn't switch to the host's copy by itself yet.
- **Verifying.** After any download, the app checks size and SHA-256 against the host's copy, so a different release with the same name is caught. Without a hash it checks size only.
  - PROVEN: unit tests cover this, including a deliberately wrong file.
  - INFERRED: the check only runs when the host has the file on the shelf. A magnet-only entry gets a size check only.
- **Seeding while open.** While the app is open, finished torrent files keep seeding, so friends who started late get pieces from friends, not just strangers (on by default, can be turned off).

### P2P: yes

- **What works now:**
  - Magnets download inside the app.
  - Friends who have the file seed to each other and the public swarm.
  - PROVEN: a test does a real magnet download between two local peers through a tracker.
- **Group torrent (next phase), for files only the host has:**
  - The host app makes a private `.torrent` for the shelf file.
  - It runs its own tracker, and uses the shelf link as a web seed.
  - the host's upload then gets shared out: each friend pulls some pieces from the host and the rest from each other.
  - The pack format already reserves a `torrent` source type for this.
  - MonoTorrent has a tracker server and web seed support (PROVEN for the tracker, used in our tests; INFERRED for web seeds until tested).
- **Sync stays on the Syncplay server.** Peer-to-peer sync without a server is possible but would mean rewriting Syncplay for little gain.

### Why not uTorrent

- uTorrent is closed source and has a history of bundled adware and, in 2015, a bundled cryptocurrency miner (INFERRED: widely reported, not re-verified here).
- The app's built-in engine is MonoTorrent: MIT licensed, open source, and only downloads what's in the pack.
- Anyone who wants an external client can still copy the magnet from the night page. qBittorrent is the open-source pick there.
- **Note:** a torrent shows your IP to everyone in the swarm. That's the same as uTorrent today, but worth knowing before going public.

### Player: bundled mpv with a VLC look

- Syncplay supports VLC 3, so anyone can keep VLC (PROVEN, Syncplay player list). Chat over the video is mpv-only (PROVEN, Syncplay client guide).
- Styled anime subtitles: VLC 3 and mpv both use libass, so they should look the same (INFERRED: same library, versions differ).
- Plain subtitles: `viewer/mpv.conf` approximates VLC 3's Windows default (Arial, white, black outline, soft shadow). INFERRED until compared side by side.
- mpv reads `mpv/portable_config/mpv.conf` next to `mpv.exe` (PROVEN, mpv docs), so the bundled player doesn't touch anyone's own mpv setup.

### Hosting and cleanup

| How | Host app | Syncplay server | Status |
|---|---|---|---|
| Docker (Linux server, or WSL on Windows) | `server/docker-compose.yml` | same compose file | PROVEN: both images built and tested here |
| Native Linux | `dotnet publish` binary | Syncplay from source, started by the host app (`Tlp__Syncplay__ServerCommand`) | INFERRED |
| Native Windows | `dotnet publish` `.exe` | `syncplayServer.exe` from the Syncplay download, started by the host app | INFERRED |

Cleanup:

- A shelf file is deleted a set number of days (default 7) after the last movie night that uses it, unless pinned.
- Files no night uses are never deleted automatically. The admin page lists them with a delete button and shows disk space.
- Cleanup has a preview, and can be switched to report-only (`Tlp__CleanupEnabled=false`).
- PROVEN: logic unit-tested, and delete works in Docker.
- Docker detail: the host app runs as user id 1654. The shelf folder has to be writable by it for cleanup (for example `sudo chown -R 1654 shelf`).

## MVP (v0.1): the first real movie night

Done when the host and one friend watch an episode this way and it beats Discord.

Host app:
- [x] Shelf listed with size, date, which nights use each file, pin and delete, disk usage. PROVEN (tests and Docker).
- [x] Create a movie night from shelf files and/or magnets. Gives a share link, a web page and `pack.json`. PROVEN.
- [x] "Find magnets on a page": paste a release page link and pick from the magnets and `.torrent` links on it. PROVEN on test HTML.
  - It refuses addresses on your own network, so a shared host can't be used to probe it.
  - Real tracker sites may block it or need a login (UNKNOWN until tried on the sites you use). Pasting the magnet directly always works.
- [x] Shelf downloads with resume, protected by the night's secret link. PROVEN (range requests tested).
- [x] SHA-256 of shelf files, computed in the background and cached. PROVEN.
- [x] Cleanup: keep N days after the night, pin, preview, run. PROVEN.
- [x] Admin page protected by a token. A token is generated and saved if not set. PROVEN.
- [x] Docker image plus compose with Syncplay. Optional Caddy for automatic HTTPS. PROVEN for the host image, INFERRED for Caddy.
- [x] Can start and restart a native Syncplay server. INFERRED, not run against a real `syncplayServer`.

Viewer app (Windows):
- [x] Paste a link or click "Open in The Last Piece" (`tlp://`, registered for the current user on first run). INFERRED on Windows.
- [x] Downloads each file by torrent or from the host, with progress and resume, then verifies. PROVEN on Linux.
- [x] Settings: your name, movie night folder, source preference, chat style, seed while open.
- [x] Join: writes Syncplay's per-folder `.syncplay` config and opens the bundled Syncplay with the server, room, password, name, mpv and the first episode. PROVEN that the command line and config are what Syncplay 1.7.6's source expects; INFERRED that it all works together on Windows.
- [x] Zip with `TheLastPiece.exe`, portable mpv with our `mpv.conf`, and portable Syncplay, built by GitHub Actions. PROVEN that it builds (about 120 MB) and that the bundled mpv starts on a Windows runner. Download it from the latest `dotnet` run under Actions.
- [ ] Tried on a real Windows PC.
- [ ] Single instance: clicking a second link while the app is open currently opens a second window.

Phone:
- [ ] The host tests Synkplay on Android: connect to our server, open an ASS-heavy MKV from a shelf link, check sync and subtitles.

## Roadmap after the MVP

**Phase 2: P2P and polish**
- Group torrent for shelf files: host tracker plus web seed (see P2P above).
- Skip Syncplay's own connect window. Syncplay 1.7.6 shows it every launch by default, with our values filled in, so friends just press "Run Syncplay". The fix is a small settings file we ship with Syncplay; it needs testing on Windows first.
- Single instance, so a second link goes to the open window.
- Discord bot:
  - Posts the night's link.
  - Shows who's downloaded and who's in the room.
  - Bridges Discord text into the on-screen chat.
- Signed installer and auto-update. Until then Windows SmartScreen warns on the unsigned `.exe` ("More info, Run anyway").

**Phase 3: going public**
- Accounts or invite links, several groups per host, per-room passwords.
- Setup wizard for hosts (ports, domain, HTTPS).
- macOS and Linux viewer builds (Avalonia and MonoTorrent run there; mpv and Syncplay bundling differs).
- License for our code, and third-party notices (Syncplay is Apache-2.0; the mpv Windows build bundles GPL parts, so we ship it unmodified with its license and a source link).
- Stays content-neutral: no built-in torrent search or index.

**Phase 4: only if needed**
- Our own Android app if Synkplay falls short.
- Zero-install browser fallback: a web player using JASSUB for subtitles, or an OBS → MediaMTX stream.

## Running the host

Docker (Linux server, or WSL on Windows):

```bash
cd server
cp .env.example .env      # set SYNCPLAY_PASSWORD and TLP_PUBLIC_URL at least
mkdir -p shelf            # put episodes here
docker compose up -d --build
docker compose logs host | grep -i token    # first admin login, if TLP_ADMIN_TOKEN is empty
```

Then open `http://<host>:8080`, log in with the token, and create a movie night.

Ports friends need to reach:

| Port | What | Notes |
|---|---|---|
| 8999/TCP | Syncplay | Router port forward. If the domain is on Cloudflare, these records must be DNS-only (grey cloud), since Cloudflare's normal proxy doesn't carry raw TCP like Syncplay's (INFERRED). |
| 8080/TCP | Host app | For a first test. |
| 80 and 443/TCP | Host app over HTTPS | With `--profile https` and `TLP_DOMAIN` set. |

Checking the Syncplay server: `python3 tools/smoketest.py --password '<password>' --room movienight`.

Native (no Docker): `dotnet publish src/Tlp.Host -c Release -r linux-x64` (or `win-x64`). Settings go in `appsettings.json` or `Tlp__...` environment variables (see `src/Tlp.Host/TlpOptions.cs`).

Development: .NET 10 SDK, `dotnet test TheLastPiece.sln`.

### Notes from building it

- Syncplay isn't on PyPI, so the image installs from the tagged GitHub release (PROVEN).
- `--isolate-rooms` silently disables the saved-rooms database in 1.7.6 (PROVEN). Isolation is opt-in with `SYNCPLAY_ISOLATE_ROOMS=true`.
- Docker's port proxy accepts connections before Syncplay listens, so the smoke test waits for a real handshake.

## Repo map

| Path | What |
|---|---|
| `src/Tlp.Core` | Pack format, magnet parsing, magnet finder, safe filenames |
| `src/Tlp.Host` | Host app: API, admin page (`wwwroot`), cleanup, hashing, Dockerfile |
| `src/Tlp.Viewer.Core` | Viewer logic: pack client, downloads, torrent, verify, Syncplay launch |
| `src/Tlp.Viewer` | Windows app window (Avalonia) |
| `tests/Tlp.Tests` | 47 tests, including a real two-peer torrent transfer |
| `server/` | Compose file, Syncplay image, Caddy |
| `viewer/mpv.conf` | VLC look for the bundled mpv |
| `tools/` | Syncplay smoke test, viewer zip packager |
| `docs/options.md` | Every option considered and why |

## Open decisions

1. **Where does the first real night run?** WSL on your PC or the Linux server. Both are supported; this decides which gets tried first.
2. **A domain for HTTPS?** Friends can start on `http://<ip>:8080`. A domain gives HTTPS and a nicer link.
3. **Who's the test friend?** One person runs the zip on Windows before a real night.
4. **License** for the public release. MIT or Apache-2.0 is the usual choice.
