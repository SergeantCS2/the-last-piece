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
    - Click a movie night link and it downloads each file with its built-in torrent engine: friends share pieces with each other and the host fills the gaps. It falls back to a plain download from the host if the torrent stalls.
    - It checks every file against the host's copy.
    - Then one button opens the bundled player, already in the room.
- **Bundled player is mpv, set up to look like VLC.** Anime subtitles are styled ASS, which VLC and mpv both draw with libass. mpv rather than VLC because Syncplay's on-screen chat only works in mpv.
- **No more uTorrent needed.** The viewer app has an open-source torrent engine built in (MonoTorrent, MIT).
- **Status:**
  - 62 automated tests on Linux, including a group torrent between the real host app and two friends.
  - The host runs in Docker.
  - On every push, GitHub Actions builds the Windows zip and runs the real `TheLastPiece.exe` on a Windows machine. It opens the window, downloads a test torrent, and starts the bundled Syncplay and mpv, which must join a real Syncplay server with the file open (see CI for the latest result).
  - **A person hasn't used it on a Windows PC yet.** The checklist at the end is for that first try.

![Viewer app preview](docs/viewer-preview.png)

*Viewer window rendered off-screen on Linux with sample data. CI saves the same picture from the Windows run in the `viewer-self-test` artifact.*

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
| **Group torrent** (built in) | The host has the file on the shelf. Made automatically. | About one copy's worth, shared out: friends swap pieces, the host's shelf fills the gaps. |
| **Public magnet** (built in) | The episode came from a public torrent. The host pastes the magnet, or pastes the page and the host app finds it. | None: friends download from the public swarm. |
| **Host's copy** (plain download) | Fallback, or for anyone who prefers it. | One copy per friend. |

- **Picking a source.** Each friend chooses in settings:
  - Best available (default): group torrent, then the host's copy, then the public magnet.
  - Torrents first: group torrent, then public magnet, then the host's copy.
  - Host's copy first.
- **Fallback.** If a source fails, or a torrent gets no new data for 3 minutes, the app moves to the next one. PROVEN in a test with a broken torrent link.
- **Verifying.** After any download, the app checks size and SHA-256 against the host's copy, so a different release with the same name is caught. Without a hash it checks size only.
  - PROVEN: unit tests cover this, including a deliberately wrong file.
  - INFERRED: the check only runs when the host has the file on the shelf. A magnet-only entry gets a size check only.
- **Seeding while open.** While the app is open, finished torrent files keep seeding, so friends who started late get pieces from friends, not just strangers (on by default, can be turned off).

### P2P: built

- **Group torrent, for files on the host's shelf:**
  - The host app makes a private `.torrent` for each shelf file in a night, in the background.
  - It runs the tracker itself, behind the night's secret link, so only people with the link find each other. Private means clients don't use DHT or peer exchange, so strangers can't join.
  - The shelf link is the web seed: the host's copy is always there to fill gaps, without a torrent client on the host.
  - PROVEN, with the real host app over real sockets:
    - Friend A downloads with nobody else around, entirely from the web seed.
    - the host's file is then deleted, and friend B still gets the whole file from friend A through the tracker.
- **Public magnets** download inside the app too, and finished files keep seeding while the app is open. PROVEN with two local peers.
- **Where it can fall short (INFERRED, test on a real night):**
  - The tracker hands out the address each friend connects from. If the host app runs behind something that hides those addresses, friends can't find each other and everyone pulls from the web seed instead.
    - Hiding happens with Docker Desktop or WSL port forwarding on Windows (UNKNOWN for the host's setup), and with a reverse proxy unless `TLP_BEHIND_PROXY=true`.
    - Downloads still work either way, they just don't save the host's upload.
  - Two friends can swap pieces only if at least one of them can accept connections. The app asks the router via UPnP; routers without it limit sharing to friends who can.
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
- [x] Group torrents: tracker and web seed. PROVEN (see P2P).
- [x] "Post to Discord" button for a night, through a channel webhook. The post shows the start time in each reader's own time zone and never pings anyone. PROVEN for the message format; INFERRED against real Discord.
- [x] Can start and restart a native Syncplay server. INFERRED, not run against a real `syncplayServer`.

Viewer app (Windows):
- [x] Paste a link or click "Open in The Last Piece" (`tlp://`, registered for the current user on first run). INFERRED on Windows.
- [x] Downloads each file by group torrent, public magnet or from the host, with progress, resume and fallback, then verifies. PROVEN on Linux.
- [x] Settings: your name, movie night folder, source preference, chat style, seed while open.
- [x] Join opens the bundled Syncplay and mpv straight into the room, with the first episode.
  - **No Syncplay connect window.** The app writes a `syncplay.ini` next to the bundled `Syncplay.exe`. Syncplay 1.7.6 reads a file there instead of the user's own settings (PROVEN in Syncplay's source, `_checkForPortableFile`). It sets `forceGuiPrompt = False` and turns off update pop-ups, so the bundled Syncplay doesn't touch anyone's own Syncplay setup.
  - If the app folder is read-only, the window appears again, filled in.
  - CI checks this on Windows: the bundled Syncplay has to join a real server with the file open, which it can't do if the window stopped it.
- [x] One window: a second link clicked while the app is open goes to the open window. PROVEN for the hand-over; INFERRED for the browser-to-app step on Windows.
- [x] Zip with `TheLastPiece.exe`, portable mpv with our `mpv.conf`, and portable Syncplay, built by GitHub Actions. Download it from the latest `dotnet` run under Actions.
- [ ] Tried by a person on a real Windows PC.

Phone:
- [ ] The host tests Synkplay on Android: connect to our server, open an ASS-heavy MKV from a shelf link, check sync and subtitles.

## Roadmap after the MVP

**Phase 2: polish, after the first real night**
- Show on the admin page who has downloaded what (the viewer reports progress to the host).
- Discord bot (a real bot, beyond the webhook post):
  - Shows who's downloaded and who's in the room.
  - Bridges Discord text into the on-screen chat.
- Signed installer and auto-update. Until then Windows SmartScreen warns on the unsigned `.exe` ("More info, Run anyway").
- Smaller download (see [Download size](#download-size)).

**Phase 3: going public**
- Accounts or invite links, several groups per host, per-room passwords.
- Setup wizard for hosts (ports, domain, HTTPS).
- macOS and Linux viewer builds (Avalonia and MonoTorrent run there; mpv and Syncplay bundling differs).
- License for our code, and third-party notices (Syncplay is Apache-2.0; the mpv Windows build bundles GPL parts, so we ship it unmodified with its license and a source link).
- Stays content-neutral: no built-in torrent search or index.

**Phase 4: only if needed**
- Our own Android app if Synkplay falls short.
- Zero-install browser fallback: a web player using JASSUB for subtitles, or an OBS → MediaMTX stream.

## Download size

The Windows zip is about 88 MB (it was about 120 MB before trimming). Each friend downloads it once. Sizes are from the CI build, zipped:

| Part | Zipped | Why it's there | Could it shrink? |
|---|---|---|---|
| mpv | 45.9 MB | The player. `mpv.exe` alone is 116 MB unzipped because the video decoders (FFmpeg) and subtitle renderer (libass) are built into it. | Only with a different mpv build. Hard to do safely before the first night. |
| Syncplay | 25.7 MB | Keeps everyone in step. It's a Python program packed with Python and the Qt window toolkit. | Dropping unused Qt parts might save a few MB (INFERRED, risky). Replacing Syncplay with our own sync code would save it all but means rebuilding the hardest part. Not worth it now. |
| The Last Piece | 16.3 MB | Our app, with .NET included so friends install nothing. | Already trimmed: unused .NET code is cut out at build time. |

Already removed: mpv's installer and updater scripts. For comparison, one 1080p anime episode is usually 1 to 1.5 GB, so the zip is well under the size of a single episode (INFERRED, typical release sizes).

## First test checklist

For the host and one friend before a real night. Each step says what to look for.

1. **Host.** Start the host (Docker or native), open the admin page, and create a night with one episode from the shelf. The night should show a "Group torrent" button after a minute (the torrent file is made in the background).
2. **Zip.** Download `TheLastPiece-win-x64` from the latest green `dotnet` run under Actions, unzip it, and run `TheLastPiece.exe`. Expect a SmartScreen warning ("More info", "Run anyway").
3. **Link.** Click the night link in a browser and choose "Open in The Last Piece". The app should open on that night. Click a second link while it's open: the same window should switch to it, with no second window.
4. **Download.** Press "Download all". It should say "group torrent" and finish with a green check. If you have two friends, start the second one after the first finishes and see that it's quick.
5. **Join.** Press "Join movie night". mpv should open straight into the room, with no Syncplay window. Pause on one PC: the other should pause.
6. **Look.** Check subtitles look right on an anime MKV, and press Enter in mpv to send a chat message.
7. **Discord (optional).** Set `TLP_DISCORD_WEBHOOK` and press "Post to Discord" on the night. Check the time shows in your own time zone.

If something goes wrong, the useful things to send back are a screenshot and which step it was.

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

Optional settings in `server/.env`:
- `TLP_DISCORD_WEBHOOK`: a Discord channel webhook (channel settings, Integrations, Webhooks). Adds a "Post to Discord" button to each night. Nothing is posted until you press it.
- `TLP_BEHIND_PROXY=true`: set this when using the Caddy HTTPS profile, so the group torrent sees friends' real addresses.
- `TLP_GROUP_TORRENTS=false`: turns group torrents off; friends then download from the host's copy or a magnet.

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
| `tests/Tlp.Tests` | 62 tests, including a group torrent through the real host app |
| `server/` | Compose file, Syncplay image, Caddy |
| `viewer/mpv.conf` | VLC look for the bundled mpv |
| `tools/` | Syncplay smoke test, viewer zip packager |
| `docs/options.md` | Every option considered and why |

## Open decisions

1. **Where does the first real night run?** WSL on your PC or the Linux server. Both are supported; this decides which gets tried first.
2. **A domain for HTTPS?** Friends can start on `http://<ip>:8080`. A domain gives HTTPS and a nicer link.
3. **Who's the test friend?** One person runs the zip on Windows before a real night.
4. **License** for the public release. MIT or Apache-2.0 is the usual choice.
